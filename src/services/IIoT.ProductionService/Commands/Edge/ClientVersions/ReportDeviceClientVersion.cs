using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.ClientVersions;

public sealed record DeviceClientPluginVersionReportItem(
    string ModuleId,
    string? DisplayName,
    string Version,
    string? HostApiVersion);

public sealed record ReportDeviceClientVersionCommand(
    Guid DeviceId,
    string ClientCode,
    string HostVersion,
    string HostApiVersion,
    IReadOnlyList<DeviceClientPluginVersionReportItem> InstalledPlugins,
    IReadOnlyList<string> EnabledPlugins,
    string Channel,
    DateTime ReportedAtUtc,
    IReadOnlyList<string>? LocalIpAddresses = null,
    string? RemoteIpAddress = null) : IDeviceCommand<Result<DeviceClientVersionReportResultDto>>;

public sealed class ReportDeviceClientVersionHandler(
    IDeviceIdentityQueryService deviceIdentityQueryService,
    IDeviceClientStateStore clientStateStore)
    : ICommandHandler<ReportDeviceClientVersionCommand, Result<DeviceClientVersionReportResultDto>>
{
    public async Task<Result<DeviceClientVersionReportResultDto>> Handle(
        ReportDeviceClientVersionCommand request,
        CancellationToken cancellationToken)
    {
        var clientCode = request.ClientCode?.Trim() ?? string.Empty;
        var identity = await deviceIdentityQueryService.GetByDeviceIdAsync(
            request.DeviceId,
            cancellationToken);
        if (identity is null)
        {
            return Result.Failure("版本上报失败: 设备不存在");
        }

        if (!string.Equals(identity.Code, clientCode, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("版本上报失败: ClientCode 与 DeviceId 不匹配");
        }

        var enabled = new HashSet<string>(
            request.EnabledPlugins ?? [],
            StringComparer.OrdinalIgnoreCase);
        var pluginVersions = (request.InstalledPlugins ?? [])
            .GroupBy(plugin => plugin.ModuleId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var plugin = group.First();
                return new DeviceClientPluginVersion(
                    plugin.ModuleId,
                    plugin.DisplayName,
                    plugin.Version,
                    plugin.HostApiVersion,
                    enabled.Contains(plugin.ModuleId));
            })
            .ToList();

        var snapshot = await clientStateStore.GetVersionSnapshotByDeviceAsync(request.DeviceId, cancellationToken);
        if (snapshot is null)
        {
            snapshot = new DeviceClientVersionSnapshot(
                request.DeviceId,
                clientCode,
                request.HostVersion,
                request.HostApiVersion,
                request.Channel,
                request.ReportedAtUtc,
                pluginVersions,
                request.LocalIpAddresses,
                request.RemoteIpAddress);
            clientStateStore.AddVersionSnapshot(snapshot);
        }
        else
        {
            snapshot.ReplaceReport(
                clientCode,
                request.HostVersion,
                request.HostApiVersion,
                request.Channel,
                request.ReportedAtUtc,
                pluginVersions,
                request.LocalIpAddresses,
                request.RemoteIpAddress);
        }

        var state = await clientStateStore.GetStateByIdentityAsync(request.DeviceId, clientCode, cancellationToken);
        if (state is null)
        {
            state = new DeviceClientState(request.DeviceId, clientCode);
            clientStateStore.AddState(state);
        }

        state.ApplyVersionReport(snapshot);

        await clientStateStore.SaveChangesAsync(cancellationToken);
        return Result.Success(new DeviceClientVersionReportResultDto(
            snapshot.DeviceId,
            snapshot.ReceivedAtUtc));
    }
}
