using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.Core.Production.Contracts.ClientReleases;

/// <summary>客户端首装包成功生成记录的只增不改持久化端口。</summary>
public interface IEdgeInstallerGenerationStore
{
    Task<bool> TryAddConfirmedAsync(
        EdgeInstallerGenerationRecord record,
        CancellationToken cancellationToken = default);

    Task<EdgeInstallerGenerationRecord?> GetByIdAsync(
        Guid generationId,
        CancellationToken cancellationToken = default);
}
