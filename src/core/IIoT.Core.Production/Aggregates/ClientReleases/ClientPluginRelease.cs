using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// Edge 工序插件发布记录。
/// </summary>
public sealed class ClientPluginRelease : BaseEntity<Guid>
{
    private ClientPluginRelease()
    {
    }

    public ClientPluginRelease(
        string moduleId,
        string displayName,
        string? description,
        string? iconKind,
        string? accentColor,
        string channel,
        string version,
        string hostApiVersion,
        string minHostVersion,
        string maxHostVersion,
        string targetRuntime,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        string dependenciesJson,
        ClientReleaseStatus status,
        string? signature,
        string? publisher,
        DateTime? publishedAtUtc = null)
    {
        Id = Guid.NewGuid();
        ModuleId = NormalizeRequired(moduleId, nameof(moduleId));
        DisplayName = NormalizeRequired(displayName, nameof(displayName));
        Description = NormalizeOptional(description);
        IconKind = NormalizeOptional(iconKind);
        AccentColor = NormalizeOptional(accentColor);
        Channel = NormalizeRequired(channel, nameof(channel));
        Version = NormalizeRequired(version, nameof(version));
        HostApiVersion = NormalizeRequired(hostApiVersion, nameof(hostApiVersion));
        MinHostVersion = NormalizeRequired(minHostVersion, nameof(minHostVersion));
        MaxHostVersion = NormalizeRequired(maxHostVersion, nameof(maxHostVersion));
        TargetRuntime = NormalizeRequired(targetRuntime, nameof(targetRuntime));
        TargetFramework = NormalizeOptional(targetFramework);
        DownloadUrl = NormalizeRequired(downloadUrl, nameof(downloadUrl));
        Sha256 = NormalizeRequired(sha256, nameof(sha256));
        PackageSize = packageSize;
        ReleaseNotes = NormalizeOptional(releaseNotes);
        DependenciesJson = NormalizeDependencies(dependenciesJson);
        Status = status;
        Signature = NormalizeOptional(signature);
        Publisher = NormalizeOptional(publisher);
        CreatedAtUtc = DateTime.UtcNow;
        PublishedAtUtc = status == ClientReleaseStatus.Published
            ? publishedAtUtc ?? CreatedAtUtc
            : publishedAtUtc;

        Validate();
    }

    public string ModuleId { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public string? Description { get; private set; }

    public string? IconKind { get; private set; }

    public string? AccentColor { get; private set; }

    public string Channel { get; private set; } = null!;

    public string Version { get; private set; } = null!;

    public string HostApiVersion { get; private set; } = null!;

    public string MinHostVersion { get; private set; } = null!;

    public string MaxHostVersion { get; private set; } = null!;

    public string TargetRuntime { get; private set; } = null!;

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

    public void UpdateRelease(
        string displayName,
        string? description,
        string? iconKind,
        string? accentColor,
        string hostApiVersion,
        string minHostVersion,
        string maxHostVersion,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        string dependenciesJson,
        ClientReleaseStatus status,
        string? signature,
        string? publisher)
    {
        DisplayName = NormalizeRequired(displayName, nameof(displayName));
        Description = NormalizeOptional(description);
        IconKind = NormalizeOptional(iconKind);
        AccentColor = NormalizeOptional(accentColor);
        HostApiVersion = NormalizeRequired(hostApiVersion, nameof(hostApiVersion));
        MinHostVersion = NormalizeRequired(minHostVersion, nameof(minHostVersion));
        MaxHostVersion = NormalizeRequired(maxHostVersion, nameof(maxHostVersion));
        TargetFramework = NormalizeOptional(targetFramework);
        DownloadUrl = NormalizeRequired(downloadUrl, nameof(downloadUrl));
        Sha256 = NormalizeRequired(sha256, nameof(sha256));
        PackageSize = packageSize;
        ReleaseNotes = NormalizeOptional(releaseNotes);
        DependenciesJson = NormalizeDependencies(dependenciesJson);
        Status = status;
        Signature = NormalizeOptional(signature);
        Publisher = NormalizeOptional(publisher);
        if (status == ClientReleaseStatus.Published && PublishedAtUtc is null)
        {
            PublishedAtUtc = DateTime.UtcNow;
        }

        Validate();
    }

    private void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ModuleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(Channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(HostApiVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(MinHostVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(MaxHostVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(TargetRuntime);
        ArgumentException.ThrowIfNullOrWhiteSpace(DownloadUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(Sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(DependenciesJson);
        if (PackageSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PackageSize), "包大小不能为负数。");
        }
    }

    private static string NormalizeRequired(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeDependencies(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? "[]" : normalized;
    }
}
