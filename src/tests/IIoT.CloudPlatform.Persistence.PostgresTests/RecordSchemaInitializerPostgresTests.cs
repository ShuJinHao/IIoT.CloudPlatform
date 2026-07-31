using IIoT.Dapper;
using IIoT.Dapper.Initializers;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class RecordSchemaInitializerPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    [Fact]
    public async Task RecordSchemas_FirstAndWarmRun_ShouldConvergeToRequiredTables()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var baseConnectionString = budget.ConnectionString;
        var schemaName = $"record_schema_{Guid.NewGuid():N}";
        await CreateSchemaAsync(baseConnectionString, schemaName, budget.Token);
        try
        {
            var connectionString = WithSearchPath(baseConnectionString, schemaName);
            var initializer = new RecordSchemaInitializer(
                new NpgsqlConnectionFactory(connectionString),
                NullLogger<RecordSchemaInitializer>.Instance);

            await initializer.InitializeAsync(budget.Token);
            await initializer.InitializeAsync(budget.Token);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(budget.Token);
            await using var command = new NpgsqlCommand(
                """
                SELECT
                    to_regclass('device_logs') IS NOT NULL
                    AND to_regclass('hourly_capacity') IS NOT NULL
                    AND to_regclass('pass_station_records') IS NOT NULL;
                """,
                connection);
            Assert.True(Convert.ToBoolean(
                await command.ExecuteScalarAsync(budget.Token)));
        }
        finally
        {
            await DropSchemaAsync(baseConnectionString, schemaName);
        }
    }

    [Fact]
    public async Task RecordSchemaInterruption_ShouldRollbackWholeScriptBatch()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var baseConnectionString = budget.ConnectionString;
        var schemaName = $"record_schema_failure_{Guid.NewGuid():N}";
        var scriptDirectory = Path.Combine(
            Path.GetTempPath(),
            $"iiot-record-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scriptDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(scriptDirectory, "001_create_probe.sql"),
            "CREATE TABLE interruption_probe(id integer PRIMARY KEY);",
            budget.Token);
        await File.WriteAllTextAsync(
            Path.Combine(scriptDirectory, "002_insert_probe.sql"),
            "INSERT INTO interruption_probe(id) VALUES (1);",
            budget.Token);
        await File.WriteAllTextAsync(
            Path.Combine(scriptDirectory, "003_fail.sql"),
            "SELECT iiot_missing_schema_function();",
            budget.Token);
        await CreateSchemaAsync(baseConnectionString, schemaName, budget.Token);

        try
        {
            var connectionString = WithSearchPath(baseConnectionString, schemaName);
            var initializer = new RecordSchemaInitializer(
                new NpgsqlConnectionFactory(connectionString),
                NullLogger<RecordSchemaInitializer>.Instance,
                scriptDirectory);

            await Assert.ThrowsAsync<PostgresException>(
                () => initializer.InitializeAsync(budget.Token));

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(budget.Token);
            await using var command = new NpgsqlCommand(
                "SELECT to_regclass('interruption_probe') IS NULL;",
                connection);
            Assert.True(Convert.ToBoolean(
                await command.ExecuteScalarAsync(budget.Token)));
        }
        finally
        {
            Directory.Delete(scriptDirectory, recursive: true);
            await DropSchemaAsync(baseConnectionString, schemaName);
        }
    }

    private static async Task CreateSchemaAsync(
        string connectionString,
        string schemaName,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"CREATE SCHEMA \"{schemaName}\";",
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DropSchemaAsync(
        string connectionString,
        string schemaName)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(timeout.Token);
        await using var command = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;",
            connection);
        await command.ExecuteNonQueryAsync(timeout.Token);
    }

    private static string WithSearchPath(
        string connectionString,
        string schemaName)
        => new NpgsqlConnectionStringBuilder(connectionString)
        {
            SearchPath = schemaName,
            ApplicationName = $"record-schema-{Guid.NewGuid():N}"
        }.ConnectionString;
}
