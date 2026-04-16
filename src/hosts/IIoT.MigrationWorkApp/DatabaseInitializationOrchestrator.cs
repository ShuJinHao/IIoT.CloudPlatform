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
            await dbContext.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_devices_mac_address_client_code ON devices (mac_address, client_code);",
                cancellationToken);
        });

        logger.LogInformation("EF Core 迁移完成。");
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
}
