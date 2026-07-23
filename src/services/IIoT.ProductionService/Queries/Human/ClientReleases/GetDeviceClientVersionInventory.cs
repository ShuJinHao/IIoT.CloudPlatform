using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Contracts.ClientReleases;
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
    IDeviceClientStateStore clientStateStore,
    IReadRepository<ClientReleaseComponent> componentRepository)
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
        var deviceIds = devices.Select(device => device.Id).ToList();
        var snapshots = await clientStateStore.GetVersionSnapshotsByDevicesAsync(deviceIds, cancellationToken);
        var snapshotByDevice = snapshots.ToDictionary(snapshot => snapshot.DeviceId);
        var states = await clientStateStore.GetStatesByDevicesAsync(deviceIds, cancellationToken);
        var stateByDevice = states
            .GroupBy(state => state.DeviceId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(state => state.UpdatedAtUtc).First());

        var channel = ClientReleaseText.NormalizeChannel(request.Channel);
        var components = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(channel, request.TargetRuntime, onlyPublished: true),
            cancellationToken);
        var referenceCatalog = DeviceClientVersionInventoryMapping.BuildReferenceCatalog(components);
        var utcNow = DateTime.UtcNow;

        var result = devices
            .OrderBy(device => device.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Select(device => DeviceClientVersionInventoryMapping.BuildInventory(
                device,
                snapshotByDevice.GetValueOrDefault(device.Id),
                stateByDevice.GetValueOrDefault(device.Id),
                referenceCatalog,
                utcNow))
            .ToList();

        return Result.Success<IReadOnlyList<DeviceClientVersionInventoryDto>>(result);
    }
}
