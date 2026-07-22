using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.SharedKernel.Architecture;

namespace IIoT.Core.Production.Contracts.ClientReleases;

/// <summary>
/// 发布组件永久删除操作记录的专用持久化端口。
/// 这些表不是业务聚合根，不通过通用 IRepository 暴露。
/// </summary>
public interface IClientReleaseComponentDeletionStore : IReadOnlyQueryPort
{
    Task<ClientReleaseComponentDeletion?> GetByIdAsync(
        Guid deletionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientReleaseComponentDeletion>> GetPendingAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClientReleaseComponentDeletion>> GetByChannelAsync(
        string channel,
        CancellationToken cancellationToken = default);

    void Add(ClientReleaseComponentDeletion deletion);

    void Remove(ClientReleaseComponentDeletion deletion);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
