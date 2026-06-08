using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.ClientReleases;

/// <summary>
/// 通用 Edge 宿主发布记录。
/// </summary>
public sealed class ClientHostRelease : BaseEntity<Guid>
{
    private ClientHostRelease()
    {
    }

    public ClientHostRelease(
        string channel,
        string version,
        string hostApiVersion,
        string targetRuntime,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        ClientReleaseStatus status,
        string? signature,
        string? publisher,
        DateTime? publishedAtUtc = null)
    {
        Id = Guid.NewGuid();
        Channel = NormalizeRequired(channel, nameof(channel));
        Version = NormalizeRequired(version, nameof(version));
        HostApiVersion = NormalizeRequired(hostApiVersion, nameof(hostApiVersion));
        TargetRuntime = NormalizeRequired(targetRuntime, nameof(targetRuntime));
        TargetFramework = NormalizeOptional(targetFramework);
        DownloadUrl = NormalizeRequired(downloadUrl, nameof(downloadUrl));
        Sha256 = NormalizeRequired(sha256, nameof(sha256));
        PackageSize = packageSize;
        ReleaseNotes = NormalizeOptional(releaseNotes);
        Status = status;
        Signature = NormalizeOptional(signature);
        Publisher = NormalizeOptional(publisher);
        CreatedAtUtc = DateTime.UtcNow;
        PublishedAtUtc = status == ClientReleaseStatus.Published
            ? publishedAtUtc ?? CreatedAtUtc
            : publishedAtUtc;

        Validate();
    }

    public string Channel { get; private set; } = null!;

    public string Version { get; private set; } = null!;

    public string HostApiVersion { get; private set; } = null!;

    public string TargetRuntime { get; private set; } = null!;

    public string? TargetFramework { get; private set; }

    public string DownloadUrl { get; private set; } = null!;

    public string Sha256 { get; private set; } = null!;

    public long PackageSize { get; private set; }

    public string? ReleaseNotes { get; private set; }

    public ClientReleaseStatus Status { get; private set; }

    public string? Signature { get; private set; }

    public string? Publisher { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? PublishedAtUtc { get; private set; }

    public void UpdateRelease(
        string hostApiVersion,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        ClientReleaseStatus status,
        string? signature,
        string? publisher)
    {
        HostApiVersion = NormalizeRequired(hostApiVersion, nameof(hostApiVersion));
        TargetFramework = NormalizeOptional(targetFramework);
        DownloadUrl = NormalizeRequired(downloadUrl, nameof(downloadUrl));
        Sha256 = NormalizeRequired(sha256, nameof(sha256));
        PackageSize = packageSize;
        ReleaseNotes = NormalizeOptional(releaseNotes);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(Channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(HostApiVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(TargetRuntime);
        ArgumentException.ThrowIfNullOrWhiteSpace(DownloadUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(Sha256);
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
}
