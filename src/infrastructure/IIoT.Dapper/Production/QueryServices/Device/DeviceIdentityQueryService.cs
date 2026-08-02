using Dapper;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.RecordQueries;

namespace IIoT.Dapper.Production.QueryServices.Device;

/// <summary>
/// 设备身份读服务。
/// 按 DeviceId 读取设备的基础身份快照，供 edge 鉴别、worker 校验和内部事件处理复用。
/// </summary>
internal class DeviceIdentityQueryService(
    IDbConnectionFactory connectionFactory) : IDeviceIdentityQueryService
{
    public async Task<DeviceIdentitySnapshot?> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (deviceId == Guid.Empty) return null;

        const string sql = @"
            SELECT
                d.id            AS DeviceId,
                d.client_code   AS Code,
                d.process_id    AS ProcessId,
                p.process_code  AS ProcessCode
            FROM devices d
            LEFT JOIN mfg_processes p ON p.id = d.process_id
            WHERE d.id = @DeviceId
            LIMIT 1";

        using var connection = connectionFactory.CreateConnection();

        var cmd = new ReadOnlyCommandDefinition(
            sql,
            new { DeviceId = deviceId },
            cancellationToken: cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<DeviceIdentitySnapshot>(cmd);
    }

    public async Task<bool> ExistsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetByDeviceIdAsync(deviceId, cancellationToken);
        return snapshot is not null;
    }
}
