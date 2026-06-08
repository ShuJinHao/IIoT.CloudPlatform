using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.ProductionService.ClientReleases;

public static class ClientReleaseCatalogSchema
{
    public const int Version = 1;
}

public sealed record ClientReleaseCatalogDto(
    int CatalogSchemaVersion,
    string Channel,
    string? TargetRuntime,
    ClientHostReleaseDto? LatestHost,
    IReadOnlyList<ClientHostReleaseDto> HostReleases,
    IReadOnlyList<ClientPluginReleaseDto> PluginReleases,
    DateTime GeneratedAtUtc);

public sealed record ClientHostReleaseDto(
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

public sealed record ClientPluginReleaseDto(
    Guid Id,
    string ModuleId,
    string DisplayName,
    string? Description,
    string? IconKind,
    string? AccentColor,
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
    string? Channel,
    string? HostVersion,
    string? HostApiVersion,
    string HostUpdateStatus,
    string? LatestHostVersion,
    string? HostCompatibilityIssue,
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
    string? LatestVersion,
    string? CompatibilityIssue);

public sealed record PublicClientDownloadCatalogDto(
    int CatalogSchemaVersion,
    string Channel,
    string TargetRuntime,
    PublicClientHostDownloadDto? LatestHost,
    IReadOnlyList<PublicClientPluginCatalogItemDto> Plugins,
    DateTime GeneratedAtUtc);

public sealed record PublicClientHostDownloadDto(
    string Channel,
    string Version,
    string HostApiVersion,
    string TargetRuntime,
    string? TargetFramework,
    string DownloadUrl,
    string Sha256,
    long PackageSize,
    string? ReleaseNotes,
    string? Publisher,
    DateTime? PublishedAtUtc);

public sealed record PublicClientPluginCatalogItemDto(
    string ModuleId,
    string DisplayName,
    string? Description,
    string? IconKind,
    string? AccentColor,
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
    string? Publisher,
    DateTime? PublishedAtUtc);

internal static class ClientReleaseMapping
{
    private static readonly JsonElement EmptyArray = JsonDocument.Parse("[]").RootElement.Clone();

    public static ClientHostReleaseDto ToDto(ClientHostRelease release)
    {
        return new ClientHostReleaseDto(
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

    public static ClientPluginReleaseDto ToDto(ClientPluginRelease release)
    {
        return new ClientPluginReleaseDto(
            release.Id,
            release.ModuleId,
            release.DisplayName,
            release.Description,
            release.IconKind,
            release.AccentColor,
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

    private static bool IsVersionInRange(string hostVersion, string minHostVersion, string maxHostVersion)
    {
        return CompareVersions(hostVersion, minHostVersion) >= 0
            && CompareVersions(hostVersion, maxHostVersion) <= 0;
    }
}
