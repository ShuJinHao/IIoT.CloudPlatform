using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.SharedKernel.Architecture;

namespace IIoT.Core.Production.Contracts.ClientReleases;

/// <summary>
/// 客户端版本快照、运行心跳和状态投影的专用持久化端口。
/// 这些表不是业务聚合根，不通过通用 IRepository 暴露。
/// </summary>
public interface IDeviceClientStateQueryService : IReadOnlyQueryPort
{
    Task<DeviceClientVersionSnapshot?> GetVersionSnapshotByDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceClientVersionSnapshot>> GetVersionSnapshotsByDevicesAsync(
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default);

    Task<EdgeDeviceRuntimeHeartbeat?> GetRuntimeHeartbeatByIdentityAsync(
        Guid deviceId,
        string clientCode,
        CancellationToken cancellationToken = default);

    Task<DeviceClientState?> GetStateByIdentityAsync(
        Guid deviceId,
        string clientCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceClientState>> GetStatesByDevicesAsync(
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default);
}

public interface IDeviceClientStateStore : IDeviceClientStateQueryService
{

    void AddVersionSnapshot(DeviceClientVersionSnapshot snapshot);

    void AddRuntimeHeartbeat(EdgeDeviceRuntimeHeartbeat heartbeat);

    void AddState(DeviceClientState state);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
