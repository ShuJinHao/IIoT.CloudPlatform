using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.Core.Production.Contracts.ClientReleases;

/// <summary>客户端首装包成功生成记录的只增不改持久化端口。</summary>
public interface IEdgeInstallerGenerationStore
{
    Task<bool> TryAddConfirmedAsync(
        EdgeInstallerGenerationRecord record,
        CancellationToken cancellationToken = default)
        => TryAddConfirmedAsync(record, [], cancellationToken);

    Task<bool> TryAddConfirmedAsync(
        EdgeInstallerGenerationRecord record,
        IReadOnlyCollection<EdgeInstallerPendingCredential> pendingCredentials,
        CancellationToken cancellationToken = default);

    Task<EdgeInstallerGenerationRecord?> GetByIdAsync(
        Guid generationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EdgeInstallerGenerationRecord>> GetRecentByDeviceAsync(
        Guid deviceId,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EdgeInstallerPendingCredential>> GetPendingByClientCodeAsync(
        string clientCode,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<EdgeInstallerPendingCredential?> GetPendingAsync(
        Guid generationId,
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<EdgeInstallerActivationAttempt> TryActivateAsync(
        Guid generationId,
        Guid deviceId,
        int processId,
        DateTime readyAtUtc,
        CancellationToken cancellationToken = default);

    Task<EdgeInstallerActivationAttempt> ConfirmActivationAsync(
        Guid generationId,
        Guid deviceId,
        int processId,
        DateTime readyAtUtc,
        CancellationToken cancellationToken = default);

    Task<bool> HasDownloadablePendingAsync(
        Guid generationId,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetPackageCleanupCandidateIdsAsync(
        DateTime nowUtc,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
