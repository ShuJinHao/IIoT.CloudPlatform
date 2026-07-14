using IIoT.Dapper;
using IIoT.Dapper.Production.QueryServices.Device;
using Npgsql;
using Xunit;

namespace IIoT.EndToEndTests;

[Trait("Category", "PostgresPersistenceIntegration")]
[Trait("TestKind", "Integration")]
[Trait("Runtime", "Postgres")]
[Trait("Risk", "P0")]
[Trait("Capability", "DeviceIdentity")]
[Trait("Owner", "Cloud.Persistence")]
[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class DeviceIdentityQueryPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    [Fact]
    public async Task GetByDeviceIdAsync_MissingAndCurrentPostgresFacts_ReturnExpectedSnapshots()
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var testToken = testTimeout.Token;
        var connectionString = await fixture.GetConnectionStringAsync();
        var service = CreateService(connectionString, $"device-identity-current-{Guid.NewGuid():N}");
        var missingDeviceId = Guid.NewGuid();

        var missing = await service.GetByDeviceIdAsync(missingDeviceId, testToken);

        Assert.Null(missing);

        var deviceId = Guid.NewGuid();
        var code = $"DEV-PG-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        await InsertDeviceAsync(connectionString, deviceId, code, testToken);

        var current = await service.GetByDeviceIdAsync(deviceId, testToken);

        Assert.NotNull(current);
        Assert.Equal(deviceId, current.DeviceId);
        Assert.Equal(code, current.Code);
    }

    [Fact]
    public async Task GetByDeviceIdAsync_DirectPostgresMutation_IsVisibleOnSecondRead()
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var testToken = testTimeout.Token;
        var connectionString = await fixture.GetConnectionStringAsync();
        var service = CreateService(connectionString, $"device-identity-mutation-{Guid.NewGuid():N}");
        var deviceId = Guid.NewGuid();
        var originalCode = $"DEV-OLD-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        var updatedCode = $"DEV-NEW-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
        await InsertDeviceAsync(connectionString, deviceId, originalCode, testToken);

        var first = await service.GetByDeviceIdAsync(deviceId, testToken);
        await UpdateDeviceCodeAsync(connectionString, deviceId, updatedCode, testToken);
        var second = await service.GetByDeviceIdAsync(deviceId, testToken);

        Assert.NotNull(first);
        Assert.Equal(originalCode, first.Code);
        Assert.NotNull(second);
        Assert.Equal(updatedCode, second.Code);
    }

    [Fact]
    public async Task GetByDeviceIdAsync_CallerCancellationWhilePostgresQueryIsBlocked_Propagates()
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var testToken = testTimeout.Token;
        var connectionString = await fixture.GetConnectionStringAsync();
        var applicationName = $"device-identity-lock-wait-{Guid.NewGuid():N}";
        var service = CreateService(connectionString, applicationName);
        await using var blocker = new NpgsqlConnection(connectionString);
        await blocker.OpenAsync(testToken);
        await using var transaction = await blocker.BeginTransactionAsync(testToken);

        try
        {
            await using (var lockCommand = new NpgsqlCommand(
                             "LOCK TABLE devices IN ACCESS EXCLUSIVE MODE",
                             blocker,
                             transaction))
            {
                await lockCommand.ExecuteNonQueryAsync(testToken);
            }

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(testToken);
            var query = service.GetByDeviceIdAsync(Guid.NewGuid(), cancellation.Token);
            await WaitForLockWaitAsync(connectionString, applicationName, testToken);
            Assert.False(query.IsCompleted, "The observed PostgreSQL lock wait must still block the Dapper query.");

            cancellation.Cancel();

            var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => query.WaitAsync(TimeSpan.FromSeconds(5), testToken));
            Assert.Equal(cancellation.Token, actual.CancellationToken);
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await transaction.RollbackAsync(cleanupTimeout.Token);
        }
    }

    private static DeviceIdentityQueryService CreateService(
        string connectionString,
        string applicationName)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = applicationName
        };
        return new DeviceIdentityQueryService(new NpgsqlConnectionFactory(builder.ConnectionString));
    }

    private static async Task InsertDeviceAsync(
        string connectionString,
        Guid deviceId,
        string code,
        CancellationToken cancellationToken)
    {
        var processId = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (var processCommand = new NpgsqlCommand(
                         """
                         INSERT INTO mfg_processes (id, process_code, process_name)
                         VALUES (@processId, @processCode, @processName)
                         """,
                         connection))
        {
            processCommand.Parameters.AddWithValue("processId", processId);
            processCommand.Parameters.AddWithValue("processCode", $"PG-{unique}");
            processCommand.Parameters.AddWithValue("processName", $"Postgres {unique}");
            await processCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var deviceCommand = new NpgsqlCommand(
            """
            INSERT INTO devices (id, device_name, process_id, client_code)
            VALUES (@deviceId, @deviceName, @processId, @code)
            """,
            connection);
        deviceCommand.Parameters.AddWithValue("deviceId", deviceId);
        deviceCommand.Parameters.AddWithValue("deviceName", $"Postgres device {unique}");
        deviceCommand.Parameters.AddWithValue("processId", processId);
        deviceCommand.Parameters.AddWithValue("code", code);
        await deviceCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateDeviceCodeAsync(
        string connectionString,
        Guid deviceId,
        string code,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE devices SET client_code = @code WHERE id = @deviceId",
            connection);
        command.Parameters.AddWithValue("code", code);
        command.Parameters.AddWithValue("deviceId", deviceId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
    }

    private static async Task WaitForLockWaitAsync(
        string connectionString,
        string applicationName,
        CancellationToken testToken)
    {
        using var readinessTimeout = CancellationTokenSource.CreateLinkedTokenSource(testToken);
        readinessTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var readinessToken = readinessTimeout.Token;
        var observerBuilder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            ApplicationName = $"device-identity-lock-observer-{Guid.NewGuid():N}"
        };

        try
        {
            await using var observer = new NpgsqlConnection(observerBuilder.ConnectionString);
            await observer.OpenAsync(readinessToken);
            while (true)
            {
                await using var command = new NpgsqlCommand(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM pg_stat_activity AS activity
                        WHERE activity.application_name = @applicationName
                          AND activity.state = 'active'
                          AND activity.wait_event_type = 'Lock'
                          AND EXISTS (
                              SELECT 1
                              FROM pg_locks AS waiting_lock
                              WHERE waiting_lock.pid = activity.pid
                                AND NOT waiting_lock.granted
                          )
                    )
                    """,
                    observer);
                command.Parameters.AddWithValue("applicationName", applicationName);
                if (await command.ExecuteScalarAsync(readinessToken) is true)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), readinessToken);
            }
        }
        catch (OperationCanceledException) when (!testToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Dapper query with ApplicationName '{applicationName}' did not enter a PostgreSQL lock wait within 10 seconds.");
        }
    }
}
