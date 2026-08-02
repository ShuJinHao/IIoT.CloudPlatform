using Dapper;
using IIoT.Services.Contracts.RecordQueries;

namespace IIoT.Dapper.Production.QueryServices.Device;

internal sealed class DeviceOperationalStatusQueryService(IDbConnectionFactory connectionFactory)
    : IDeviceOperationalStatusQueryService
{
    public async Task<IReadOnlyList<DeviceOperationalStatusTarget>> GetScopedDevicesAsync(
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default)
    {
        if (deviceIds is { Count: 0 })
            return [];

        using var connection = connectionFactory.CreateConnection();
        var conditions = "WHERE 1=1";
        var parameters = new DynamicParameters();
        if (deviceIds is { Count: > 0 })
        {
            conditions += " AND d.id = ANY(@DeviceIds)";
            parameters.Add("DeviceIds", deviceIds.ToArray());
        }

        var sql = $"""
            SELECT
                d.id AS DeviceId,
                d.client_code AS ClientCode
            FROM devices d
            {conditions}
            ORDER BY d.client_code
            """;

        var command = new ReadOnlyCommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken);
        return (await connection.QueryAsync<DeviceOperationalStatusTarget>(command)).ToList();
    }
}
