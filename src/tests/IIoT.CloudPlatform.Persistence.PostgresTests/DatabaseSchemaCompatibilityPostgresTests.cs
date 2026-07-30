using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Migrations;
using IIoT.MigrationWorkApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class DatabaseSchemaCompatibilityPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    [Fact]
    public async Task AdminLikeRolePreflight_ShouldFailWithoutMutatingHistoricalRole()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var testToken = budget.Token;
        await using var connection = new NpgsqlConnection(budget.ConnectionString);
        await connection.OpenAsync(testToken);
        await using var transaction = await connection.BeginTransactionAsync(testToken);
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connection)
            .Options;
        await using var dbContext = new IIoTDbContext(options);
        await dbContext.Database.UseTransactionAsync(transaction, testToken);
        var orchestrator = new DatabaseInitializationOrchestrator(
            dbContext,
            null!,
            null!,
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<DatabaseInitializationOrchestrator>.Instance);

        try
        {
            await orchestrator.EnsureCanonicalAdminRolePreflightAsync(testToken);

            var roleId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var unique = Guid.NewGuid().ToString("N");
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"""
                 INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
                 VALUES ('{roleId}'::uuid, ' Admin ', ' ADMIN-{unique}', '{unique}');

                 INSERT INTO "AspNetUsers" (
                     "Id",
                     "UserName",
                     "NormalizedUserName",
                     "EmailConfirmed",
                     "PhoneNumberConfirmed",
                     "TwoFactorEnabled",
                     "LockoutEnabled",
                     "AccessFailedCount")
                 VALUES (
                     '{userId}'::uuid,
                     'admin-like-{unique}',
                     'ADMIN-LIKE-{unique}',
                     FALSE,
                     FALSE,
                     FALSE,
                     TRUE,
                     0);

                 INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
                 VALUES ('{userId}'::uuid, '{roleId}'::uuid);
                 """,
                testToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => orchestrator.EnsureCanonicalAdminRolePreflightAsync(testToken));

            Assert.Contains(roleId.ToString(), exception.Message, StringComparison.Ordinal);
            Assert.Contains("name=\" Admin \"", exception.Message, StringComparison.Ordinal);
            Assert.Contains("users=1", exception.Message, StringComparison.Ordinal);
            Assert.Contains("未执行自动合并、删除或用户角色变更", exception.Message, StringComparison.Ordinal);
            Assert.Equal(
                " Admin ",
                await ExecuteScalarAsync(
                    connection,
                    transaction,
                    $"SELECT \"Name\" FROM \"AspNetRoles\" WHERE \"Id\" = '{roleId}'::uuid",
                    testToken,
                    static value => Convert.ToString(value)
                                    ?? throw new InvalidOperationException("Expected a role name.")));
        }
        finally
        {
            await PostgresTestBudget.RollbackAsync(transaction);
        }
    }

    [Fact]
    public async Task IdentityAuthorizationPreflight_ShouldAllowTargetedDeviceAdminCleanupButRejectUnknownPermission()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var testToken = budget.Token;
        await using var connection = new NpgsqlConnection(budget.ConnectionString);
        await connection.OpenAsync(testToken);
        await using var transaction = await connection.BeginTransactionAsync(testToken);
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connection)
            .Options;
        await using var dbContext = new IIoTDbContext(options);
        await dbContext.Database.UseTransactionAsync(transaction, testToken);
        var orchestrator = new DatabaseInitializationOrchestrator(
            dbContext,
            null!,
            null!,
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<DatabaseInitializationOrchestrator>.Instance);

        try
        {
            var deviceAdminRoleId = Guid.NewGuid();
            var customRoleId = Guid.NewGuid();
            var unique = Guid.NewGuid().ToString("N");
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"""
                 INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
                 VALUES
                    ('{deviceAdminRoleId}'::uuid, ' DeviceAdmin ', 'DEVICEADMIN-{unique}', '{unique}-device'),
                    ('{customRoleId}'::uuid, 'Supervisor-{unique}', 'SUPERVISOR-{unique}', '{unique}-custom');

                 INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue")
                 VALUES
                    ('{deviceAdminRoleId}'::uuid, 'permission', 'Device.Create');
                 """,
                testToken);

            await orchestrator.EnsureIdentityAuthorizationPreflightAsync(testToken);

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"""
                 INSERT INTO "AspNetRoleClaims" ("RoleId", "ClaimType", "ClaimValue")
                 VALUES
                    ('{customRoleId}'::uuid, 'permission', 'Permission.Forged');
                 """,
                testToken);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => orchestrator.EnsureIdentityAuthorizationPreflightAsync(testToken));

            Assert.Contains("Permission.Forged", exception.Message, StringComparison.Ordinal);
            Assert.Contains("PermissionNotDefined", exception.Message, StringComparison.Ordinal);
            Assert.Equal(
                1L,
                await ExecuteScalarAsync(
                    connection,
                    transaction,
                    $"""
                     SELECT COUNT(*)
                     FROM "AspNetRoleClaims"
                     WHERE "RoleId" = '{customRoleId}'::uuid
                       AND "ClaimValue" = 'Permission.Forged'
                     """,
                    testToken,
                    static value => Convert.ToInt64(value)));
            Assert.Equal(
                1L,
                await ExecuteScalarAsync(
                    connection,
                    transaction,
                    $"""
                     SELECT COUNT(*)
                     FROM "AspNetRoleClaims"
                     WHERE "RoleId" = '{deviceAdminRoleId}'::uuid
                       AND "ClaimValue" = 'Device.Create'
                     """,
                    testToken,
                    static value => Convert.ToInt64(value)));
        }
        finally
        {
            await PostgresTestBudget.RollbackAsync(transaction);
        }
    }

    [Fact]
    public async Task LegacyDeviceAndIdentitySchemas_ShouldUpgradeAgainstRealPostgres()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var testToken = budget.Token;
        await using var connection = new NpgsqlConnection(budget.ConnectionString);
        await connection.OpenAsync(testToken);
        await using var transaction = await connection.BeginTransactionAsync(testToken);
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connection)
            .Options;
        await using var dbContext = new IIoTDbContext(options);
        await dbContext.Database.UseTransactionAsync(transaction, testToken);
        var orchestrator = new DatabaseInitializationOrchestrator(
            dbContext,
            null!,
            null!,
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<DatabaseInitializationOrchestrator>.Instance);

        try
        {
            var unique = Guid.NewGuid().ToString("N");
            var processId = Guid.NewGuid();
            var firstDeviceId = Guid.NewGuid();
            var secondDeviceId = Guid.NewGuid();
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                TRUNCATE TABLE devices CASCADE;
                DROP INDEX IF EXISTS ix_devices_client_code;
                ALTER TABLE devices ADD COLUMN IF NOT EXISTS mac_address text;
                """,
                testToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"""
                 INSERT INTO mfg_processes (id, process_code, process_name)
                 VALUES ('{processId}'::uuid, 'PG-{unique}', 'Postgres {unique}');
                 INSERT INTO devices (id, device_name, process_id, client_code, mac_address)
                 VALUES
                    ('{firstDeviceId}'::uuid, 'Legacy first', '{processId}'::uuid, ' legacy-code ', '00:00:00:00:00:01'),
                    ('{secondDeviceId}'::uuid, 'Legacy second', '{processId}'::uuid, 'LEGACY-CODE', '00:00:00:00:00:02');
                 """,
                testToken);

            var conflict = await Assert.ThrowsAsync<InvalidOperationException>(
                () => orchestrator.EnsureDeviceCodeSchemaCompatibilityAsync(testToken));
            Assert.Contains("LEGACY-CODE (2)", conflict.Message, StringComparison.Ordinal);
            Assert.Equal(
                0L,
                await ExecuteScalarAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'devices' AND indexname = 'ix_devices_client_code'",
                    testToken,
                    static value => Convert.ToInt64(value)));

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"UPDATE devices SET client_code = 'OTHER-CODE' WHERE id = '{secondDeviceId}'::uuid",
                testToken);
            await orchestrator.EnsureDeviceCodeSchemaCompatibilityAsync(testToken);

            Assert.Equal(
                "LEGACY-CODE",
                await ExecuteScalarAsync(
                    connection,
                    transaction,
                    $"SELECT client_code FROM devices WHERE id = '{firstDeviceId}'::uuid",
                    testToken,
                    static value => Convert.ToString(value)
                                    ?? throw new InvalidOperationException("Expected a non-null scalar string.")));
            Assert.Equal(
                1L,
                await ExecuteScalarAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM pg_indexes WHERE tablename = 'devices' AND indexname = 'ix_devices_client_code'",
                    testToken,
                    static value => Convert.ToInt64(value)));
            Assert.Equal(
                0L,
                await ExecuteScalarAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM information_schema.columns WHERE lower(table_name) = 'devices' AND lower(column_name) = 'mac_address'",
                    testToken,
                    static value => Convert.ToInt64(value)));

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                "ALTER TABLE \"AspNetUsers\" RENAME COLUMN \"IsEnabled\" TO is_enabled",
                testToken);
            await orchestrator.EnsureIdentitySchemaCompatibilityAsync(testToken);

            Assert.Equal(
                1L,
                await ExecuteScalarAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM information_schema.columns WHERE lower(table_name) = 'aspnetusers' AND column_name = 'IsEnabled'",
                    testToken,
                    static value => Convert.ToInt64(value)));
            Assert.Equal(
                0L,
                await ExecuteScalarAsync(
                    connection,
                    transaction,
                    "SELECT COUNT(*) FROM information_schema.columns WHERE lower(table_name) = 'aspnetusers' AND column_name = 'is_enabled'",
                    testToken,
                    static value => Convert.ToInt64(value)));
        }
        finally
        {
            await PostgresTestBudget.RollbackAsync(transaction);
        }
    }

    [Fact]
    public async Task PlcSnapshotMarkerMigration_ShouldFenceEveryRegisteredDevice()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        await using var connection =
            new NpgsqlConnection(budget.ConnectionString);
        await connection.OpenAsync(budget.Token);
        await using var transaction =
            await connection.BeginTransactionAsync(budget.Token);
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connection)
            .Options;
        await using var dbContext = new IIoTDbContext(options);
        await dbContext.Database.UseTransactionAsync(
            transaction,
            budget.Token);

        try
        {
            var unique = Guid.NewGuid().ToString("N");
            var process = new MfgProcess(
                $"PLC-MIG-{unique}"[..24],
                "PLC migration marker");
            var existingStateDevice = new Device(
                $"Existing state {unique}",
                $"PLC-E-{unique}"[..24],
                process.Id);
            var missingStateDevice = new Device(
                $"Missing state {unique}",
                $"PLC-M-{unique}"[..24],
                process.Id);
            var emptySnapshotDevice = new Device(
                $"Empty snapshot {unique}",
                $"PLC-Z-{unique}"[..24],
                process.Id);
            var futureSnapshotDevice = new Device(
                $"Future snapshot {unique}",
                $"PLC-F-{unique}"[..24],
                process.Id);
            var registeredOnlyDevice = new Device(
                $"Registered only {unique}",
                $"PLC-R-{unique}"[..24],
                process.Id);
            var emptySnapshotReceivedAt =
                DateTime.UtcNow.AddMinutes(-3);
            var existingState = new DeviceClientState(
                existingStateDevice.Id,
                existingStateDevice.Code);
            var emptySnapshotState = new DeviceClientState(
                emptySnapshotDevice.Id,
                emptySnapshotDevice.Code,
                createdAtUtc: emptySnapshotReceivedAt);
            var olderObservedAt = DateTime.UtcNow.AddMinutes(-10);
            var latestObservedAt = olderObservedAt.AddMinutes(5);
            var snapshotReceivedAt = latestObservedAt.AddMinutes(4);
            var existingRuntime = CreateRuntimeState(
                existingStateDevice,
                "PLC-1",
                latestObservedAt);
            var missingRuntime = CreateRuntimeState(
                missingStateDevice,
                "PLC-2",
                olderObservedAt);
            var futureObservedAt = snapshotReceivedAt.AddHours(1);
            var futureRuntime = CreateRuntimeState(
                futureSnapshotDevice,
                "PLC-3",
                futureObservedAt);
            process.ClearDomainEvents();
            existingStateDevice.ClearDomainEvents();
            missingStateDevice.ClearDomainEvents();
            emptySnapshotDevice.ClearDomainEvents();
            futureSnapshotDevice.ClearDomainEvents();
            registeredOnlyDevice.ClearDomainEvents();
            dbContext.MfgProcesses.Add(process);
            dbContext.Devices.AddRange(
                existingStateDevice,
                missingStateDevice,
                emptySnapshotDevice,
                futureSnapshotDevice,
                registeredOnlyDevice);
            dbContext.DeviceClientStates.AddRange(
                existingState,
                emptySnapshotState);
            dbContext.EdgeHostPlcRuntimeStates.AddRange(
                existingRuntime,
                missingRuntime,
                futureRuntime);
            await dbContext.SaveChangesAsync(budget.Token);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 update edge_host_plc_runtime_states
                 set updated_at_utc = {snapshotReceivedAt}
                 where device_id in (
                     {existingStateDevice.Id},
                     {missingStateDevice.Id},
                     {futureSnapshotDevice.Id})
                 """,
                budget.Token);

            var migrationStartedAt = await ExecuteScalarAsync(
                connection,
                transaction,
                "select statement_timestamp()",
                budget.Token,
                static value => (DateTime)value!);
            var migration = new AddPlcSnapshotCommitRecoveryMarker();
            var backfillSql = Assert.Single(
                migration.UpOperations.OfType<SqlOperation>()).Sql;
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                backfillSql,
                budget.Token);
            var migrationCompletedAt = await ExecuteScalarAsync(
                connection,
                transaction,
                "select statement_timestamp()",
                budget.Token,
                static value => (DateTime)value!);
            var migratedDeviceIds = new[]
            {
                existingStateDevice.Id,
                missingStateDevice.Id,
                emptySnapshotDevice.Id,
                futureSnapshotDevice.Id,
                registeredOnlyDevice.Id
            };
            dbContext.ChangeTracker.Clear();
            var firstMarkers = await dbContext.DeviceClientStates
                .AsNoTracking()
                .Where(state => migratedDeviceIds.Contains(state.DeviceId))
                .ToDictionaryAsync(
                    state => state.DeviceId,
                    state => new
                    {
                        state.PlcSnapshotReportedAtUtc,
                        state.PlcSnapshotReceivedAtUtc,
                        state.PlcSnapshotContentSha256
                    },
                    budget.Token);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                backfillSql,
                budget.Token);

            dbContext.ChangeTracker.Clear();
            var states = await dbContext.DeviceClientStates
                .AsNoTracking()
                .Where(state =>
                    state.DeviceId == existingStateDevice.Id
                    || state.DeviceId == missingStateDevice.Id
                    || state.DeviceId == emptySnapshotDevice.Id
                    || state.DeviceId == futureSnapshotDevice.Id
                    || state.DeviceId == registeredOnlyDevice.Id)
                .OrderBy(state => state.DeviceId)
                .ToListAsync(budget.Token);

            Assert.Equal(5, states.Count);
            var backfilledExisting = Assert.Single(
                states,
                state => state.DeviceId == existingStateDevice.Id);
            var backfilledMissing = Assert.Single(
                states,
                state => state.DeviceId == missingStateDevice.Id);
            var backfilledEmpty = Assert.Single(
                states,
                state => state.DeviceId == emptySnapshotDevice.Id);
            var backfilledFuture = Assert.Single(
                states,
                state => state.DeviceId == futureSnapshotDevice.Id);
            var backfilledRegisteredOnly = Assert.Single(
                states,
                state => state.DeviceId == registeredOnlyDevice.Id);
            Assert.NotEqual(
                snapshotReceivedAt,
                backfilledExisting.PlcSnapshotReportedAtUtc);
            Assert.NotEqual(
                futureObservedAt,
                backfilledFuture.PlcSnapshotReportedAtUtc);
            foreach (var state in states)
            {
                var firstMarker = firstMarkers[state.DeviceId];
                Assert.Equal(
                    firstMarker.PlcSnapshotReportedAtUtc,
                    state.PlcSnapshotReportedAtUtc);
                Assert.Equal(
                    firstMarker.PlcSnapshotReceivedAtUtc,
                    state.PlcSnapshotReceivedAtUtc);
                Assert.Equal(
                    firstMarker.PlcSnapshotContentSha256,
                    state.PlcSnapshotContentSha256);
                AssertMarker(
                    state,
                    migrationStartedAt,
                    migrationCompletedAt);
            }
            Assert.Equal("[]", backfilledMissing.VersionLocalIpAddressesJson);
            Assert.Equal("[]", backfilledMissing.RuntimeLocalIpAddressesJson);
            Assert.Equal("[]", backfilledRegisteredOnly.VersionLocalIpAddressesJson);
            Assert.Equal("[]", backfilledRegisteredOnly.RuntimeLocalIpAddressesJson);
        }
        finally
        {
            await PostgresTestBudget.RollbackAsync(transaction);
        }
    }

    private static EdgeHostPlcRuntimeState CreateRuntimeState(
        Device device,
        string plcCode,
        DateTime observedAtUtc)
    {
        var state = new EdgeHostPlcRuntimeState(
            device.Id,
            device.Code,
            plcCode,
            createdAtUtc: observedAtUtc.AddMinutes(-1));
        state.ReplaceReport(
            $"Reported {plcCode}",
            true,
            EdgeHostPlcRuntimeStatus.Connected,
            observedAtUtc);
        return state;
    }

    private static void AssertMarker(
        DeviceClientState state,
        DateTime migrationStartedAt,
        DateTime migrationCompletedAt)
    {
        Assert.NotNull(state.PlcSnapshotReceivedAtUtc);
        Assert.InRange(
            state.PlcSnapshotReceivedAtUtc.Value,
            migrationStartedAt,
            migrationCompletedAt);
        Assert.Equal(
            DateTime.MaxValue,
            state.PlcSnapshotReportedAtUtc);
        Assert.Equal(
            new string('0', 64),
            state.PlcSnapshotContentSha256);
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        Func<object?, T> convert)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return convert(await command.ExecuteScalarAsync(cancellationToken));
    }
}
