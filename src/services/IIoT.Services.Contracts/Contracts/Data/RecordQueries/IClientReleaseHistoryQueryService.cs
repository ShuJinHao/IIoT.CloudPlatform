using IIoT.SharedKernel.Architecture;

namespace IIoT.Services.Contracts.RecordQueries;

public sealed record ClientReleaseHistoryQueryRequest(
    string? Channel,
    string? TargetRuntime,
    int Skip,
    int Take);

public sealed record ClientReleaseHistoryComponentReadItem(
    Guid ComponentId,
    string ComponentKind,
    string ComponentKey,
    string DisplayName,
    string Channel,
    string TargetRuntime,
    IReadOnlyList<ClientReleaseHistoryVersionReadItem> Versions,
    bool CanHardDelete = false);

public sealed record ClientReleaseHistoryVersionReadItem(
    Guid Id,
    string Version,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc,
    DateTime? DeletedAtUtc,
    string? DeletionReason,
    string? DeletionFailure,
    string? ReleaseNotes = null,
    string Sha256 = "",
    long PackageSize = 0,
    string? Publisher = null,
    string? Signature = null,
    string DownloadUrl = "",
    string HostApiVersion = "",
    string? TargetFramework = null,
    string? MinHostVersion = null,
    string? MaxHostVersion = null,
    string DependenciesJson = "[]",
    IReadOnlyList<ClientReleaseHistoryArtifactReadItem>? Artifacts = null);

public sealed record ClientReleaseHistoryArtifactReadItem(
    string ArtifactKind,
    string RelativePath,
    string? Sha256,
    long? Size);

/// <summary>
/// Human 客户端发布历史专用只读端口。
/// 历史状态过滤、总数、稳定排序和分页必须由持久化实现完成，
/// 应用层只能收到当前页，不能加载全部发布聚合后再分页。
/// </summary>
public interface IClientReleaseHistoryQueryService : IReadOnlyQueryPort
{
    Task<(IReadOnlyList<ClientReleaseHistoryComponentReadItem> Items, int TotalCount)> GetPagedAsync(
        ClientReleaseHistoryQueryRequest request,
        CancellationToken cancellationToken = default);
}
