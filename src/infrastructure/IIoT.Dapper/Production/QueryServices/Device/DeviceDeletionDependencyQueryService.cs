using Dapper;
using IIoT.Services.Contracts.RecordQueries;

namespace IIoT.Dapper.Production.QueryServices.Device;

public sealed class DeviceDeletionDependencyQueryService(IDbConnectionFactory connectionFactory)
    : IDeviceDeletionDependencyQueryService
{
    public async Task<DeviceDeletionDependencies> GetDependenciesAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select
                exists(select 1 from recipes where device_id = @DeviceId)            as HasRecipes,
                exists(select 1 from hourly_capacity where device_id = @DeviceId)    as HasCapacities,
                exists(select 1 from device_logs where device_id = @DeviceId)        as HasDeviceLogs,
                exists(select 1 from pass_station_records where device_id = @DeviceId) as HasPassStations;
            """;

        using var connection = connectionFactory.CreateConnection();
        var command = new CommandDefinition(sql, new { DeviceId = deviceId }, cancellationToken: cancellationToken);

        return await connection.QuerySingleAsync<DeviceDeletionDependencies>(command);
    }
}
