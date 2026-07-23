using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// 发布组件永久删除的持久化操作记录。不是业务聚合根，不通过通用 IRepository 暴露；
/// 与组件元数据删除在同一数据库事务写入，驱动后续可重试、可恢复的幂等文件清理。
/// 保存请求管理员身份、删除原因和每个受控文件目标的类型与精确事实（SHA256/大小），
/// 重试前按事实与完整受控目录祖先链重新校验，防止持久化后被替换/穿透。
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
        Guid? requestedByUserId,
        string? requestedByUserName,
        IEnumerable<ClientReleaseComponentDeletionFileTarget> fileTargets)
    {
        Id = Guid.NewGuid();
        ComponentId = componentId;
        ComponentKind = ClientReleaseComponent.NormalizeRequired(componentKind, nameof(componentKind));
        ComponentKey = ClientReleaseComponent.NormalizeRequired(componentKey, nameof(componentKey));
        Channel = ClientReleaseComponent.NormalizeRequired(channel, nameof(channel));
        TargetRuntime = ClientReleaseComponent.NormalizeRequired(targetRuntime, nameof(targetRuntime));
        VersionsJson = System.Text.Json.JsonSerializer.Serialize(
            versions.OrderBy(version => version, StringComparer.OrdinalIgnoreCase).ToArray());
        Reason = ClientReleaseComponent.NormalizeOptional(reason);
        RequestedByUserId = requestedByUserId;
        RequestedByUserName = ClientReleaseComponent.NormalizeOptional(requestedByUserName);
        Status = ClientReleaseComponentDeletionStatus.Requested;
        RetryCount = 0;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        foreach (var target in fileTargets
                     .DistinctBy(target => target.RelativePath, StringComparer.Ordinal)
                     .OrderBy(target => target.RelativePath, StringComparer.Ordinal))
        {
            _files.Add(new ClientReleaseComponentDeletionFile(
                Id,
                target.RelativePath,
                target.ArtifactKind,
                target.Sha256,
                target.SizeBytes));
        }
    }

    public Guid ComponentId { get; private set; }

    public string ComponentKind { get; private set; } = null!;

    public string ComponentKey { get; private set; } = null!;

    public string Channel { get; private set; } = null!;

    public string TargetRuntime { get; private set; } = null!;

    public string VersionsJson { get; private set; } = "[]";

    public string? Reason { get; private set; }

    public Guid? RequestedByUserId { get; private set; }

    public string? RequestedByUserName { get; private set; }

    public ClientReleaseComponentDeletionStatus Status { get; private set; }

    public string? FailureCode { get; private set; }

    public int RetryCount { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public string? CleanupResultJson { get; private set; }

    public DateTime? CleanupCompletedAtUtc { get; private set; }

    public IReadOnlyCollection<ClientReleaseComponentDeletionFile> Files => _files.AsReadOnly();

    public void MarkFailed(string failureCode)
    {
        Status = ClientReleaseComponentDeletionStatus.Failed;
        FailureCode = ClientReleaseComponent.NormalizeRequired(failureCode, nameof(failureCode));
        CleanupResultJson = null;
        CleanupCompletedAtUtc = null;
        RetryCount += 1;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// 文件清理已收敛，等待成功审计写稳后才能删除操作记录。
    /// </summary>
    public void MarkCleanupCompleted(
        IReadOnlyList<string> deletedPaths,
        IReadOnlyList<string> skippedPaths,
        bool manifestChanged)
    {
        if (Status == ClientReleaseComponentDeletionStatus.CleanupCompleted
            && CleanupResultJson is not null
            && CleanupCompletedAtUtc.HasValue)
        {
            return;
        }

        var completedAtUtc = DateTime.UtcNow;
        Status = ClientReleaseComponentDeletionStatus.CleanupCompleted;
        FailureCode = null;
        CleanupResultJson = System.Text.Json.JsonSerializer.Serialize(
            new ClientReleaseComponentDeletionCleanupResult(
                [.. deletedPaths],
                [.. skippedPaths],
                manifestChanged));
        CleanupCompletedAtUtc = completedAtUtc;
        UpdatedAtUtc = completedAtUtc;
    }

    public bool TryGetCleanupResult(out ClientReleaseComponentDeletionCleanupResult? result)
    {
        result = null;
        if (Status != ClientReleaseComponentDeletionStatus.CleanupCompleted
            || string.IsNullOrWhiteSpace(CleanupResultJson)
            || !CleanupCompletedAtUtc.HasValue)
        {
            return false;
        }

        try
        {
            result = System.Text.Json.JsonSerializer.Deserialize<ClientReleaseComponentDeletionCleanupResult>(
                CleanupResultJson);
            return result is not null
                   && result.DeletedPaths is not null
                   && result.SkippedPaths is not null;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    public void ResetForRetry()
    {
        Status = ClientReleaseComponentDeletionStatus.Requested;
        FailureCode = null;
        CleanupResultJson = null;
        CleanupCompletedAtUtc = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}

public sealed record ClientReleaseComponentDeletionCleanupResult(
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<string> SkippedPaths,
    bool ManifestChanged);

/// <summary>
/// 删除操作持久化的单个受控文件目标：相对路径 + 类型 + 精确事实（SHA256/大小）。
/// </summary>
public sealed record ClientReleaseComponentDeletionFileTarget(
    string RelativePath,
    string ArtifactKind,
    string? Sha256,
    long? SizeBytes);

public sealed class ClientReleaseComponentDeletionFile : BaseEntity<Guid>
{
    private ClientReleaseComponentDeletionFile()
    {
    }

    internal ClientReleaseComponentDeletionFile(
        Guid deletionId,
        string relativePath,
        string artifactKind,
        string? sha256,
        long? sizeBytes)
    {
        Id = Guid.NewGuid();
        ClientReleaseComponentDeletionId = deletionId;
        RelativePath = ClientReleaseComponent.NormalizeRequired(relativePath, nameof(relativePath));
        ArtifactKind = ClientReleaseComponent.NormalizeRequired(artifactKind, nameof(artifactKind));
        Sha256 = ClientReleaseComponent.NormalizeOptional(sha256);
        SizeBytes = sizeBytes;
    }

    public Guid ClientReleaseComponentDeletionId { get; private set; }

    public string RelativePath { get; private set; } = null!;

    public string ArtifactKind { get; private set; } = null!;

    public string? Sha256 { get; private set; }

    public long? SizeBytes { get; private set; }
}

public enum ClientReleaseComponentDeletionStatus
{
    Requested,
    Failed,
    CleanupCompleted
}
