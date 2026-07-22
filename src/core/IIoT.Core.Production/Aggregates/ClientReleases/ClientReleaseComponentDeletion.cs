using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// 发布组件永久删除的持久化操作记录。不是业务聚合根，不通过通用 IRepository 暴露；
/// 与组件元数据删除在同一数据库事务写入，驱动后续可重试、可恢复的幂等文件清理。
/// </summary>
public sealed class ClientReleaseComponentDeletion : BaseEntity<Guid>
{
    private readonly List<ClientReleaseComponentDeletionFile> _files = [];

    private ClientReleaseComponentDeletion()
    {
    }

    public ClientReleaseComponentDeletion(
        Guid componentId,
        string componentKind,
        string componentKey,
        string channel,
        string targetRuntime,
        IReadOnlyList<string> versions,
        string? reason,
        IReadOnlyList<string> relativePaths)
    {
        if (relativePaths.Count == 0)
        {
            throw new ArgumentException("删除操作必须包含至少一个受控文件目标。", nameof(relativePaths));
        }

        Id = Guid.NewGuid();
        ComponentId = componentId;
        ComponentKind = ClientReleaseComponent.NormalizeRequired(componentKind, nameof(componentKind));
        ComponentKey = ClientReleaseComponent.NormalizeRequired(componentKey, nameof(componentKey));
        Channel = ClientReleaseComponent.NormalizeRequired(channel, nameof(channel));
        TargetRuntime = ClientReleaseComponent.NormalizeRequired(targetRuntime, nameof(targetRuntime));
        VersionsJson = System.Text.Json.JsonSerializer.Serialize(
            versions.OrderBy(version => version, StringComparer.OrdinalIgnoreCase).ToArray());
        Reason = ClientReleaseComponent.NormalizeOptional(reason);
        Status = ClientReleaseComponentDeletionStatus.Requested;
        RetryCount = 0;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        foreach (var relativePath in relativePaths.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
        {
            _files.Add(new ClientReleaseComponentDeletionFile(Id, relativePath));
        }
    }

    public Guid ComponentId { get; private set; }

    public string ComponentKind { get; private set; } = null!;

    public string ComponentKey { get; private set; } = null!;

    public string Channel { get; private set; } = null!;

    public string TargetRuntime { get; private set; } = null!;

    public string VersionsJson { get; private set; } = "[]";

    public string? Reason { get; private set; }

    public ClientReleaseComponentDeletionStatus Status { get; private set; }

    public string? FailureCode { get; private set; }

    public int RetryCount { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<ClientReleaseComponentDeletionFile> Files => _files.AsReadOnly();

    public void MarkFailed(string failureCode)
    {
        Status = ClientReleaseComponentDeletionStatus.Failed;
        FailureCode = ClientReleaseComponent.NormalizeRequired(failureCode, nameof(failureCode));
        RetryCount += 1;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ResetForRetry()
    {
        Status = ClientReleaseComponentDeletionStatus.Requested;
        FailureCode = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed class ClientReleaseComponentDeletionFile : BaseEntity<Guid>
{
    private ClientReleaseComponentDeletionFile()
    {
    }

    internal ClientReleaseComponentDeletionFile(Guid deletionId, string relativePath)
    {
        Id = Guid.NewGuid();
        ClientReleaseComponentDeletionId = deletionId;
        RelativePath = ClientReleaseComponent.NormalizeRequired(relativePath, nameof(relativePath));
    }

    public Guid ClientReleaseComponentDeletionId { get; private set; }

    public string RelativePath { get; private set; } = null!;
}

public enum ClientReleaseComponentDeletionStatus
{
    Requested,
    Failed
}
