using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.ClientReleases;

[AuthorizeRequirement("Device.Read")]
public sealed record GetDeviceClientVersionInventoryQuery(
    string? Channel = null,
    string? TargetRuntime = null,
    string? Keyword = null) : IHumanQuery<Result<IReadOnlyList<DeviceClientVersionInventoryDto>>>;

public sealed class GetDeviceClientVersionInventoryHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IReadRepository<Device> deviceRepository,
    IReadRepository<DeviceClientVersionSnapshot> snapshotRepository,
    IReadRepository<ClientHostRelease> hostReleaseRepository,
    IReadRepository<ClientPluginRelease> pluginReleaseRepository)
    : IQueryHandler<GetDeviceClientVersionInventoryQuery, Result<IReadOnlyList<DeviceClientVersionInventoryDto>>>
{
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

        var channel = string.IsNullOrWhiteSpace(request.Channel) ? "stable" : request.Channel.Trim();
        var hostReleases = await hostReleaseRepository.GetListAsync(
            new ClientHostReleasesByChannelSpec(channel, request.TargetRuntime, onlyPublished: true),
            cancellationToken);
        var pluginReleases = await pluginReleaseRepository.GetListAsync(
            new ClientPluginReleasesByChannelSpec(channel, request.TargetRuntime, onlyPublished: true),
            cancellationToken);
        var latestHost = hostReleases
            .OrderByDescending(release => release.Version, VersionStringComparer.Instance)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .FirstOrDefault();
        var latestPluginsByModule = pluginReleases
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
            .Select(device => BuildInventory(device, snapshotByDevice.GetValueOrDefault(device.Id), latestHost, latestPluginsByModule))
            .ToList();

        return Result.Success<IReadOnlyList<DeviceClientVersionInventoryDto>>(result);
    }

    private static DeviceClientVersionInventoryDto BuildInventory(
        Device device,
        DeviceClientVersionSnapshot? snapshot,
        ClientHostRelease? latestHost,
        IReadOnlyDictionary<string, ClientPluginRelease> latestPluginsByModule)
    {
        var hostStatus = ResolveHostStatus(snapshot, latestHost, out var hostIssue);
        var installedPlugins = snapshot?.InstalledPlugins ?? [];
        var pluginRows = installedPlugins
            .OrderBy(plugin => plugin.ModuleId, StringComparer.OrdinalIgnoreCase)
            .Select(plugin => BuildPluginInventory(snapshot, plugin, latestPluginsByModule))
            .ToList();

        return new DeviceClientVersionInventoryDto(
            device.Id,
            device.DeviceName,
            device.Code,
            snapshot?.Channel,
            snapshot?.HostVersion,
            snapshot?.HostApiVersion,
            hostStatus,
            latestHost?.Version,
            hostIssue,
            snapshot?.ReportedAtUtc,
            snapshot?.ReceivedAtUtc,
            pluginRows);
    }

    private static string ResolveHostStatus(
        DeviceClientVersionSnapshot? snapshot,
        ClientHostRelease? latestHost,
        out string? issue)
    {
        issue = null;
        if (snapshot is null)
        {
            return "MissingReport";
        }

        if (latestHost is null)
        {
            return "NoRelease";
        }

        if (!string.Equals(snapshot.HostApiVersion, latestHost.HostApiVersion, StringComparison.OrdinalIgnoreCase))
        {
            issue = $"hostApiVersion 不匹配: 设备 {snapshot.HostApiVersion}, 最新宿主 {latestHost.HostApiVersion}";
            return "Incompatible";
        }

        return ClientReleaseMapping.CompareVersions(snapshot.HostVersion, latestHost.Version) < 0
            ? "UpdateAvailable"
            : "Latest";
    }

    private static DeviceClientPluginInventoryDto BuildPluginInventory(
        DeviceClientVersionSnapshot? snapshot,
        DeviceClientPluginVersion plugin,
        IReadOnlyDictionary<string, ClientPluginRelease> latestPluginsByModule)
    {
        if (!latestPluginsByModule.TryGetValue(plugin.ModuleId, out var latestRelease))
        {
            return new DeviceClientPluginInventoryDto(
                plugin.ModuleId,
                plugin.DisplayName,
                plugin.Version,
                plugin.HostApiVersion,
                plugin.Enabled,
                "NoRelease",
                null,
                null);
        }

        string? compatibilityIssue = null;
        var compatible = snapshot is not null
            && ClientReleaseMapping.IsCompatibleWithHost(
                latestRelease,
                snapshot.HostVersion,
                snapshot.HostApiVersion,
                out compatibilityIssue);
        var updateStatus = compatible
            ? ClientReleaseMapping.CompareVersions(plugin.Version, latestRelease.Version) < 0
                ? "UpdateAvailable"
                : "Latest"
            : "Incompatible";

        return new DeviceClientPluginInventoryDto(
            plugin.ModuleId,
            plugin.DisplayName ?? latestRelease.DisplayName,
            plugin.Version,
            plugin.HostApiVersion,
            plugin.Enabled,
            updateStatus,
            latestRelease.Version,
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
