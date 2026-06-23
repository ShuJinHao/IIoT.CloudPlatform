using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.ProductionService.ClientReleases;

public static class ClientReleaseCatalogSchema
{
    public const int Version = 2;
}

public sealed record ClientReleaseCatalogDto(
    int CatalogSchemaVersion,
    string Channel,
    string? TargetRuntime,
    ClientHostReleaseComponentDto Host,
    IReadOnlyList<ClientPluginReleaseComponentDto> Plugins,
    DateTime GeneratedAtUtc,
    string? HostUpdateSource = null);

public sealed record ClientHostReleaseComponentDto(
    string ComponentKind,
    string DisplayName,
    IReadOnlyList<ClientHostVersionEntryDto> Versions);

public sealed record ClientPluginReleaseComponentDto(
    string ComponentKind,
    string ModuleId,
    string DisplayName,
    string? Description,
    string? IconKind,
    string? AccentColor,
    IReadOnlyList<ClientPluginVersionEntryDto> Versions);

public sealed record ClientHostVersionEntryDto(
    Guid Id,
    string Channel,
    string Version,
    string HostApiVersion,
    string TargetRuntime,
    string? TargetFramework,
    string DownloadUrl,
    string Sha256,
    long PackageSize,
    string? ReleaseNotes,
    string Status,
    string? Signature,
    string? Publisher,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc);

public sealed record ClientPluginVersionEntryDto(
    Guid Id,
    string Channel,
    string Version,
    string HostApiVersion,
    string MinHostVersion,
    string MaxHostVersion,
    string TargetRuntime,
    string? TargetFramework,
    string DownloadUrl,
    string Sha256,
    long PackageSize,
    string? ReleaseNotes,
    JsonElement Dependencies,
    string Status,
    string? Signature,
    string? Publisher,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc);

public sealed record UpsertClientHostReleaseResultDto(Guid Id);

public sealed record UpsertClientPluginReleaseResultDto(Guid Id);

public sealed record DeviceClientVersionReportResultDto(
    Guid DeviceId,
    DateTime ReceivedAtUtc);

public sealed record DeviceClientVersionInventoryDto(
    Guid DeviceId,
    string DeviceName,
    string ClientCode,
    string? PrimaryIp,
    IReadOnlyList<string> LocalIpAddresses,
    string? RemoteIpAddress,
    string? Channel,
    string? HostVersion,
    string? HostApiVersion,
    string HostUpdateStatus,
    string? HostCompatibilityIssue,
    string InstallStatus,
    string CurrentVersion,
    string? Issue,
    DateTime? ReportedAtUtc,
    DateTime? ReceivedAtUtc,
    IReadOnlyList<DeviceClientPluginInventoryDto> Plugins);

public sealed record DeviceClientPluginInventoryDto(
    string ModuleId,
    string? DisplayName,
    string? Version,
    string? HostApiVersion,
    bool Enabled,
    string UpdateStatus,
    string? CompatibilityIssue);

public sealed record PublicClientDownloadCatalogDto(
    int CatalogSchemaVersion,
    string Channel,
    string TargetRuntime,
    PublicClientHostDownloadComponentDto Host,
    IReadOnlyList<PublicClientPluginCatalogComponentDto> Plugins,
    DateTime GeneratedAtUtc);

public sealed record PublicClientHostDownloadComponentDto(
    string ComponentKind,
    string DisplayName,
    IReadOnlyList<PublicClientHostVersionDto> Versions);

public sealed record PublicClientHostVersionDto(
    string Channel,
    string Version,
    string HostApiVersion,
    string TargetRuntime,
    string? TargetFramework,
    string Sha256,
    long PackageSize,
    string? ReleaseNotes,
    string Status,
    string? Publisher,
    DateTime? PublishedAtUtc);

public sealed record PublicClientPluginCatalogComponentDto(
    string ComponentKind,
    string ModuleId,
    string DisplayName,
    string? Description,
    string? IconKind,
    string? AccentColor,
    IReadOnlyList<PublicClientPluginVersionDto> Versions);

public sealed record PublicClientPluginVersionDto(
    string Channel,
    string Version,
    string HostApiVersion,
    string MinHostVersion,
    string MaxHostVersion,
    string TargetRuntime,
    string? TargetFramework,
    long PackageSize,
    string? ReleaseNotes,
    JsonElement Dependencies,
    string Status,
    string? Publisher,
    DateTime? PublishedAtUtc);

public sealed record ClientReleaseRetentionPolicyDto(
    int MaxVersionsPerComponent,
    DateTime UpdatedAtUtc);

internal static class ClientReleaseMapping
{
    private static readonly JsonElement EmptyArray = JsonDocument.Parse("[]").RootElement.Clone();
    private static readonly IComparer<string> VersionComparer = Comparer<string>.Create(CompareVersions);

    public static ClientHostReleaseComponentDto ToHostComponent(
        IEnumerable<ClientHostRelease> releases,
        int? maxVersions = null)
    {
        var versions = OrderHostVersions(releases, maxVersions)
            .Select(ToHostVersion)
            .ToList();

        return new ClientHostReleaseComponentDto("Host", "Edge Host", versions);
    }

    public static IReadOnlyList<ClientPluginReleaseComponentDto> ToPluginComponents(
        IEnumerable<ClientPluginRelease> releases,
        int? maxVersions = null)
    {
        return releases
            .GroupBy(release => release.ModuleId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = OrderPluginVersions(group, maxVersions).ToList();
                var metadata = ordered.FirstOrDefault() ?? group.First();
                return new ClientPluginReleaseComponentDto(
                    "Plugin",
                    metadata.ModuleId,
                    metadata.DisplayName,
                    metadata.Description,
                    metadata.IconKind,
                    metadata.AccentColor,
                    ordered.Select(ToPluginVersion).ToList());
            })
            .ToList();
    }

    public static PublicClientHostDownloadComponentDto ToPublicHostComponent(
        IEnumerable<ClientHostRelease> releases,
        int? maxVersions = null)
    {
        var versions = OrderHostVersions(releases, maxVersions)
            .Select(ToPublicHostVersion)
            .ToList();

        return new PublicClientHostDownloadComponentDto("Host", "Edge Host", versions);
    }

    public static IReadOnlyList<PublicClientPluginCatalogComponentDto> ToPublicPluginComponents(
        IEnumerable<ClientPluginRelease> releases,
        int? maxVersions = null)
    {
        return releases
            .GroupBy(release => release.ModuleId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = OrderPluginVersions(group, maxVersions).ToList();
                var metadata = ordered.FirstOrDefault() ?? group.First();
                return new PublicClientPluginCatalogComponentDto(
                    "Plugin",
                    metadata.ModuleId,
                    metadata.DisplayName,
                    metadata.Description,
                    metadata.IconKind,
                    metadata.AccentColor,
                    ordered.Select(ToPublicPluginVersion).ToList());
            })
            .ToList();
    }

    public static ClientHostVersionEntryDto ToHostVersion(ClientHostRelease release)
    {
        return new ClientHostVersionEntryDto(
            release.Id,
            release.Channel,
            release.Version,
            release.HostApiVersion,
            release.TargetRuntime,
            release.TargetFramework,
            release.DownloadUrl,
            release.Sha256,
            release.PackageSize,
            release.ReleaseNotes,
            release.Status.ToString(),
            release.Signature,
            release.Publisher,
            release.CreatedAtUtc,
            release.PublishedAtUtc);
    }

    public static ClientPluginVersionEntryDto ToPluginVersion(ClientPluginRelease release)
    {
        return new ClientPluginVersionEntryDto(
            release.Id,
            release.Channel,
            release.Version,
            release.HostApiVersion,
            release.MinHostVersion,
            release.MaxHostVersion,
            release.TargetRuntime,
            release.TargetFramework,
            release.DownloadUrl,
            release.Sha256,
            release.PackageSize,
            release.ReleaseNotes,
            ParseDependencies(release.DependenciesJson),
            release.Status.ToString(),
            release.Signature,
            release.Publisher,
            release.CreatedAtUtc,
            release.PublishedAtUtc);
    }

    public static PublicClientHostVersionDto ToPublicHostVersion(ClientHostRelease release)
    {
        return new PublicClientHostVersionDto(
            release.Channel,
            release.Version,
            release.HostApiVersion,
            release.TargetRuntime,
            release.TargetFramework,
            release.Sha256,
            release.PackageSize,
            release.ReleaseNotes,
            release.Status.ToString(),
            release.Publisher,
            release.PublishedAtUtc);
    }

    public static PublicClientPluginVersionDto ToPublicPluginVersion(ClientPluginRelease release)
    {
        return new PublicClientPluginVersionDto(
            release.Channel,
            release.Version,
            release.HostApiVersion,
            release.MinHostVersion,
            release.MaxHostVersion,
            release.TargetRuntime,
            release.TargetFramework,
            release.PackageSize,
            release.ReleaseNotes,
            ParseDependencies(release.DependenciesJson),
            release.Status.ToString(),
            release.Publisher,
            release.PublishedAtUtc);
    }

    public static JsonElement ParseDependencies(string? dependenciesJson)
    {
        if (string.IsNullOrWhiteSpace(dependenciesJson))
        {
            return EmptyArray.Clone();
        }

        using var document = JsonDocument.Parse(dependenciesJson);
        return document.RootElement.Clone();
    }

    public static bool TryParseStatus(string value, out ClientReleaseStatus status)
    {
        return Enum.TryParse(value?.Trim(), ignoreCase: true, out status);
    }

    public static bool IsCompatibleWithHost(
        ClientPluginRelease release,
        string hostVersion,
        string hostApiVersion,
        out string? issue)
    {
        if (!string.Equals(release.HostApiVersion, hostApiVersion, StringComparison.OrdinalIgnoreCase))
        {
            issue = $"hostApiVersion 不匹配: 插件要求 {release.HostApiVersion}, 当前宿主 {hostApiVersion}";
            return false;
        }

        if (!IsVersionInRange(hostVersion, release.MinHostVersion, release.MaxHostVersion))
        {
            issue = $"宿主版本 {hostVersion} 不在插件兼容窗口 [{release.MinHostVersion}, {release.MaxHostVersion}]";
            return false;
        }

        issue = null;
        return true;
    }

    public static int CompareVersions(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(left))
        {
            return -1;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return 1;
        }

        if (Version.TryParse(left, out var leftVersion) && Version.TryParse(right, out var rightVersion))
        {
            return leftVersion.CompareTo(rightVersion);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<ClientHostRelease> OrderHostVersions(
        IEnumerable<ClientHostRelease> releases,
        int? maxVersions = null)
    {
        var ordered = releases
            .OrderByDescending(release => release.Version, VersionComparer)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc);

        return (maxVersions is { } max ? ordered.Take(max) : ordered).ToList();
    }

    public static IReadOnlyList<ClientPluginRelease> OrderPluginVersions(
        IEnumerable<ClientPluginRelease> releases,
        int? maxVersions = null)
    {
        var ordered = releases
            .OrderByDescending(release => release.Version, VersionComparer)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc);

        return (maxVersions is { } max ? ordered.Take(max) : ordered).ToList();
    }

    private static bool IsVersionInRange(string hostVersion, string minHostVersion, string maxHostVersion)
    {
        return CompareVersions(hostVersion, minHostVersion) >= 0
            && CompareVersions(hostVersion, maxHostVersion) <= 0;
    }
}
