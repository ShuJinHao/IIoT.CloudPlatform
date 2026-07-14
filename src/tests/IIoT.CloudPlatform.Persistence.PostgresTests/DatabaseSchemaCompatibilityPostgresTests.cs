using IIoT.EntityFrameworkCore;
using IIoT.MigrationWorkApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class DatabaseSchemaCompatibilityPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
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
