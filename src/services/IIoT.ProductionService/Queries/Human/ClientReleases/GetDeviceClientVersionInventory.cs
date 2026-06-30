using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.ClientReleases;

[AuthorizeRequirement(ClientReleasePermissions.Read)]
public sealed record GetDeviceClientVersionInventoryQuery(
    string? Channel = null,
    string? TargetRuntime = null,
    string? Keyword = null) : IHumanQuery<Result<IReadOnlyList<DeviceClientVersionInventoryDto>>>;

public sealed class GetDeviceClientVersionInventoryHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IReadRepository<Device> deviceRepository,
    IReadRepository<DeviceClientVersionSnapshot> snapshotRepository,
    IReadRepository<EdgeDeviceRuntimeHeartbeat> runtimeHeartbeatRepository,
    IReadRepository<ClientHostRelease> hostReleaseRepository,
    IReadRepository<ClientPluginRelease> pluginReleaseRepository)
    : IQueryHandler<GetDeviceClientVersionInventoryQuery, Result<IReadOnlyList<DeviceClientVersionInventoryDto>>>
{
    private static readonly TimeSpan ReportStaleThreshold = TimeSpan.FromHours(24);

    public async Task<Result<IReadOnlyList<DeviceClientVersionInventoryDto>>> Handle(
        GetDeviceClientVersionInventoryQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await currentUserDeviceAccessService.GetAccessibleDeviceIdsAsync(cancellationToken);
        if (!scope.IsSuccess)
        {
            return Result.Failure(scope.Errors?.ToArray() ?? ["用户凭证异常"]);
        }

        var allowedDeviceIds = scope.Value?.ToList();
        if (allowedDeviceIds is { Count: 0 })
        {
            return Result.Success<IReadOnlyList<DeviceClientVersionInventoryDto>>([]);
        }

        var devices = await deviceRepository.GetListAsync(
            new DevicePagedSpec(0, 0, allowedDeviceIds, request.Keyword, isPaging: false),
            cancellationToken);
        var snapshots = await snapshotRepository.GetListAsync(
            new DeviceClientVersionSnapshotsByDevicesSpec(devices.Select(device => device.Id).ToList()),
            cancellationToken);
        var snapshotByDevice = snapshots.ToDictionary(snapshot => snapshot.DeviceId);
        var runtimeHeartbeats = await runtimeHeartbeatRepository.GetListAsync(
            new EdgeDeviceRuntimeHeartbeatsByDevicesSpec(devices.Select(device => device.Id).ToList()),
            cancellationToken);
        var runtimeHeartbeatByDevice = runtimeHeartbeats
            .GroupBy(heartbeat => heartbeat.DeviceId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(heartbeat => heartbeat.LastHeartbeatAtUtc).First());

        var channel = string.IsNullOrWhiteSpace(request.Channel) ? "stable" : request.Channel.Trim();
        var hostReleases = await hostReleaseRepository.GetListAsync(
            new ClientHostReleasesByChannelSpec(channel, request.TargetRuntime, onlyPublished: true),
            cancellationToken);
        var pluginReleases = await pluginReleaseRepository.GetListAsync(
            new ClientPluginReleasesByChannelSpec(channel, request.TargetRuntime, onlyPublished: true),
            cancellationToken);
        var referenceHost = hostReleases
            .OrderByDescending(release => release.Version, VersionStringComparer.Instance)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .FirstOrDefault();
        var referencePluginsByModule = pluginReleases
            .GroupBy(release => release.ModuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(release => release.Version, VersionStringComparer.Instance)
                    .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        var result = devices
            .OrderBy(device => device.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Select(device => BuildInventory(
                device,
                snapshotByDevice.GetValueOrDefault(device.Id),
                runtimeHeartbeatByDevice.GetValueOrDefault(device.Id),
                referenceHost,
                referencePluginsByModule))
            .ToList();

        return Result.Success<IReadOnlyList<DeviceClientVersionInventoryDto>>(result);
    }

    private static DeviceClientVersionInventoryDto BuildInventory(
        Device device,
        DeviceClientVersionSnapshot? snapshot,
        EdgeDeviceRuntimeHeartbeat? runtimeHeartbeat,
        ClientHostRelease? referenceHost,
        IReadOnlyDictionary<string, ClientPluginRelease> referencePluginsByModule)
    {
        var hostStatus = ResolveHostStatus(snapshot, referenceHost, out var hostIssue);
        var installedPlugins = snapshot?.InstalledPlugins ?? [];
        var pluginRows = installedPlugins
            .OrderBy(plugin => plugin.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(plugin => BuildPluginInventory(snapshot, plugin, referencePluginsByModule))
            .ToList();
        var runtimeLocalIpAddresses = runtimeHeartbeat?.GetLocalIpAddresses() ?? [];
        var snapshotLocalIpAddresses = snapshot?.GetLocalIpAddresses() ?? [];
        var localIpAddresses = runtimeLocalIpAddresses.Count > 0
            ? runtimeLocalIpAddresses
            : snapshotLocalIpAddresses;
        var primaryIp = localIpAddresses.FirstOrDefault()
            ?? NormalizeOptional(runtimeHeartbeat?.RemoteIpAddress)
            ?? NormalizeOptional(snapshot?.RemoteIpAddress);
        var installStatus = ResolveInstallStatus(snapshot, hostStatus, pluginRows);
        var softwareStatus = ResolveSoftwareStatus(runtimeHeartbeat, out var runtimeIssue);
        var versionIssue = ResolveVersionIssue(snapshot, hostStatus, hostIssue, pluginRows);
        var cloudIssue = ResolveCloudIssue();
        var issue = runtimeIssue ?? versionIssue ?? cloudIssue;

        return new DeviceClientVersionInventoryDto(
            device.Id,
            device.DeviceName,
            device.Code,
            primaryIp,
            localIpAddresses,
            snapshot?.RemoteIpAddress,
            snapshot?.Channel,
            snapshot?.HostVersion,
            snapshot?.HostApiVersion,
            hostStatus,
            hostIssue,
            installStatus,
            softwareStatus,
            BuildCurrentVersion(snapshot, pluginRows),
            issue,
            versionIssue,
            cloudIssue,
            runtimeHeartbeat?.LastHeartbeatAtUtc,
            snapshot?.ReportedAtUtc,
            snapshot?.ReceivedAtUtc,
            pluginRows);
    }

    private static string ResolveHostStatus(
        DeviceClientVersionSnapshot? snapshot,
        ClientHostRelease? referenceHost,
        out string? issue)
    {
        issue = null;
        if (snapshot is null)
        {
            return "MissingReport";
        }

        if (referenceHost is null)
        {
            return "NoRelease";
        }

        if (!string.Equals(snapshot.HostApiVersion, referenceHost.HostApiVersion, StringComparison.OrdinalIgnoreCase))
        {
            issue = $"hostApiVersion 不匹配: 设备 {snapshot.HostApiVersion}, 推荐宿主 {referenceHost.HostApiVersion}";
            return "Incompatible";
        }

        return ClientReleaseMapping.CompareVersions(snapshot.HostVersion, referenceHost.Version) < 0
            ? "UpdateAvailable"
            : "Latest";
    }

    private static string ResolveInstallStatus(
        DeviceClientVersionSnapshot? snapshot,
        string hostStatus,
        IReadOnlyList<DeviceClientPluginInventoryDto> plugins)
    {
        if (snapshot is null)
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

    private static string ResolveSoftwareStatus(
        EdgeDeviceRuntimeHeartbeat? runtimeHeartbeat,
        out string? issue)
    {
        issue = null;
        if (runtimeHeartbeat is null)
        {
            issue = "客户端尚未上报运行心跳。";
            return "MissingRuntimeHeartbeat";
        }

        if (DateTime.UtcNow - runtimeHeartbeat.LastHeartbeatAtUtc.ToUniversalTime() > ReportStaleThreshold)
        {
            issue = "超过 24 小时未收到运行心跳。";
            return "RuntimeHeartbeatStale";
        }

        return runtimeHeartbeat.Status switch
        {
            "Starting" => "Starting",
            "Running" => "Running",
            "Stopping" or "Stopped" => "Stopped",
            _ => "Unknown"
        };
    }

    private static string? ResolveVersionIssue(
        DeviceClientVersionSnapshot? snapshot,
        string hostStatus,
        string? hostIssue,
        IReadOnlyList<DeviceClientPluginInventoryDto> plugins)
    {
        if (snapshot is null)
        {
            return "客户端尚未上报安装状态。";
        }

        if (DateTime.UtcNow - snapshot.ReceivedAtUtc.ToUniversalTime() > ReportStaleThreshold)
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
        DeviceClientVersionSnapshot? snapshot,
        IReadOnlyList<DeviceClientPluginInventoryDto> plugins)
    {
        if (snapshot is null)
        {
            return "-";
        }

        return plugins.Count == 0
            ? $"宿主 {snapshot.HostVersion}"
            : $"宿主 {snapshot.HostVersion} / 插件 {plugins.Count} 个";
    }

    private static DeviceClientPluginInventoryDto BuildPluginInventory(
        DeviceClientVersionSnapshot? snapshot,
        DeviceClientPluginVersion plugin,
        IReadOnlyDictionary<string, ClientPluginRelease> referencePluginsByModule)
    {
        if (!referencePluginsByModule.TryGetValue(plugin.ModuleId, out var referenceRelease))
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
                referenceRelease,
                snapshot.HostVersion,
                snapshot.HostApiVersion,
                out compatibilityIssue);
        var updateStatus = compatible
            ? ClientReleaseMapping.CompareVersions(plugin.Version, referenceRelease.Version) < 0
                ? "UpdateAvailable"
                : "Latest"
            : "Incompatible";

        return new DeviceClientPluginInventoryDto(
            plugin.ModuleId,
            plugin.DisplayName ?? referenceRelease.DisplayName,
            plugin.Version,
            plugin.HostApiVersion,
            plugin.Enabled,
            updateStatus,
            compatibilityIssue);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
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
