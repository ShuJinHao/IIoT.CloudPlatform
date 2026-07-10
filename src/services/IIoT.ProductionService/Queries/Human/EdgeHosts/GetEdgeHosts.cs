using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Contracts.EdgeHosts;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.EdgeHosts;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.EdgeHosts;

[AuthorizeRequirement(EdgeHostPermissions.Read)]
public sealed record GetEdgeHostPagedListQuery(
    Pagination PaginationParams,
    string? Keyword = null) : IHumanQuery<Result<PagedList<EdgeHostListItemDto>>>;

[AuthorizeRequirement(EdgeHostPermissions.Read)]
public sealed record GetEdgeHostDetailQuery(Guid DeviceId) : IHumanQuery<Result<EdgeHostDto>>;

[AuthorizeRequirement(EdgeHostPermissions.Read)]
public sealed record GetEdgeHostPlcRuntimeStatesQuery(Guid DeviceId)
    : IHumanQuery<Result<IReadOnlyList<EdgeHostPlcRuntimeStateDto>>>;

public sealed class GetEdgeHostPagedListHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IEdgeHostOverviewQueryService overviewQueryService,
    IDeviceClientStateStore clientStateStore,
    IEdgeHostPlcRuntimeStateStore runtimeStateStore)
    : IQueryHandler<GetEdgeHostPagedListQuery, Result<PagedList<EdgeHostListItemDto>>>
{
    public async Task<Result<PagedList<EdgeHostListItemDto>>> Handle(
        GetEdgeHostPagedListQuery request,
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
            return Result.Success(new PagedList<EdgeHostListItemDto>([], 0, request.PaginationParams));
        }

        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var page = await overviewQueryService.SearchAccessibleDevicesAsync(
            allowedDeviceIds,
            request.Keyword,
            skip,
            request.PaginationParams.PageSize,
            cancellationToken);
        var deviceIds = page.Devices.Select(device => device.DeviceId).ToList();
        var statesByDevice = await GetClientStatesByDeviceAsync(deviceIds, cancellationToken);
        var plcStatesByDevice = await GetPlcStatesByDeviceAsync(deviceIds, cancellationToken);
        var utcNow = DateTime.UtcNow;

        var pageItems = page.Devices
            .Select(device => EdgeHostMapping.ToListItemDto(
                device.DeviceId,
                device.DeviceName,
                device.ClientCode,
                statesByDevice.GetValueOrDefault(device.DeviceId),
                plcStatesByDevice.GetValueOrDefault(device.DeviceId) ?? [],
                utcNow))
            .ToList();

        return Result.Success(new PagedList<EdgeHostListItemDto>(
            pageItems,
            page.TotalCount,
            request.PaginationParams));
    }

    private async Task<IReadOnlyDictionary<Guid, DeviceClientState>> GetClientStatesByDeviceAsync(
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken cancellationToken)
    {
        var states = await clientStateStore.GetStatesByDevicesAsync(deviceIds, cancellationToken);
        return states
            .GroupBy(state => state.DeviceId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(state => state.UpdatedAtUtc).First());
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<EdgeHostPlcRuntimeState>>> GetPlcStatesByDeviceAsync(
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken cancellationToken)
    {
        var states = await runtimeStateStore.GetByDevicesAsync(deviceIds, cancellationToken);
        return states
            .GroupBy(state => state.DeviceId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<EdgeHostPlcRuntimeState>)group
                    .OrderByDescending(state => state.LastSeenAtUtc)
                    .ThenBy(state => state.PlcCode, StringComparer.OrdinalIgnoreCase)
                    .ToList());
    }

}

public sealed class GetEdgeHostDetailHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IReadRepository<Device> deviceRepository,
    IDeviceClientStateStore clientStateStore,
    IEdgeHostPlcRuntimeStateStore runtimeStateStore)
    : IQueryHandler<GetEdgeHostDetailQuery, Result<EdgeHostDto>>
{
    public async Task<Result<EdgeHostDto>> Handle(
        GetEdgeHostDetailQuery request,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveDeviceAsync(
            request.DeviceId,
            currentUserDeviceAccessService,
            deviceRepository,
            cancellationToken);
        if (!resolved.IsSuccess)
        {
            return Result.From(resolved);
        }

        var device = resolved.Value!;
        var clientState = await clientStateStore.GetStateByIdentityAsync(
            device.Id,
            device.Code,
            cancellationToken);
        var plcStates = await runtimeStateStore.GetByIdentityAsync(
            device.Id,
            device.Code,
            cancellationToken);
        var utcNow = DateTime.UtcNow;

        return Result.Success(EdgeHostMapping.ToDetailDto(device, clientState, plcStates, utcNow));
    }

    internal static async Task<Result<Device>> ResolveDeviceAsync(
        Guid deviceId,
        ICurrentUserDeviceAccessService currentUserDeviceAccessService,
        IReadRepository<Device> deviceRepository,
        CancellationToken cancellationToken)
    {
        if (deviceId == Guid.Empty)
        {
            return Result.Invalid("设备不能为空。");
        }

        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(deviceId),
            cancellationToken);
        if (device is null)
        {
            return Result.NotFound("设备不存在。");
        }

        var access = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(device.Id, cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.Failure(access.Errors?.ToArray() ?? ["越权: 未授权访问该设备"]);
        }

        return Result.Success(device);
    }
}

public sealed class GetEdgeHostPlcRuntimeStatesHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IReadRepository<Device> deviceRepository,
    IEdgeHostPlcRuntimeStateStore runtimeStateStore)
    : IQueryHandler<GetEdgeHostPlcRuntimeStatesQuery, Result<IReadOnlyList<EdgeHostPlcRuntimeStateDto>>>
{
    public async Task<Result<IReadOnlyList<EdgeHostPlcRuntimeStateDto>>> Handle(
        GetEdgeHostPlcRuntimeStatesQuery request,
        CancellationToken cancellationToken)
    {
        var resolved = await GetEdgeHostDetailHandler.ResolveDeviceAsync(
            request.DeviceId,
            currentUserDeviceAccessService,
            deviceRepository,
            cancellationToken);
        if (!resolved.IsSuccess)
        {
            return Result.From(resolved);
        }

        var device = resolved.Value!;
        var states = await runtimeStateStore.GetByIdentityAsync(
            device.Id,
            device.Code,
            cancellationToken);
        var items = states
            .OrderByDescending(state => state.LastSeenAtUtc)
            .ThenBy(state => state.PlcCode, StringComparer.OrdinalIgnoreCase)
            .Select(EdgeHostMapping.ToRuntimeStateDto)
            .ToList();

        return Result.Success((IReadOnlyList<EdgeHostPlcRuntimeStateDto>)items);
    }
}
