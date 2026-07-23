using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;

namespace IIoT.ProductionService.ClientReleases;

internal static class DeviceClientVersionInventoryMapping
{
    private static readonly TimeSpan VersionReportStaleThreshold = TimeSpan.FromHours(24);

    internal sealed record ReferenceCatalog(
        (ClientReleaseComponent Component, ClientReleaseVersion Version) Host,
        IReadOnlyDictionary<string, (ClientReleaseComponent Component, ClientReleaseVersion Version)> PluginsByModule);

    internal static ReferenceCatalog BuildReferenceCatalog(
        IReadOnlyList<ClientReleaseComponent> components)
    {
        var referenceHost = components
            .Where(component => component.ComponentKind == ClientReleaseComponentKind.Host)
            .SelectMany(component => component.Versions
                .Where(version => version.Status == ClientReleaseStatus.Published)
                .Select(version => (Component: component, Version: version)))
            .OrderByDescending(item => item.Version.Version, VersionStringComparer.Instance)
            .ThenByDescending(item => item.Version.PublishedAtUtc ?? item.Version.CreatedAtUtc)
            .FirstOrDefault();
        var referencePluginsByModule = components
            .Where(component => component.ComponentKind == ClientReleaseComponentKind.Plugin)
            .ToDictionary(
                component => component.ComponentKey,
                component => component.Versions
                    .Where(version => version.Status == ClientReleaseStatus.Published)
                    .Select(version => (Component: component, Version: version))
                    .OrderByDescending(item => item.Version.Version, VersionStringComparer.Instance)
                    .ThenByDescending(item => item.Version.PublishedAtUtc ?? item.Version.CreatedAtUtc)
                    .FirstOrDefault(),
                StringComparer.OrdinalIgnoreCase);

        return new ReferenceCatalog(referenceHost, referencePluginsByModule);
    }

    internal static DeviceClientVersionInventoryDto BuildInventory(
        Device device,
        DeviceClientVersionSnapshot? snapshot,
        DeviceClientState? state,
        ReferenceCatalog referenceCatalog,
        DateTime utcNow)
    {
        var hostStatus = ResolveHostStatus(state, referenceCatalog.Host, out var hostIssue);
        var installedPlugins = snapshot?.InstalledPlugins ?? [];
        var pluginRows = installedPlugins
            .OrderBy(plugin => plugin.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(plugin => BuildPluginInventory(snapshot, plugin, referenceCatalog.PluginsByModule))
            .ToList();
        var runtimeLocalIpAddresses = state?.GetRuntimeLocalIpAddresses() ?? [];
        var snapshotLocalIpAddresses = state?.GetVersionLocalIpAddresses() ?? snapshot?.GetLocalIpAddresses() ?? [];
        var localIpAddresses = runtimeLocalIpAddresses.Count > 0
            ? runtimeLocalIpAddresses
            : snapshotLocalIpAddresses;
        var primaryIp = localIpAddresses.FirstOrDefault()
            ?? ClientReleaseText.NormalizeOptional(state?.RuntimeRemoteIpAddress)
            ?? ClientReleaseText.NormalizeOptional(state?.VersionRemoteIpAddress)
            ?? ClientReleaseText.NormalizeOptional(snapshot?.RemoteIpAddress);
        var installStatus = ResolveInstallStatus(state, hostStatus, pluginRows);
        var softwareStatus = DeviceClientSoftwareStatusResolver.Resolve(state, utcNow);
        var versionIssue = ResolveVersionIssue(state, hostStatus, hostIssue, pluginRows, utcNow);
        var cloudIssue = ResolveCloudIssue();
        var issue = softwareStatus.Issue ?? versionIssue ?? cloudIssue;

        return new DeviceClientVersionInventoryDto(
            device.Id,
            device.DeviceName,
            device.Code,
            primaryIp,
            localIpAddresses,
            state?.VersionRemoteIpAddress ?? snapshot?.RemoteIpAddress,
            state?.Channel ?? snapshot?.Channel,
            state?.HostVersion ?? snapshot?.HostVersion,
            state?.HostApiVersion ?? snapshot?.HostApiVersion,
            hostStatus,
            hostIssue,
            installStatus,
            softwareStatus.SoftwareStatus,
            BuildCurrentVersion(state, snapshot, pluginRows),
            issue,
            versionIssue,
            cloudIssue,
            state?.LastRuntimeHeartbeatAtUtc,
            state?.VersionReportedAtUtc ?? snapshot?.ReportedAtUtc,
            state?.VersionReceivedAtUtc ?? snapshot?.ReceivedAtUtc,
            pluginRows);
    }

    private static string ResolveHostStatus(
        DeviceClientState? state,
        (ClientReleaseComponent Component, ClientReleaseVersion Version) referenceHost,
        out string? issue)
    {
        issue = null;
        if (state?.HostVersion is null || state.HostApiVersion is null)
        {
            return "MissingReport";
        }

        if (referenceHost.Version is null)
        {
            return "NoRelease";
        }

        if (!string.Equals(state.HostApiVersion, referenceHost.Version.HostApiVersion, StringComparison.OrdinalIgnoreCase))
        {
            issue = $"hostApiVersion 不匹配: 设备 {state.HostApiVersion}, 推荐宿主 {referenceHost.Version.HostApiVersion}";
            return "Incompatible";
        }

        return ClientReleaseMapping.CompareVersions(state.HostVersion, referenceHost.Version.Version) < 0
            ? "UpdateAvailable"
            : "Latest";
    }

    private static string ResolveInstallStatus(
        DeviceClientState? state,
        string hostStatus,
        IReadOnlyList<DeviceClientPluginInventoryDto> plugins)
    {
        if (state?.VersionReceivedAtUtc is null)
        {
            return "MissingReport";
        }

        if (hostStatus == "Incompatible" || plugins.Any(plugin => plugin.UpdateStatus == "Incompatible"))
        {
            return "Incompatible";
        }

        if (hostStatus == "UpdateAvailable" || plugins.Any(plugin => plugin.UpdateStatus == "UpdateAvailable"))
        {
            return "UpdateAvailable";
        }

        if (hostStatus == "NoRelease" || plugins.Any(plugin => plugin.UpdateStatus == "NoRelease"))
        {
            return "NoRelease";
        }

        return "Normal";
    }

    private static string? ResolveVersionIssue(
        DeviceClientState? state,
        string hostStatus,
        string? hostIssue,
        IReadOnlyList<DeviceClientPluginInventoryDto> plugins,
        DateTime utcNow)
    {
        if (state?.VersionReceivedAtUtc is null)
        {
            return "客户端尚未上报安装状态。";
        }

        if (utcNow.ToUniversalTime() - state.VersionReceivedAtUtc.Value.ToUniversalTime() > VersionReportStaleThreshold)
        {
            return "超过 24 小时未上报版本。";
        }

        if (!string.IsNullOrWhiteSpace(hostIssue))
        {
            return hostIssue;
        }

        var pluginIssue = plugins
            .Select(plugin => plugin.CompatibilityIssue)
            .FirstOrDefault(issue => !string.IsNullOrWhiteSpace(issue));
        if (!string.IsNullOrWhiteSpace(pluginIssue))
        {
            return pluginIssue;
        }

        if (hostStatus == "UpdateAvailable" || plugins.Any(plugin => plugin.UpdateStatus == "UpdateAvailable"))
        {
            return "存在可安装的新版本。";
        }

        if (hostStatus == "NoRelease")
        {
            return "当前渠道暂无可对比的宿主发布版本。";
        }

        if (plugins.Any(plugin => plugin.UpdateStatus == "NoRelease"))
        {
            return "部分插件暂无可对比的发布版本。";
        }

        return null;
    }

    private static string? ResolveCloudIssue()
    {
        return null;
    }

    private static string BuildCurrentVersion(
        DeviceClientState? state,
        DeviceClientVersionSnapshot? snapshot,
        IReadOnlyList<DeviceClientPluginInventoryDto> plugins)
    {
        var hostVersion = state?.HostVersion ?? snapshot?.HostVersion;
        if (hostVersion is null)
        {
            return "-";
        }

        return plugins.Count == 0
            ? $"宿主 {hostVersion}"
            : $"宿主 {hostVersion} / 插件 {plugins.Count} 个";
    }

    private static DeviceClientPluginInventoryDto BuildPluginInventory(
        DeviceClientVersionSnapshot? snapshot,
        DeviceClientPluginVersion plugin,
        IReadOnlyDictionary<string, (ClientReleaseComponent Component, ClientReleaseVersion Version)> referencePluginsByModule)
    {
        if (!referencePluginsByModule.TryGetValue(plugin.ModuleId, out var referenceRelease)
            || referenceRelease.Version is null)
        {
            return new DeviceClientPluginInventoryDto(
                plugin.ModuleId,
                plugin.DisplayName,
                plugin.Version,
                plugin.HostApiVersion,
                plugin.Enabled,
                "NoRelease",
                null);
        }

        string? compatibilityIssue = null;
        var compatible = snapshot is not null
            && ClientReleaseMapping.IsCompatibleWithHost(
                referenceRelease.Version,
                snapshot.HostVersion,
                snapshot.HostApiVersion,
                out compatibilityIssue);
        var updateStatus = compatible
            ? ClientReleaseMapping.CompareVersions(plugin.Version, referenceRelease.Version.Version) < 0
                ? "UpdateAvailable"
                : "Latest"
            : "Incompatible";

        return new DeviceClientPluginInventoryDto(
            plugin.ModuleId,
            plugin.DisplayName ?? referenceRelease.Component.DisplayName,
            plugin.Version,
            plugin.HostApiVersion,
            plugin.Enabled,
            updateStatus,
            compatibilityIssue);
    }

    private sealed class VersionStringComparer : IComparer<string>
    {
        public static VersionStringComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            return ClientReleaseMapping.CompareVersions(x, y);
        }
    }
}
