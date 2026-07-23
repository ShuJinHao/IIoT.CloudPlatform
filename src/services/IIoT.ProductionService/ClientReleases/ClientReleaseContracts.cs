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
    Guid ComponentId,
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
    DateTime? PublishedAtUtc,
    DateTime? DeletedAtUtc,
    string? DeletionReason,
    string? DeletionFailure,
    bool FilesPresent = true);

public sealed record ClientPluginVersionEntryDto(
    Guid Id,
    Guid ComponentId,
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
    DateTime? PublishedAtUtc,
    DateTime? DeletedAtUtc,
    string? DeletionReason,
    string? DeletionFailure,
    bool FilesPresent = true);

public sealed record UpsertClientHostReleaseResultDto(Guid Id);

public sealed record UpsertClientPluginReleaseResultDto(Guid Id);

public sealed record DeviceClientVersionReportResultDto(
    Guid DeviceId,
    DateTime ReceivedAtUtc);

public sealed record DeviceRuntimeHeartbeatResultDto(
    Guid DeviceId,
    DateTime LastHeartbeatAtUtc);

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
    string SoftwareStatus,
    string CurrentVersion,
    string? Issue,
    string? VersionIssue,
    string? CloudIssue,
    DateTime? LastRuntimeHeartbeatAtUtc,
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
        IEnumerable<ClientReleaseComponent> components,
        int? maxVersions = null,
        bool onlyPublished = false,
        bool includeArchived = false)
    {
        var versions = OrderVersions(
                components
                    .Where(component => component.ComponentKind == ClientReleaseComponentKind.Host)
                    .SelectMany(component => component.Versions
                        .Where(version => ShouldExposeVersion(version, onlyPublished, includeArchived))
                        .Select(version => (Component: component, Version: version))),
                maxVersions)
            .Select(item => ToHostVersion(item.Component, item.Version))
            .ToList();

        return new ClientHostReleaseComponentDto("Host", "Edge Host", versions);
    }

    public static IReadOnlyList<ClientPluginReleaseComponentDto> ToPluginComponents(
        IEnumerable<ClientReleaseComponent> components,
        int? maxVersions = null,
        bool onlyPublished = false,
        bool includeArchived = false)
    {
        return components
            .Where(component => component.ComponentKind == ClientReleaseComponentKind.Plugin)
            .OrderBy(component => component.ComponentKey, StringComparer.OrdinalIgnoreCase)
            .Select(component =>
            {
                var ordered = OrderVersions(
                        component.Versions
                            .Where(version => ShouldExposeVersion(version, onlyPublished, includeArchived))
                            .Select(version => (Component: component, Version: version)),
                        maxVersions)
                    .ToList();
                return new ClientPluginReleaseComponentDto(
                    "Plugin",
                    component.ComponentKey,
                    component.DisplayName,
                    component.Description,
                    component.IconKind,
                    component.AccentColor,
                    ordered.Select(item => ToPluginVersion(item.Component, item.Version)).ToList());
            })
            .ToList();
    }

    // 硬删除数据库先行后，文件由幂等任务清理；清理未完成期间 catalog 不得把已删文件的版本继续当作可用。
    // 历史/详情读取不传 excludedRelativePaths，行为不变。
    public static ClientHostReleaseComponentDto ToHostComponentExcludingMissingFiles(
        IEnumerable<ClientReleaseComponent> components,
        ISet<string> excludedRelativePaths,
        int? maxVersions = null,
        bool onlyPublished = false,
        bool includeArchived = false)
    {
        var versions = components
            .Where(component => component.ComponentKind == ClientReleaseComponentKind.Host)
            .SelectMany(component => component.Versions
                .Where(version => ShouldExposeVersion(version, onlyPublished, includeArchived))
                .Select(version => (
                    Component: component,
                    Version: version,
                    FilesPresent: VersionFilesPresent(version, excludedRelativePaths)))
                .Where(item => item.FilesPresent))
            .OrderByDescending(item => item.Version.Version, VersionComparer)
            .ThenByDescending(item => item.Version.PublishedAtUtc ?? item.Version.CreatedAtUtc)
            .Take(maxVersions ?? int.MaxValue)
            .Select(item => ToHostVersion(item.Component, item.Version, item.FilesPresent))
            .ToList();

        return new ClientHostReleaseComponentDto("Host", "Edge Host", versions);
    }

    public static IReadOnlyList<ClientPluginReleaseComponentDto> ToPluginComponentsExcludingMissingFiles(
        IEnumerable<ClientReleaseComponent> components,
        ISet<string> excludedRelativePaths,
        int? maxVersions = null,
        bool onlyPublished = false,
        bool includeArchived = false)
    {
        return components
            .Where(component => component.ComponentKind == ClientReleaseComponentKind.Plugin)
            .OrderBy(component => component.ComponentKey, StringComparer.OrdinalIgnoreCase)
            .Select(component =>
            {
                var ordered = component.Versions
                    .Where(version => ShouldExposeVersion(version, onlyPublished, includeArchived))
                    .Select(version => (
                        Component: component,
                        Version: version,
                        FilesPresent: VersionFilesPresent(version, excludedRelativePaths)))
                    .Where(item => item.FilesPresent)
                    .OrderByDescending(item => item.Version.Version, VersionComparer)
                    .ThenByDescending(item => item.Version.PublishedAtUtc ?? item.Version.CreatedAtUtc)
                    .Take(maxVersions ?? int.MaxValue)
                    .ToList();
                return new ClientPluginReleaseComponentDto(
                    "Plugin",
                    component.ComponentKey,
                    component.DisplayName,
                    component.Description,
                    component.IconKind,
                    component.AccentColor,
                    ordered.Select(item => ToPluginVersion(item.Component, item.Version, item.FilesPresent)).ToList());
            })
            .Where(component => component.Versions.Count > 0)
            .ToList();
    }

    private static bool VersionFilesPresent(
        ClientReleaseVersion version,
        ISet<string> excludedRelativePaths)
    {
        return !version.Artifacts.Any(artifact => excludedRelativePaths.Contains(artifact.RelativePath));
    }

    public static PublicClientHostDownloadComponentDto ToPublicHostComponent(
        IEnumerable<ClientReleaseComponent> components,
        int? maxVersions = null)
    {
        var versions = OrderVersions(
                components
                    .Where(component => component.ComponentKind == ClientReleaseComponentKind.Host)
                    .SelectMany(component => component.Versions
                        .Where(version => ShouldExposeVersion(version, onlyPublished: true, includeArchived: false))
                        .Select(version => (Component: component, Version: version))),
                maxVersions)
            .Select(item => ToPublicHostVersion(item.Component, item.Version))
            .ToList();

        return new PublicClientHostDownloadComponentDto("Host", "Edge Host", versions);
    }

    public static IReadOnlyList<PublicClientPluginCatalogComponentDto> ToPublicPluginComponents(
        IEnumerable<ClientReleaseComponent> components,
        int? maxVersions = null)
    {
        return components
            .Where(component => component.ComponentKind == ClientReleaseComponentKind.Plugin)
            .OrderBy(component => component.ComponentKey, StringComparer.OrdinalIgnoreCase)
            .Select(component =>
            {
                var ordered = OrderVersions(
                        component.Versions
                            .Where(version => ShouldExposeVersion(version, onlyPublished: true, includeArchived: false))
                            .Select(version => (Component: component, Version: version)),
                        maxVersions)
                    .ToList();
                return new PublicClientPluginCatalogComponentDto(
                    "Plugin",
                    component.ComponentKey,
                    component.DisplayName,
                    component.Description,
                    component.IconKind,
                    component.AccentColor,
                    ordered.Select(item => ToPublicPluginVersion(item.Component, item.Version)).ToList());
            })
            .ToList();
    }

    public static ClientHostVersionEntryDto ToHostVersion(
        ClientReleaseComponent component,
        ClientReleaseVersion release,
        bool filesPresent = true)
    {
        return new ClientHostVersionEntryDto(
            release.Id,
            component.Id,
            component.Channel,
            release.Version,
            release.HostApiVersion,
            component.TargetRuntime,
            release.TargetFramework,
            release.DownloadUrl,
            release.Sha256,
            release.PackageSize,
            release.ReleaseNotes,
            release.Status.ToString(),
            release.Signature,
            release.Publisher,
            release.CreatedAtUtc,
            release.PublishedAtUtc,
            release.DeletedAtUtc,
            release.DeletionReason,
            release.DeletionFailure,
            filesPresent);
    }

    public static ClientPluginVersionEntryDto ToPluginVersion(
        ClientReleaseComponent component,
        ClientReleaseVersion release,
        bool filesPresent = true)
    {
        return new ClientPluginVersionEntryDto(
            release.Id,
            component.Id,
            component.Channel,
            release.Version,
            release.HostApiVersion,
            release.MinHostVersion ?? string.Empty,
            release.MaxHostVersion ?? string.Empty,
            component.TargetRuntime,
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
            release.PublishedAtUtc,
            release.DeletedAtUtc,
            release.DeletionReason,
            release.DeletionFailure,
            filesPresent);
    }

    public static PublicClientHostVersionDto ToPublicHostVersion(
        ClientReleaseComponent component,
        ClientReleaseVersion release)
    {
        return new PublicClientHostVersionDto(
            component.Channel,
            release.Version,
            release.HostApiVersion,
            component.TargetRuntime,
            release.TargetFramework,
            release.Sha256,
            release.PackageSize,
            release.ReleaseNotes,
            release.Status.ToString(),
            release.Publisher,
            release.PublishedAtUtc);
    }

    public static PublicClientPluginVersionDto ToPublicPluginVersion(
        ClientReleaseComponent component,
        ClientReleaseVersion release)
    {
        return new PublicClientPluginVersionDto(
            component.Channel,
            release.Version,
            release.HostApiVersion,
            release.MinHostVersion ?? string.Empty,
            release.MaxHostVersion ?? string.Empty,
            component.TargetRuntime,
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
        ClientReleaseVersion release,
        string hostVersion,
        string hostApiVersion,
        out string? issue)
    {
        if (!string.Equals(release.HostApiVersion, hostApiVersion, StringComparison.OrdinalIgnoreCase))
        {
            issue = $"hostApiVersion 不匹配: 插件要求 {release.HostApiVersion}, 当前宿主 {hostApiVersion}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(release.MinHostVersion)
            || string.IsNullOrWhiteSpace(release.MaxHostVersion)
            || !IsVersionInRange(hostVersion, release.MinHostVersion, release.MaxHostVersion))
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

    public static IReadOnlyList<(ClientReleaseComponent Component, ClientReleaseVersion Version)> OrderVersions(
        IEnumerable<(ClientReleaseComponent Component, ClientReleaseVersion Version)> releases,
        int? maxVersions = null)
    {
        var ordered = releases
            .OrderByDescending(item => item.Version.Version, VersionComparer)
            .ThenByDescending(item => item.Version.PublishedAtUtc ?? item.Version.CreatedAtUtc);

        return (maxVersions is { } max ? ordered.Take(max) : ordered).ToList();
    }

    private static bool IsVersionInRange(string hostVersion, string minHostVersion, string maxHostVersion)
    {
        return CompareVersions(hostVersion, minHostVersion) >= 0
            && CompareVersions(hostVersion, maxHostVersion) <= 0;
    }

    private static bool ShouldExposeVersion(
        ClientReleaseVersion version,
        bool onlyPublished,
        bool includeArchived)
    {
        var publishedMatch = !onlyPublished
            || version.Status == ClientReleaseStatus.Published
            || version.Status == ClientReleaseStatus.Deprecated;
        var archiveMatch = includeArchived
            || (version.Status != ClientReleaseStatus.Archived
                && version.Status != ClientReleaseStatus.Deleted
                && version.Status != ClientReleaseStatus.DeleteFailed
                && version.Status != ClientReleaseStatus.DeleteRequested);
        return publishedMatch && archiveMatch;
    }
}
