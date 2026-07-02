using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// Edge 客户端发布组件聚合根。宿主和工序插件统一由该聚合承载版本、发布素材和生命周期。
/// </summary>
public sealed class ClientReleaseComponent : BaseEntity<Guid>
{
    public const string HostComponentKey = "EdgeHost";

    private readonly List<ClientReleaseVersion> _versions = [];

    private ClientReleaseComponent()
    {
    }

    public ClientReleaseComponent(
        ClientReleaseComponentKind componentKind,
        string componentKey,
        string displayName,
        string? description,
        string? iconKind,
        string? accentColor,
        string channel,
        string targetRuntime)
    {
        Id = Guid.NewGuid();
        ComponentKind = componentKind;
        ComponentKey = NormalizeRequired(componentKey, nameof(componentKey));
        DisplayName = NormalizeRequired(displayName, nameof(displayName));
        Description = NormalizeOptional(description);
        IconKind = NormalizeOptional(iconKind);
        AccentColor = NormalizeOptional(accentColor);
        Channel = NormalizeRequired(channel, nameof(channel));
        TargetRuntime = NormalizeRequired(targetRuntime, nameof(targetRuntime));
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        Validate();
    }

    public ClientReleaseComponentKind ComponentKind { get; private set; }

    public string ComponentKey { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public string? Description { get; private set; }

    public string? IconKind { get; private set; }

    public string? AccentColor { get; private set; }

    public string Channel { get; private set; } = null!;

    public string TargetRuntime { get; private set; } = null!;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<ClientReleaseVersion> Versions => _versions.AsReadOnly();

    public static ClientReleaseComponent CreateHost(
        string channel,
        string targetRuntime)
    {
        return new ClientReleaseComponent(
            ClientReleaseComponentKind.Host,
            HostComponentKey,
            "Edge Host",
            null,
            null,
            null,
            channel,
            targetRuntime);
    }

    public static ClientReleaseComponent CreatePlugin(
        string moduleId,
        string displayName,
        string? description,
        string? iconKind,
        string? accentColor,
        string channel,
        string targetRuntime)
    {
        return new ClientReleaseComponent(
            ClientReleaseComponentKind.Plugin,
            moduleId,
            displayName,
            description,
            iconKind,
            accentColor,
            channel,
            targetRuntime);
    }

    public void UpdateHostMetadata()
    {
        EnsureKind(ClientReleaseComponentKind.Host);
        DisplayName = "Edge Host";
        Description = null;
        IconKind = null;
        AccentColor = null;
        Touch();
    }

    public void UpdatePluginMetadata(
        string displayName,
        string? description,
        string? iconKind,
        string? accentColor)
    {
        EnsureKind(ClientReleaseComponentKind.Plugin);
        DisplayName = NormalizeRequired(displayName, nameof(displayName));
        Description = NormalizeOptional(description);
        IconKind = NormalizeOptional(iconKind);
        AccentColor = NormalizeOptional(accentColor);
        Touch();
    }

    public ClientReleaseVersion UpsertHostVersion(
        string version,
        string hostApiVersion,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        ClientReleaseStatus status,
        string? signature,
        string? publisher,
        DateTime? publishedAtUtc = null,
        IEnumerable<ClientReleaseArtifact>? artifacts = null)
    {
        EnsureKind(ClientReleaseComponentKind.Host);
        var release = FindVersion(version);
        if (release is null)
        {
            release = ClientReleaseVersion.CreateHost(
                Id,
                version,
                hostApiVersion,
                targetFramework,
                downloadUrl,
                sha256,
                packageSize,
                releaseNotes,
                status,
                signature,
                publisher,
                publishedAtUtc,
                artifacts);
            _versions.Add(release);
        }
        else
        {
            release.UpdateHost(
                hostApiVersion,
                targetFramework,
                downloadUrl,
                sha256,
                packageSize,
                releaseNotes,
                status,
                signature,
                publisher,
                artifacts);
        }

        Touch();
        return release;
    }

    public ClientReleaseVersion UpsertPluginVersion(
        string version,
        string hostApiVersion,
        string minHostVersion,
        string maxHostVersion,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        string? dependenciesJson,
        ClientReleaseStatus status,
        string? signature,
        string? publisher,
        DateTime? publishedAtUtc = null,
        IEnumerable<ClientReleaseArtifact>? artifacts = null)
    {
        EnsureKind(ClientReleaseComponentKind.Plugin);
        var release = FindVersion(version);
        if (release is null)
        {
            release = ClientReleaseVersion.CreatePlugin(
                Id,
                version,
                hostApiVersion,
                minHostVersion,
                maxHostVersion,
                targetFramework,
                downloadUrl,
                sha256,
                packageSize,
                releaseNotes,
                dependenciesJson,
                status,
                signature,
                publisher,
                publishedAtUtc,
                artifacts);
            _versions.Add(release);
        }
        else
        {
            release.UpdatePlugin(
                hostApiVersion,
                minHostVersion,
                maxHostVersion,
                targetFramework,
                downloadUrl,
                sha256,
                packageSize,
                releaseNotes,
                dependenciesJson,
                status,
                signature,
                publisher,
                artifacts);
        }

        Touch();
        return release;
    }

    public ClientReleaseVersion? FindVersion(Guid versionId)
    {
        return _versions.FirstOrDefault(version => version.Id == versionId);
    }

    public ClientReleaseVersion? FindVersion(string version)
    {
        var normalized = NormalizeRequired(version, nameof(version));
        return _versions.FirstOrDefault(item =>
            string.Equals(item.Version, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public void ChangeVersionStatus(Guid versionId, ClientReleaseStatus status)
    {
        var version = FindRequiredVersion(versionId);
        version.ChangeStatus(status);
        Touch();
    }

    public void MarkVersionDeleted(Guid versionId, string? reason)
    {
        var version = FindRequiredVersion(versionId);
        version.MarkDeleted(reason);
        Touch();
    }

    public void MarkVersionDeleteFailed(Guid versionId, string failure)
    {
        var version = FindRequiredVersion(versionId);
        version.MarkDeleteFailed(failure);
        Touch();
    }

    private ClientReleaseVersion FindRequiredVersion(Guid versionId)
    {
        return FindVersion(versionId)
            ?? throw new InvalidOperationException("发布版本不存在。");
    }

    private void EnsureKind(ClientReleaseComponentKind expectedKind)
    {
        if (ComponentKind != expectedKind)
        {
            throw new InvalidOperationException("发布组件类型不匹配。");
        }
    }

    private void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ComponentKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(TargetRuntime);
    }

    internal static string NormalizeRequired(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }

    internal static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}

public enum ClientReleaseComponentKind
{
    Host,
    Plugin
}

public sealed class ClientReleaseVersion : BaseEntity<Guid>
{
    private readonly List<ClientReleaseArtifact> _artifacts = [];

    private ClientReleaseVersion()
    {
    }

    private ClientReleaseVersion(
        Guid componentId,
        string version,
        string hostApiVersion,
        string? minHostVersion,
        string? maxHostVersion,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        string? dependenciesJson,
        ClientReleaseStatus status,
        string? signature,
        string? publisher,
        DateTime? publishedAtUtc,
        IEnumerable<ClientReleaseArtifact>? artifacts)
    {
        Id = Guid.NewGuid();
        ClientReleaseComponentId = componentId;
        CreatedAtUtc = DateTime.UtcNow;
        Apply(
            version,
            hostApiVersion,
            minHostVersion,
            maxHostVersion,
            targetFramework,
            downloadUrl,
            sha256,
            packageSize,
            releaseNotes,
            dependenciesJson,
            status,
            signature,
            publisher,
            publishedAtUtc,
            artifacts);
    }

    public Guid ClientReleaseComponentId { get; private set; }

    public string Version { get; private set; } = null!;

    public string HostApiVersion { get; private set; } = null!;

    public string? MinHostVersion { get; private set; }

    public string? MaxHostVersion { get; private set; }

    public string? TargetFramework { get; private set; }

    public string DownloadUrl { get; private set; } = null!;

    public string Sha256 { get; private set; } = null!;

    public long PackageSize { get; private set; }

    public string? ReleaseNotes { get; private set; }

    public string DependenciesJson { get; private set; } = "[]";

    public ClientReleaseStatus Status { get; private set; }

    public string? Signature { get; private set; }

    public string? Publisher { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? PublishedAtUtc { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public string? DeletionReason { get; private set; }

    public string? DeletionFailure { get; private set; }

    public IReadOnlyCollection<ClientReleaseArtifact> Artifacts => _artifacts.AsReadOnly();

    public static ClientReleaseVersion CreateHost(
        Guid componentId,
        string version,
        string hostApiVersion,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        ClientReleaseStatus status,
        string? signature,
        string? publisher,
        DateTime? publishedAtUtc = null,
        IEnumerable<ClientReleaseArtifact>? artifacts = null)
    {
        return new ClientReleaseVersion(
            componentId,
            version,
            hostApiVersion,
            null,
            null,
            targetFramework,
            downloadUrl,
            sha256,
            packageSize,
            releaseNotes,
            "[]",
            status,
            signature,
            publisher,
            publishedAtUtc,
            artifacts);
    }

    public static ClientReleaseVersion CreatePlugin(
        Guid componentId,
        string version,
        string hostApiVersion,
        string minHostVersion,
        string maxHostVersion,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        string? dependenciesJson,
        ClientReleaseStatus status,
        string? signature,
        string? publisher,
        DateTime? publishedAtUtc = null,
        IEnumerable<ClientReleaseArtifact>? artifacts = null)
    {
        return new ClientReleaseVersion(
            componentId,
            version,
            hostApiVersion,
            minHostVersion,
            maxHostVersion,
            targetFramework,
            downloadUrl,
            sha256,
            packageSize,
            releaseNotes,
            dependenciesJson,
            status,
            signature,
            publisher,
            publishedAtUtc,
            artifacts);
    }

    public void UpdateHost(
        string hostApiVersion,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        ClientReleaseStatus status,
        string? signature,
        string? publisher,
        IEnumerable<ClientReleaseArtifact>? artifacts = null)
    {
        Apply(
            Version,
            hostApiVersion,
            null,
            null,
            targetFramework,
            downloadUrl,
            sha256,
            packageSize,
            releaseNotes,
            "[]",
            status,
            signature,
            publisher,
            null,
            artifacts);
    }

    public void UpdatePlugin(
        string hostApiVersion,
        string minHostVersion,
        string maxHostVersion,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        string? dependenciesJson,
        ClientReleaseStatus status,
        string? signature,
        string? publisher,
        IEnumerable<ClientReleaseArtifact>? artifacts = null)
    {
        Apply(
            Version,
            hostApiVersion,
            minHostVersion,
            maxHostVersion,
            targetFramework,
            downloadUrl,
            sha256,
            packageSize,
            releaseNotes,
            dependenciesJson,
            status,
            signature,
            publisher,
            null,
            artifacts);
    }

    public void ChangeStatus(ClientReleaseStatus status)
    {
        Status = status;
        if (status != ClientReleaseStatus.Deleted && status != ClientReleaseStatus.DeleteFailed)
        {
            DeletedAtUtc = null;
            DeletionReason = null;
            DeletionFailure = null;
        }

        if (status == ClientReleaseStatus.Published && PublishedAtUtc is null)
        {
            PublishedAtUtc = DateTime.UtcNow;
        }
    }

    public void MarkDeleted(string? reason)
    {
        Status = ClientReleaseStatus.Deleted;
        DeletedAtUtc = DateTime.UtcNow;
        DeletionReason = ClientReleaseComponent.NormalizeOptional(reason);
        DeletionFailure = null;
    }

    public void MarkDeleteFailed(string failure)
    {
        Status = ClientReleaseStatus.DeleteFailed;
        DeletionFailure = ClientReleaseComponent.NormalizeRequired(failure, nameof(failure));
    }

    public void ReplaceArtifacts(IEnumerable<ClientReleaseArtifact>? artifacts)
    {
        _artifacts.Clear();
        foreach (var artifact in NormalizeArtifacts(artifacts))
        {
            artifact.AssignVersion(Id);
            _artifacts.Add(artifact);
        }
    }

    private void Apply(
        string version,
        string hostApiVersion,
        string? minHostVersion,
        string? maxHostVersion,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        string? dependenciesJson,
        ClientReleaseStatus status,
        string? signature,
        string? publisher,
        DateTime? publishedAtUtc,
        IEnumerable<ClientReleaseArtifact>? artifacts)
    {
        Version = ClientReleaseComponent.NormalizeRequired(version, nameof(version));
        HostApiVersion = ClientReleaseComponent.NormalizeRequired(hostApiVersion, nameof(hostApiVersion));
        MinHostVersion = ClientReleaseComponent.NormalizeOptional(minHostVersion);
        MaxHostVersion = ClientReleaseComponent.NormalizeOptional(maxHostVersion);
        TargetFramework = ClientReleaseComponent.NormalizeOptional(targetFramework);
        DownloadUrl = ClientReleaseComponent.NormalizeRequired(downloadUrl, nameof(downloadUrl));
        Sha256 = ClientReleaseComponent.NormalizeRequired(sha256, nameof(sha256));
        PackageSize = packageSize;
        ReleaseNotes = ClientReleaseComponent.NormalizeOptional(releaseNotes);
        DependenciesJson = NormalizeDependencies(dependenciesJson);
        Status = status;
        Signature = ClientReleaseComponent.NormalizeOptional(signature);
        Publisher = ClientReleaseComponent.NormalizeOptional(publisher);
        if (status != ClientReleaseStatus.Deleted && status != ClientReleaseStatus.DeleteFailed)
        {
            DeletedAtUtc = null;
            DeletionReason = null;
            DeletionFailure = null;
        }

        if (status == ClientReleaseStatus.Published && PublishedAtUtc is null)
        {
            PublishedAtUtc = publishedAtUtc ?? DateTime.UtcNow;
        }
        else if (publishedAtUtc is not null && PublishedAtUtc is null)
        {
            PublishedAtUtc = publishedAtUtc;
        }

        ReplaceArtifacts(artifacts);
        Validate();
    }

    private void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(HostApiVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(DownloadUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(Sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(DependenciesJson);
        if (PackageSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PackageSize), "包大小不能为负数。");
        }
    }

    private static string NormalizeDependencies(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "[]" : normalized;
    }

    private static IReadOnlyList<ClientReleaseArtifact> NormalizeArtifacts(
        IEnumerable<ClientReleaseArtifact>? artifacts)
    {
        return (artifacts ?? [])
            .GroupBy(artifact => new
            {
                artifact.ArtifactKind,
                Path = artifact.RelativePath.ToUpperInvariant()
            })
            .Select(group => group.First())
            .ToList();
    }
}

public sealed class ClientReleaseArtifact : BaseEntity<Guid>
{
    private ClientReleaseArtifact()
    {
    }

    public ClientReleaseArtifact(
        ClientReleaseArtifactKind artifactKind,
        string relativePath,
        string? sha256 = null,
        long? size = null)
    {
        Id = Guid.NewGuid();
        ArtifactKind = artifactKind;
        RelativePath = NormalizeRelativePath(relativePath);
        Sha256 = ClientReleaseComponent.NormalizeOptional(sha256);
        Size = size;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid ClientReleaseVersionId { get; private set; }

    public ClientReleaseArtifactKind ArtifactKind { get; private set; }

    public string RelativePath { get; private set; } = null!;

    public string? Sha256 { get; private set; }

    public long? Size { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    internal void AssignVersion(Guid versionId)
    {
        ClientReleaseVersionId = versionId;
    }

    private static string NormalizeRelativePath(string value)
    {
        var normalized = ClientReleaseComponent.NormalizeRequired(value, nameof(value))
            .Replace('\\', '/')
            .TrimStart('/');
        if (normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("发布文件相对路径非法。", nameof(value));
        }

        return normalized;
    }
}

public enum ClientReleaseArtifactKind
{
    InstallerDirectory,
    ManifestFile,
    PackageFile,
    VelopackFile,
    PluginPackageDirectory
}
