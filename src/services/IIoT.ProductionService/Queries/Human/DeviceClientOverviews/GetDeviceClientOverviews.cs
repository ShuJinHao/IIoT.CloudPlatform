using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.DeviceClientOverviews;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.DeviceClientOverviews;

[AuthorizeRequirement(DeviceClientOverviewPermissions.Read)]
public sealed record GetDeviceClientOverviewQuery(
    Pagination PaginationParams,
    string? Keyword = null,
    string? SortBy = null,
    string? SortDirection = null)
    : IHumanQuery<Result<PagedList<DeviceClientOverviewItemDto>>>;

[AuthorizeRequirement(ClientReleasePermissions.Read)]
public sealed record GetDeviceClientReleaseDetailQuery(
    Guid DeviceId,
    string? Channel = null,
    string? TargetRuntime = null)
    : IHumanQuery<Result<DeviceClientVersionInventoryDto>>;

public sealed class GetDeviceClientOverviewHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IDeviceClientOverviewQueryService overviewQueryService,
    IDeviceClientStateQueryService clientStateQueryService)
    : IQueryHandler<GetDeviceClientOverviewQuery, Result<PagedList<DeviceClientOverviewItemDto>>>
{
    public async Task<Result<PagedList<DeviceClientOverviewItemDto>>> Handle(
        GetDeviceClientOverviewQuery request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveSort(
                request.SortBy,
                request.SortDirection,
                out var sortField,
                out var descending,
                out var sortError))
        {
            return Result.Invalid(sortError!);
        }

        var scope = await currentUserDeviceAccessService.GetAccessibleDeviceIdsAsync(cancellationToken);
        if (!scope.IsSuccess)
        {
            return Result.Failure(scope.Errors?.ToArray() ?? ["用户凭证异常"]);
        }

        var allowedDeviceIds = scope.Value?.ToList();
        if (allowedDeviceIds is { Count: 0 })
        {
            return Result.Success(new PagedList<DeviceClientOverviewItemDto>(
                [],
                0,
                request.PaginationParams));
        }

        var utcNow = DateTime.UtcNow;
        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var page = await overviewQueryService.SearchAsync(
            new DeviceClientOverviewQueryRequest(
                allowedDeviceIds,
                request.Keyword,
                sortField,
                descending,
                utcNow.Subtract(DeviceClientSoftwareStatusResolver.RuntimeHeartbeatStaleThreshold),
                utcNow.Add(DeviceClientSoftwareStatusResolver.MaximumFutureClockSkew),
                skip,
                request.PaginationParams.PageSize),
            cancellationToken);
        var deviceIds = page.Devices.Select(device => device.DeviceId).ToList();
        var states = deviceIds.Count == 0
            ? []
            : await clientStateQueryService.GetStatesByDevicesAsync(deviceIds, cancellationToken);
        var statesByIdentity = states
            .GroupBy(state => (state.DeviceId, state.ClientCode))
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(state => state.UpdatedAtUtc).First());

        var items = page.Devices
            .Select(device => DeviceClientOverviewMapping.ToListItem(
                device,
                statesByIdentity.GetValueOrDefault((device.DeviceId, device.ClientCode)),
                utcNow))
            .ToList();

        return Result.Success(new PagedList<DeviceClientOverviewItemDto>(
            items,
            page.TotalCount,
            request.PaginationParams));
    }

    private static bool TryResolveSort(
        string? sortBy,
        string? sortDirection,
        out DeviceClientOverviewSortField sortField,
        out bool descending,
        out string? error)
    {
        var normalizedSort = sortBy?.Trim();
        sortField = normalizedSort?.ToLowerInvariant() switch
        {
            null or "" or "devicename" => DeviceClientOverviewSortField.DeviceName,
            "softwarestatus" => DeviceClientOverviewSortField.SoftwareStatus,
            "currentversion" => DeviceClientOverviewSortField.CurrentVersion,
            "lastruntimeheartbeatatutc" => DeviceClientOverviewSortField.LastRuntimeHeartbeatAtUtc,
            _ => (DeviceClientOverviewSortField)(-1)
        };
        if (!Enum.IsDefined(sortField))
        {
            descending = false;
            error = "sortBy 仅支持 deviceName、softwareStatus、currentVersion、lastRuntimeHeartbeatAtUtc。";
            return false;
        }

        var normalizedDirection = sortDirection?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedDirection)
            || string.Equals(normalizedDirection, "asc", StringComparison.OrdinalIgnoreCase))
        {
            descending = false;
            error = null;
            return true;
        }

        if (string.Equals(normalizedDirection, "desc", StringComparison.OrdinalIgnoreCase))
        {
            descending = true;
            error = null;
            return true;
        }

        descending = false;
        error = "sortDirection 仅支持 asc 或 desc。";
        return false;
    }
}

public sealed class GetDeviceClientReleaseDetailHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IReadRepository<Device> deviceRepository,
    IDeviceClientStateQueryService clientStateQueryService,
    IReadRepository<ClientReleaseComponent> componentRepository)
    : IQueryHandler<GetDeviceClientReleaseDetailQuery, Result<DeviceClientVersionInventoryDto>>
{
    public async Task<Result<DeviceClientVersionInventoryDto>> Handle(
        GetDeviceClientReleaseDetailQuery request,
        CancellationToken cancellationToken)
    {
        if (request.DeviceId == Guid.Empty)
        {
            return Result.Invalid("设备不能为空。");
        }

        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(request.DeviceId),
            cancellationToken);
        if (device is null)
        {
            return Result.NotFound("设备不存在。");
        }

        var access = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            device.Id,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.Failure(access.Errors?.ToArray() ?? ["越权: 未授权访问该设备"]);
        }

        var state = await clientStateQueryService.GetStateByIdentityAsync(
            device.Id,
            device.Code,
            cancellationToken);
        var snapshot = await clientStateQueryService.GetVersionSnapshotByDeviceAsync(
            device.Id,
            cancellationToken);
        var channel = ClientReleaseText.NormalizeChannel(request.Channel);
        var components = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(
                channel,
                request.TargetRuntime,
                onlyPublished: true),
            cancellationToken);
        var referenceCatalog =
            DeviceClientVersionInventoryMapping.BuildReferenceCatalog(components);
        if (snapshot is not null
            && !string.Equals(snapshot.ClientCode, device.Code, StringComparison.OrdinalIgnoreCase))
        {
            snapshot = null;
        }

        return Result.Success(DeviceClientVersionInventoryMapping.BuildInventory(
            device,
            snapshot,
            state,
            referenceCatalog,
            DateTime.UtcNow));
    }
}
