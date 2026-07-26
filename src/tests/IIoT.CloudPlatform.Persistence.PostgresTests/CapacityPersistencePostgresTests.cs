using Dapper;
using IIoT.Core.Production.Contracts.RecordRepositories;
using IIoT.Dapper;
using IIoT.Dapper.Production.QueryServices.Capacity;
using IIoT.Dapper.Production.Repositories.Capacities;
using IIoT.Dapper.TypeHandlers;
using Npgsql;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class CapacityPersistencePostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    static CapacityPersistencePostgresTests()
    {
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
    }

    [Fact]
    public async Task UpsertAsync_LateSmallerSnapshotCannotReplaceCompletedClipCount()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var device = await InsertDeviceAsync(budget.ConnectionString, budget.Token);
        var repository = new HourlyCapacityRecordRepository(
            new NpgsqlConnectionFactory(budget.ConnectionString));
        var queryService = new CapacityQueryService(
            new NpgsqlConnectionFactory(budget.ConnectionString));
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var reportedAt = DateTime.UtcNow;

        try
        {
            await repository.UpsertAsync(
                CreateWriteModel(device.DeviceId, date, "CP09", 5, 4, 1, reportedAt),
                budget.Token);
            await repository.UpsertAsync(
                CreateWriteModel(device.DeviceId, date, "CP09", 3, 3, 0, reportedAt.AddMinutes(1)),
                budget.Token);

            var afterLateSmaller = Assert.Single(
                await queryService.GetHourlyByDeviceIdAsync(
                    device.DeviceId,
                    date,
                    cancellationToken: budget.Token));
            Assert.Equal(5, afterLateSmaller.TotalCount);
            Assert.Equal(4, afterLateSmaller.OkCount);
            Assert.Equal(1, afterLateSmaller.NgCount);

            await repository.UpsertAsync(
                CreateWriteModel(device.DeviceId, date, "CP09", 5, 4, 1, reportedAt.AddMinutes(2)),
                budget.Token);
            await repository.UpsertAsync(
                CreateWriteModel(device.DeviceId, date, "CP09", 7, 5, 2, reportedAt.AddMinutes(3)),
                budget.Token);

            var afterLarger = Assert.Single(
                await queryService.GetHourlyByDeviceIdAsync(
                    device.DeviceId,
                    date,
                    cancellationToken: budget.Token));
            Assert.Equal(7, afterLarger.TotalCount);
            Assert.Equal(afterLarger.TotalCount, afterLarger.OkCount + afterLarger.NgCount);
        }
        finally
        {
            await CleanupAsync(
                budget.ConnectionString,
                device.DeviceId,
                device.ProcessId);
        }
    }

    [Fact]
    public async Task Queries_SameTimeBucketPreservesEachPlcAndDefaultAggregateShape()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var device = await InsertDeviceAsync(budget.ConnectionString, budget.Token);
        var connectionFactory = new NpgsqlConnectionFactory(budget.ConnectionString);
        var repository = new HourlyCapacityRecordRepository(connectionFactory);
        var queryService = new CapacityQueryService(connectionFactory);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var reportedAt = DateTime.UtcNow;

        try
        {
            await repository.UpsertAsync(
                CreateWriteModel(device.DeviceId, date, "CP07", 4, 4, 0, reportedAt),
                budget.Token);
            await repository.UpsertAsync(
                CreateWriteModel(device.DeviceId, date, "CP09", 6, 5, 1, reportedAt),
                budget.Token);

            var hourly = await queryService.GetHourlyByDeviceIdAsync(
                device.DeviceId,
                date,
                cancellationToken: budget.Token);
            Assert.Equal(["CP07", "CP09"], hourly.Select(item => item.PlcName));

            var startTime = date.ToDateTime(TimeOnly.MinValue);
            var endTime = date.ToDateTime(new TimeOnly(23, 59, 59));
            var hourlyRange = await queryService.GetHourlyRangeByDeviceIdAsync(
                device.DeviceId,
                startTime,
                endTime,
                cancellationToken: budget.Token);
            Assert.Equal(["CP07", "CP09"], hourlyRange.Select(item => item.PlcName));

            var aggregate = Assert.Single(
                await queryService.GetSummaryRangeAsync(
                    device.DeviceId,
                    date,
                    date,
                    cancellationToken: budget.Token));
            var byPlc = await queryService.GetSummaryRangeAsync(
                device.DeviceId,
                date,
                date,
                cancellationToken: budget.Token,
                breakdownByPlc: true);

            Assert.Null(aggregate.PlcName);
            Assert.Equal(10, aggregate.TotalCount);
            Assert.Equal(["CP07", "CP09"], byPlc.Select(item => item.PlcName));
            Assert.Equal(aggregate.TotalCount, byPlc.Sum(item => item.TotalCount));
        }
        finally
        {
            await CleanupAsync(
                budget.ConnectionString,
                device.DeviceId,
                device.ProcessId);
        }
    }

    private static HourlyCapacityWriteModel CreateWriteModel(
        Guid deviceId,
        DateOnly date,
        string plcName,
        int totalCount,
        int okCount,
        int ngCount,
        DateTime reportedAt) =>
        new(
            Guid.NewGuid(),
            deviceId,
            date,
            "D",
            10,
            0,
            "10:00-10:30",
            totalCount,
            okCount,
            ngCount,
            plcName,
            reportedAt);

    private static async Task<(Guid DeviceId, Guid ProcessId)> InsertDeviceAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var deviceId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO mfg_processes (id, process_code, process_name)
            VALUES (@process_id, @process_code, @process_name);

            INSERT INTO devices (id, device_name, process_id, client_code)
            VALUES (@device_id, @device_name, @process_id, @client_code);
            """;
        command.Parameters.AddWithValue("process_id", processId);
        command.Parameters.AddWithValue("process_code", $"CAP-{unique}");
        command.Parameters.AddWithValue("process_name", $"Capacity {unique}");
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("device_name", $"Capacity device {unique}");
        command.Parameters.AddWithValue("client_code", $"CAP-{unique}"[..24]);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return (deviceId, processId);
    }

    private static async Task CleanupAsync(
        string connectionString,
        Guid deviceId,
        Guid processId)
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cleanup.Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM hourly_capacity WHERE device_id = @device_id;
            DELETE FROM devices WHERE id = @device_id;
            DELETE FROM mfg_processes WHERE id = @process_id;
            """;
        command.Parameters.AddWithValue("device_id", deviceId);
        command.Parameters.AddWithValue("process_id", processId);
        await command.ExecuteNonQueryAsync(cleanup.Token);
    }
}
