using Dapper;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;

namespace IIoT.Dapper.Production.QueryServices.Device;

/// <summary>
/// 设备身份读服务。
/// 按 DeviceId 读取设备的基础身份快照，供 edge 鉴别、worker 校验和内部事件处理复用。
/// </summary>
public class DeviceIdentityQueryService(
    IDbConnectionFactory connectionFactory,
    ICacheService cacheService) : IDeviceIdentityQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2);

    public async Task<DeviceIdentitySnapshot?> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty) return null;

        var cacheKey = CacheKeys.DeviceIdentity(deviceId);

        var cached = await cacheService.GetAsync<DeviceIdentitySnapshot>(
            cacheKey, cancellationToken);
        if (cached is not null) return cached;

        const string sql = @"
            SELECT
                id           AS DeviceId,
                client_code  AS Code
            FROM devices
            WHERE id = @DeviceId
            LIMIT 1";

        using var connection = connectionFactory.CreateConnection();

        var cmd = new CommandDefinition(
            sql,
            new { DeviceId = deviceId },
            cancellationToken: cancellationToken);

        var snapshot = await connection
            .QuerySingleOrDefaultAsync<DeviceIdentitySnapshot>(cmd);

        if (snapshot is not null)
            await cacheService.SetAsync(cacheKey, snapshot, CacheTtl, cancellationToken);

        return snapshot;
    }

    public async Task<bool> ExistsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetByDeviceIdAsync(deviceId, cancellationToken);
        return snapshot is not null;
    }
}
