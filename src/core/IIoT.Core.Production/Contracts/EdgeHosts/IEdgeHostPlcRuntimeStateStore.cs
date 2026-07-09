using IIoT.Core.Production.Aggregates.EdgeHosts;

namespace IIoT.Core.Production.Contracts.EdgeHosts;

/// <summary>
/// 上位机 PLC runtime state 的专用持久化端口。
/// 状态投影不是业务聚合根，不通过通用 IRepository 暴露。
/// </summary>
public interface IEdgeHostPlcRuntimeStateStore
{
    Task<IReadOnlyList<EdgeHostPlcRuntimeState>> GetByIdentityAsync(
        Guid deviceId,
        string clientCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EdgeHostPlcRuntimeState>> GetByDevicesAsync(
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default);

    void Add(EdgeHostPlcRuntimeState state);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
