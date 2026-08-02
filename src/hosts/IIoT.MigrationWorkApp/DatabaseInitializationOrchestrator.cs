using System.Data;
using System.Text;
using System.Text.Json;
using IIoT.Dapper.Initializers;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.MigrationWorkApp.SeedData;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.MigrationWorkApp;

public interface IDatabaseInitializationOrchestrator
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

public sealed class DatabaseInitializationOrchestrator
    : IDatabaseInitializationOrchestrator
{
    private readonly IIoTDbContext dbContext;
    private readonly UserManager<ApplicationUser> userManager;
    private readonly RoleManager<IdentityRole<Guid>> roleManager;
    private readonly IOidcClientSeeder oidcClientSeeder;
    private readonly IRecordSchemaInitializer recordSchemaInitializer;
    private readonly IConfiguration configuration;
    private readonly ILogger<DatabaseInitializationOrchestrator> logger;
    private readonly IServiceScopeFactory? scopeFactory;

    public DatabaseInitializationOrchestrator(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IOidcClientSeeder oidcClientSeeder,
        IRecordSchemaInitializer recordSchemaInitializer,
        IConfiguration configuration,
        ILogger<DatabaseInitializationOrchestrator> logger)
    {
        this.dbContext = dbContext;
        this.userManager = userManager;
        this.roleManager = roleManager;
        this.oidcClientSeeder = oidcClientSeeder;
        this.recordSchemaInitializer = recordSchemaInitializer;
        this.configuration = configuration;
        this.logger = logger;
    }

    public DatabaseInitializationOrchestrator(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<DatabaseInitializationOrchestrator> logger)
    {
        this.scopeFactory = scopeFactory;
        this.configuration = configuration;
        this.logger = logger;
        dbContext = null!;
        userManager = null!;
        roleManager = null!;
        oidcClientSeeder = null!;
        recordSchemaInitializer = null!;
    }

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

    private const string AdminLikeRolePreflightSql =
        """
        SELECT
            role."Id",
            role."Name",
            COUNT(user_role."UserId") AS user_count
        FROM "AspNetRoles" role
        LEFT JOIN "AspNetUserRoles" user_role
            ON user_role."RoleId" = role."Id"
        WHERE UPPER(BTRIM(COALESCE(role."Name", ''))) = 'ADMIN'
          AND COALESCE(role."Name", '') <> 'Admin'
        GROUP BY role."Id", role."Name"
        ORDER BY role."Name", role."Id";
        """;

    private const string PermissionClaimPreflightSql =
        """
        SELECT
            'role' AS owner_type,
            claim."Id"::text AS claim_id,
            role."Name" AS owner_name,
            claim."ClaimValue"
        FROM "AspNetRoleClaims" claim
        INNER JOIN "AspNetRoles" role
            ON role."Id" = claim."RoleId"
        WHERE claim."ClaimType" = 'permission'

        UNION ALL

        SELECT
            'user' AS owner_type,
            claim."Id"::text AS claim_id,
            "user"."UserName" AS owner_name,
            claim."ClaimValue"
        FROM "AspNetUserClaims" claim
        INNER JOIN "AspNetUsers" "user"
            ON "user"."Id" = claim."UserId"
        WHERE claim."ClaimType" = 'permission'

        ORDER BY owner_type, owner_name, claim_id;
        """;

    private const string NormalizeHourlyCapacityPrimaryKeySql =
        """
        DO $$
        DECLARE
            current_pk_columns text[];
        BEGIN
            SELECT array_agg(attribute.attname ORDER BY columns.ordinality)
            INTO current_pk_columns
            FROM pg_constraint con
            JOIN pg_class relation ON relation.oid = con.conrelid
            JOIN unnest(con.conkey) WITH ORDINALITY AS columns(attnum, ordinality) ON TRUE
            JOIN pg_attribute attribute ON attribute.attrelid = relation.oid
                                       AND attribute.attnum = columns.attnum
            WHERE relation.relname = 'hourly_capacity'
              AND con.contype = 'p'
            GROUP BY con.oid;

            IF current_pk_columns IS DISTINCT FROM ARRAY['id', 'date'] THEN
                ALTER TABLE hourly_capacity DROP CONSTRAINT IF EXISTS hourly_capacity_pkey;
                ALTER TABLE hourly_capacity ADD CONSTRAINT hourly_capacity_pkey PRIMARY KEY (id, date);
            END IF;
        END $$;
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await RunEfMigrationsAsync(cancellationToken);
        await InitializeRecordSchemasAsync(cancellationToken);
        await EnsureRecordSchemaCompatibilityAsync(cancellationToken);
        await InitializeTimescaleDbAsync(cancellationToken);
        await SeedSystemDataAsync(cancellationToken);
        await SeedOidcClientsAsync(cancellationToken);
    }

    private async Task RunEfMigrationsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("开始应用 EF Core 迁移。");

        await ExecuteFreshStageAsync(
            static (attempt, callbackToken) =>
                attempt.RunEfMigrationsAttemptAsync(callbackToken),
            cancellationToken);

        logger.LogInformation("EF Core 迁移完成。");
    }

    private async Task RunEfMigrationsAttemptAsync(CancellationToken cancellationToken)
    {
        await EnsureIdentityAuthorizationPreflightAsync(cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await EnsureIdentitySchemaCompatibilityAsync(cancellationToken);
        await EnsureDeviceCodeSchemaCompatibilityAsync(cancellationToken);
        await BackfillPassStationContentFingerprintsAsync(cancellationToken);

        var pendingMigrations = await dbContext.Database
            .GetPendingMigrationsAsync(cancellationToken);
        if (pendingMigrations.Any())
        {
            throw new InvalidOperationException(
                "EF Core migration postcondition failed: pending migrations remain.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    internal async Task BackfillPassStationContentFingerprintsAsync(
        CancellationToken cancellationToken)
    {
        var registrations = await dbContext.UploadReceiveRegistrations
            .Where(registration => registration.MessageType.StartsWith("pass-station:")
                                   && registration.ContentFingerprint == null)
            .OrderBy(registration => registration.Id)
            .ToListAsync(cancellationToken);
        if (registrations.Count == 0)
            return;

        var outboxIds = registrations
            .Select(registration => registration.OutboxMessageId)
            .Distinct()
            .ToArray();
        var outboxes = await dbContext.OutboxMessages
            .Where(message => outboxIds.Contains(message.Id))
            .ToDictionaryAsync(message => message.Id, cancellationToken);

        var failures = new List<Guid>();
        var fingerprints = new Dictionary<Guid, string>();
        foreach (var registration in registrations)
        {
            if (!outboxes.TryGetValue(registration.OutboxMessageId, out var outbox))
            {
                failures.Add(registration.Id);
                continue;
            }

            try
            {
                var @event = JsonSerializer.Deserialize<PassStationBatchReceivedEvent>(
                    outbox.Payload,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var expectedType = registration.MessageType["pass-station:".Length..];
                if (@event is null
                    || @event.DeviceId != registration.DeviceId
                    || string.IsNullOrWhiteSpace(@event.TypeKey)
                    || @event.Items is null
                    || @event.Items.Any(item => item is null)
                    || !string.Equals(
                        @event.TypeKey.Trim(),
                        expectedType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add(registration.Id);
                    continue;
                }

                fingerprints.Add(
                    registration.Id,
                    PassStationContentFingerprint.Compute(@event));
            }
            catch (JsonException)
            {
                failures.Add(registration.Id);
            }
            catch (ArgumentException)
            {
                failures.Add(registration.Id);
            }
            catch (InvalidOperationException)
            {
                failures.Add(registration.Id);
            }
            catch (NullReferenceException)
            {
                failures.Add(registration.Id);
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Cannot reconstruct pass-station upload content fingerprints from retained Outbox records. "
                + $"RegistrationIds=[{string.Join(",", failures)}]");
        }

        foreach (var registration in registrations)
            registration.BackfillContentFingerprint(fingerprints[registration.Id]);
        await dbContext.SaveChangesAsync(cancellationToken);

        var remainingIds = await dbContext.UploadReceiveRegistrations
            .Where(registration => registration.MessageType.StartsWith("pass-station:")
                                   && registration.ContentFingerprint == null)
            .OrderBy(registration => registration.Id)
            .Select(registration => registration.Id)
            .ToListAsync(cancellationToken);
        if (remainingIds.Count > 0)
        {
            throw new InvalidOperationException(
                "Pass-station upload content fingerprint backfill postcondition failed. "
                + $"RegistrationIds=[{string.Join(",", remainingIds)}]");
        }
    }

    internal async Task EnsureDeviceCodeSchemaCompatibilityAsync(CancellationToken cancellationToken)
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
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
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

    internal async Task EnsureIdentitySchemaCompatibilityAsync(CancellationToken cancellationToken)
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

    internal async Task EnsureCanonicalAdminRolePreflightAsync(
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Checking for non-canonical Admin-like identity roles.");

        if (!await IdentityAdminTablesExistAsync(cancellationToken))
        {
            return;
        }

        var conflicts = await GetAdminLikeRoleConflictsAsync(cancellationToken);
        if (conflicts.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(BuildAdminLikeRoleConflictMessage(conflicts));
    }

    internal async Task EnsureIdentityAuthorizationPreflightAsync(
        CancellationToken cancellationToken)
    {
        if (!await IdentityAdminTablesExistAsync(cancellationToken))
        {
            logger.LogInformation(
                "Identity admin tables do not exist yet; database authorization preflight is empty.");
            return;
        }

        await EnsureCanonicalAdminRolePreflightAsync(cancellationToken);
        await SystemInitData.EnsureSingleAdminAssignmentPreflightAsync(
            dbContext,
            cancellationToken);

        if (!await IdentityAuthorizationTablesExistAsync(cancellationToken))
        {
            logger.LogInformation(
                "Identity permission tables do not exist yet; permission-claim preflight is empty.");
            return;
        }

        var conflicts = await GetPermissionClaimConflictsAsync(cancellationToken);
        if (conflicts.Count > 0)
        {
            throw new InvalidOperationException(
                BuildPermissionClaimConflictMessage(conflicts));
        }
    }

    private async Task<bool> IdentityAdminTablesExistAsync(
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
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText =
                """
                SELECT
                    to_regclass('"AspNetRoles"') IS NOT NULL
                    AND to_regclass('"AspNetUsers"') IS NOT NULL
                    AND to_regclass('"AspNetUserRoles"') IS NOT NULL;
                """;

            return Convert.ToBoolean(
                await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<bool> IdentityAuthorizationTablesExistAsync(
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
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText =
                """
                SELECT
                    to_regclass('"AspNetRoles"') IS NOT NULL
                    AND to_regclass('"AspNetUsers"') IS NOT NULL
                    AND to_regclass('"AspNetUserRoles"') IS NOT NULL
                    AND to_regclass('"AspNetRoleClaims"') IS NOT NULL
                    AND to_regclass('"AspNetUserClaims"') IS NOT NULL;
                """;

            return Convert.ToBoolean(
                await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<List<AdminLikeRoleConflict>> GetAdminLikeRoleConflictsAsync(
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
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = AdminLikeRolePreflightSql;

            var conflicts = new List<AdminLikeRoleConflict>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                conflicts.Add(new AdminLikeRoleConflict(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetFieldValue<long>(2)));
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

    private static string BuildAdminLikeRoleConflictMessage(
        IReadOnlyCollection<AdminLikeRoleConflict> conflicts)
    {
        var details = conflicts.Select(conflict =>
            $"roleId={conflict.RoleId}, name={JsonSerializer.Serialize(conflict.Name)}, users={conflict.UserCount}");
        return "身份角色预检失败：发现非规范 Admin-like 角色。"
               + " 未执行自动合并、删除或用户角色变更；请管理员确认数据并定向处理后重试。"
               + " 异常角色："
               + string.Join("; ", details);
    }

    private async Task<List<PermissionClaimConflict>> GetPermissionClaimConflictsAsync(
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
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = PermissionClaimPreflightSql;

            var conflicts = new List<PermissionClaimConflict>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var ownerType = reader.GetString(0);
                var claimId = reader.GetString(1);
                var ownerName = reader.IsDBNull(2) ? null : reader.GetString(2);
                var permission = reader.IsDBNull(3) ? null : reader.GetString(3);
                var validation = CloudPermissionCatalog.Normalize([permission!]);

                if (!validation.IsValid)
                {
                    conflicts.Add(new PermissionClaimConflict(
                        ownerType,
                        claimId,
                        ownerName,
                        permission,
                        "PermissionNotDefined"));
                    continue;
                }

                if (!string.Equals(ownerType, "role", StringComparison.Ordinal))
                {
                    continue;
                }

                if (SystemRoles.IsCanonicalAdminRole(ownerName))
                {
                    continue;
                }

                var isRetiredDeviceAdminPermission =
                    string.Equals(
                        ownerName?.Trim(),
                        SystemRoles.DeviceAdmin,
                        StringComparison.OrdinalIgnoreCase)
                    && SystemRolePermissionTemplates.DeviceAdminRetiredPermissions.Contains(
                        validation.Permissions[0],
                        StringComparer.OrdinalIgnoreCase);
                if (isRetiredDeviceAdminPermission)
                {
                    continue;
                }

                var roleValidation = CloudPermissionCatalog.NormalizeForTargetRole(
                    ownerName,
                    validation.Permissions);
                if (!roleValidation.IsValid)
                {
                    conflicts.Add(new PermissionClaimConflict(
                        ownerType,
                        claimId,
                        ownerName,
                        permission,
                        "PermissionNotAssignableToRole"));
                }
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

    private static string BuildPermissionClaimConflictMessage(
        IReadOnlyCollection<PermissionClaimConflict> conflicts)
    {
        var details = conflicts.Select(conflict =>
            $"ownerType={conflict.OwnerType}, claimId={conflict.ClaimId}, "
            + $"owner={JsonSerializer.Serialize(conflict.OwnerName)}, "
            + $"permission={JsonSerializer.Serialize(conflict.Permission)}, "
            + $"reason={conflict.Reason}");
        return "身份权限预检失败：发现非法角色或个人权限声明。"
               + " 未执行 migration、seed、权限清理或账号变更；请管理员定向处理后重试。"
               + " 非法声明："
               + string.Join("; ", details);
    }

    private async Task InitializeRecordSchemasAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("开始初始化记录表 schema。");
        await ExecuteFreshStageAsync(
            static (attempt, callbackToken) =>
                attempt.InitializeRecordSchemasAttemptAsync(callbackToken),
            cancellationToken);
        logger.LogInformation("记录表 schema 初始化完成。");
    }

    private async Task InitializeRecordSchemasAttemptAsync(
        CancellationToken cancellationToken)
    {
        await recordSchemaInitializer.InitializeAsync(cancellationToken);
        if (!await RecordTablesExistAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Record schema postcondition failed: one or more required tables are missing.");
        }
    }

    private async Task EnsureRecordSchemaCompatibilityAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Checking record-table compatibility before Timescale conversion.");

        await ExecuteFreshStageAsync(
            static (attempt, callbackToken) =>
                attempt.EnsureRecordSchemaCompatibilityAttemptAsync(callbackToken),
            cancellationToken);
    }

    private async Task EnsureRecordSchemaCompatibilityAttemptAsync(
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            NormalizeHourlyCapacityPrimaryKeySql,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task InitializeTimescaleDbAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("开始初始化 TimescaleDB。");

        await ExecuteFreshStageAsync(
            static (attempt, callbackToken) =>
                attempt.InitializeTimescaleDbAttemptAsync(callbackToken),
            cancellationToken);

        logger.LogInformation("TimescaleDB 初始化完成。");
    }

    private async Task InitializeTimescaleDbAttemptAsync(
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE EXTENSION IF NOT EXISTS timescaledb;",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM timescaledb_information.hypertables
                        WHERE hypertable_name = 'pass_station_records'
                    ) THEN
                        PERFORM create_hypertable('pass_station_records', 'completed_time');
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

        await dbContext.Database.ExecuteSqlRawAsync(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM timescaledb_information.hypertables
                        WHERE hypertable_name = 'hourly_capacity'
                    ) THEN
                        -- Keep the existing slot-based unique index valid by partitioning on `date`,
                        -- which is already part of the idempotency/upsert key.
                        PERFORM create_hypertable('hourly_capacity', 'date');
                    END IF;
                END $$;",
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SeedSystemDataAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("开始播种系统初始化数据。");
        var retryTarget = SystemInitData.CreateRetryTarget();
        await ExecuteFreshStageAsync(
            (attempt, callbackToken) =>
                SystemInitData.SeedAttemptAsync(
                    attempt.dbContext,
                    attempt.userManager,
                    attempt.roleManager,
                    attempt.configuration,
                    retryTarget,
                    callbackToken),
            cancellationToken);
        logger.LogInformation("系统初始化数据播种完成。");
    }

    private async Task SeedOidcClientsAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("开始播种 OIDC client 配置。");
        await ExecuteFreshStageAsync(
            static (attempt, callbackToken) =>
                attempt.SeedOidcClientsAttemptAsync(callbackToken),
            cancellationToken);
        logger.LogInformation("OIDC client 配置播种完成。");
    }

    private async Task SeedOidcClientsAttemptAsync(
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var clientId = await oidcClientSeeder
            .EnsureAicopilotClientAsync(cancellationToken);
        var exists = await dbContext.OpenIddictApplications
            .AsNoTracking()
            .AnyAsync(
                application => application.ClientId == clientId,
                cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException(
                "OIDC client seed postcondition failed: configured ClientId is missing.");
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> RecordTablesExistAsync(
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
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText =
                """
                SELECT
                    to_regclass('device_logs') IS NOT NULL
                    AND to_regclass('hourly_capacity') IS NOT NULL
                    AND to_regclass('pass_station_records') IS NOT NULL;
                """;
            return Convert.ToBoolean(
                await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task ExecuteFreshStageAsync(
        Func<DatabaseInitializationOrchestrator, CancellationToken, Task> stage,
        CancellationToken cancellationToken)
    {
        if (scopeFactory is null)
        {
            var strategy = dbContext.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(
                callbackToken => stage(this, callbackToken),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

        await using var strategyScope = scopeFactory.CreateAsyncScope();
        var strategyContext = strategyScope.ServiceProvider
            .GetRequiredService<IIoTDbContext>();
        var executionStrategy = strategyContext.Database.CreateExecutionStrategy();
        await executionStrategy.ExecuteAsync(
            async callbackToken =>
            {
                await using var attemptScope = scopeFactory.CreateAsyncScope();
                var attemptServices = attemptScope.ServiceProvider;
                var attempt = new DatabaseInitializationOrchestrator(
                    attemptServices.GetRequiredService<IIoTDbContext>(),
                    attemptServices.GetRequiredService<UserManager<ApplicationUser>>(),
                    attemptServices.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
                    attemptServices.GetRequiredService<IOidcClientSeeder>(),
                    attemptServices.GetRequiredService<IRecordSchemaInitializer>(),
                    configuration,
                    logger);
                await stage(attempt, callbackToken);
            },
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private sealed record NormalizedClientCodeConflict(
        string NormalizedCode,
        long DuplicateCount);

    private sealed record AdminLikeRoleConflict(
        Guid RoleId,
        string Name,
        long UserCount);

    private sealed record PermissionClaimConflict(
        string OwnerType,
        string ClaimId,
        string? OwnerName,
        string? Permission,
        string Reason);
}
