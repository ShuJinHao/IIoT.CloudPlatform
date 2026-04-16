using System.Data;
using System.Text;
using IIoT.Dapper.Initializers;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.MigrationWorkApp.SeedData;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace IIoT.MigrationWorkApp;

public interface IDatabaseInitializationOrchestrator
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

public sealed class DatabaseInitializationOrchestrator(
    IIoTDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IRecordSchemaInitializer recordSchemaInitializer,
    IConfiguration configuration,
    ILogger<DatabaseInitializationOrchestrator> logger)
    : IDatabaseInitializationOrchestrator
{
    private const string DuplicateNormalizedDeviceCodeCheckSql =
        """
        SELECT normalized_code, duplicate_count
        FROM (
            SELECT
                COALESCE(NULLIF(UPPER(BTRIM(client_code)), ''), '<EMPTY>') AS normalized_code,
                COUNT(*) AS duplicate_count
            FROM devices
            GROUP BY COALESCE(NULLIF(UPPER(BTRIM(client_code)), ''), '<EMPTY>')
            HAVING COUNT(*) > 1
        ) conflicts
        ORDER BY normalized_code;
        """;

    private const string NormalizeAndRebuildDeviceCodeIndexSql =
        """
        UPDATE devices
        SET client_code = UPPER(BTRIM(client_code))
        WHERE client_code IS NOT NULL
          AND client_code <> UPPER(BTRIM(client_code));

        DROP INDEX IF EXISTS ix_devices_mac_address_client_code;
        CREATE UNIQUE INDEX IF NOT EXISTS ix_devices_client_code ON devices (client_code);
        ALTER TABLE devices DROP COLUMN IF EXISTS mac_address;
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await RunEfMigrationsAsync(cancellationToken);
        await InitializeRecordSchemasAsync(cancellationToken);
        await InitializeTimescaleDbAsync(cancellationToken);
        await SeedSystemDataAsync(cancellationToken);
    }

    private async Task RunEfMigrationsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("开始应用 EF Core 迁移。");

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
            await EnsureIdentitySchemaCompatibilityAsync(cancellationToken);
            await EnsureDeviceCodeSchemaCompatibilityAsync(cancellationToken);
        });

        logger.LogInformation("EF Core 迁移完成。");
    }

    private async Task EnsureDeviceCodeSchemaCompatibilityAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Checking legacy device code compatibility before rebuilding the unique index.");

        var conflicts = await GetNormalizedClientCodeConflictsAsync(cancellationToken);
        if (conflicts.Count > 0)
        {
            throw new InvalidOperationException(BuildNormalizedClientCodeConflictMessage(conflicts));
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            NormalizeAndRebuildDeviceCodeIndexSql,
            cancellationToken);
    }

    private async Task<List<NormalizedClientCodeConflict>> GetNormalizedClientCodeConflictsAsync(
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = DuplicateNormalizedDeviceCodeCheckSql;

            var conflicts = new List<NormalizedClientCodeConflict>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                conflicts.Add(new NormalizedClientCodeConflict(
                    reader.GetString(0),
                    reader.GetFieldValue<long>(1)));
            }

            return conflicts;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string BuildNormalizedClientCodeConflictMessage(
        IReadOnlyCollection<NormalizedClientCodeConflict> conflicts)
    {
        var builder = new StringBuilder(
            "设备 Code 升级已被阻止：标准化后的 client_code 存在重复，无法创建唯一索引。");
        builder.Append(" 请先清理以下冲突后再重新启动迁移：");
        builder.Append(string.Join(
            ", ",
            conflicts.Select(conflict => $"{conflict.NormalizedCode} ({conflict.DuplicateCount})")));

        return builder.ToString();
    }

    private async Task EnsureIdentitySchemaCompatibilityAsync(CancellationToken cancellationToken)
    {
        // Repair drifted dev databases whose migration history is ahead of the actual schema.
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DO $$
            DECLARE
                source_col text;
                has_is_enabled boolean;
            BEGIN
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE lower(table_name) = 'aspnetusers'
                      AND lower(column_name) = 'isenabled'
                )
                INTO has_is_enabled;

                IF NOT has_is_enabled THEN
                    SELECT column_name
                    INTO source_col
                    FROM information_schema.columns
                    WHERE lower(table_name) = 'aspnetusers'
                      AND lower(column_name) IN ('isenabled', 'is_enabled')
                    ORDER BY CASE WHEN lower(column_name) = 'isenabled' THEN 0 ELSE 1 END
                    LIMIT 1;

                    IF source_col IS NOT NULL THEN
                        EXECUTE format('ALTER TABLE "AspNetUsers" RENAME COLUMN %I TO "IsEnabled"', source_col);
                    ELSE
                        ALTER TABLE "AspNetUsers"
                        ADD COLUMN "IsEnabled" boolean NOT NULL DEFAULT TRUE;
                    END IF;
                END IF;

                UPDATE "AspNetUsers"
                SET "IsEnabled" = TRUE
                WHERE "IsEnabled" IS NULL;
            END $$;
            """,
            cancellationToken);
    }

    private async Task InitializeRecordSchemasAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("开始初始化记录表 schema。");
        await recordSchemaInitializer.InitializeAsync(cancellationToken);
        logger.LogInformation("记录表 schema 初始化完成。");
    }

    private async Task InitializeTimescaleDbAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("开始初始化 TimescaleDB。");

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE EXTENSION IF NOT EXISTS timescaledb;",
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM timescaledb_information.hypertables
                        WHERE hypertable_name = 'pass_data_injection'
                    ) THEN
                        PERFORM create_hypertable('pass_data_injection', 'completed_time');
                    END IF;
                END $$;",
                cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM timescaledb_information.hypertables
                        WHERE hypertable_name = 'device_logs'
                    ) THEN
                        PERFORM create_hypertable('device_logs', 'log_time');
                    END IF;
                END $$;",
                cancellationToken);
        });

        logger.LogInformation("TimescaleDB 初始化完成。");
    }

    private async Task SeedSystemDataAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("开始播种系统初始化数据。");
        await SystemInitData.SeedAsync(
            dbContext,
            userManager,
            roleManager,
            configuration,
            cancellationToken);
        logger.LogInformation("系统初始化数据播种完成。");
    }

    private sealed record NormalizedClientCodeConflict(
        string NormalizedCode,
        long DuplicateCount);
}
