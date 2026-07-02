namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// Edge 客户端发布记录状态。
/// </summary>
public enum ClientReleaseStatus
{
    Draft,
    Published,
    Deprecated,
    Archived,
    DeleteRequested,
    Deleted,
    DeleteFailed
}
