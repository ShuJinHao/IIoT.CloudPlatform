using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Contracts.EdgeHosts;
using IIoT.Core.Production.Specifications.EdgeHosts;
using IIoT.ProductionService.EdgeHosts;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
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
public sealed record GetEdgeHostDetailQuery(Guid EdgeHostId) : IHumanQuery<Result<EdgeHostDto>>;

[AuthorizeRequirement(EdgeHostPermissions.Read)]
public sealed record GetEdgeHostPlcRuntimeStatesQuery(Guid EdgeHostId)
    : IHumanQuery<Result<IReadOnlyList<EdgeHostPlcRuntimeStateDto>>>;

[AuthorizeRequirement(EdgeHostPermissions.Read)]
[AuthorizeRequirement(DevicePermissions.Read)]
public sealed record GetEdgeHostPlcCapacitySummaryQuery(
    Guid EdgeHostId,
    DateOnly Date) : IHumanQuery<Result<IReadOnlyList<EdgeHostPlcCapacitySummaryDto>>>;

public sealed class GetEdgeHostPagedListHandler(
    IReadRepository<EdgeHost> edgeHostRepository)
    : IQueryHandler<GetEdgeHostPagedListQuery, Result<PagedList<EdgeHostListItemDto>>>
{
    public async Task<Result<PagedList<EdgeHostListItemDto>>> Handle(
        GetEdgeHostPagedListQuery request,
        CancellationToken cancellationToken)
    {
        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var take = request.PaginationParams.PageSize;

        var totalCount = await edgeHostRepository.CountAsync(
            new EdgeHostPagedSpec(0, 0, request.Keyword, isPaging: false),
            cancellationToken);

        List<EdgeHost> list = [];
        if (totalCount > 0)
        {
            list = await edgeHostRepository.GetListAsync(
                new EdgeHostPagedSpec(skip, take, request.Keyword, isPaging: true),
                cancellationToken);
        }

        var items = list.Select(EdgeHostMapping.ToListItemDto).ToList();
        return Result.Success(new PagedList<EdgeHostListItemDto>(items, totalCount, request.PaginationParams));
    }
}

public sealed class GetEdgeHostDetailHandler(
    IReadRepository<EdgeHost> edgeHostRepository)
    : IQueryHandler<GetEdgeHostDetailQuery, Result<EdgeHostDto>>
{
    public async Task<Result<EdgeHostDto>> Handle(
        GetEdgeHostDetailQuery request,
        CancellationToken cancellationToken)
    {
        var host = await edgeHostRepository.GetSingleOrDefaultAsync(
            new EdgeHostByIdSpec(request.EdgeHostId),
            cancellationToken);

        return host is null
            ? Result.NotFound("上位机不存在。")
            : Result.Success(EdgeHostMapping.ToDto(host));
    }
}

public sealed class GetEdgeHostPlcCapacitySummaryHandler(
    IReadRepository<EdgeHost> edgeHostRepository,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    ICapacityQueryService capacityQueryService)
    : IQueryHandler<GetEdgeHostPlcCapacitySummaryQuery, Result<IReadOnlyList<EdgeHostPlcCapacitySummaryDto>>>
{
    private const string StatusReady = "Ready";
    private const string StatusNoBusinessDevice = "NoBusinessDevice";
    private const string StatusBindingDisabled = "BindingDisabled";
    private const string StatusNoDeviceAccess = "NoDeviceAccess";
    private const string StatusNoCapacityData = "NoCapacityData";

    public async Task<Result<IReadOnlyList<EdgeHostPlcCapacitySummaryDto>>> Handle(
        GetEdgeHostPlcCapacitySummaryQuery request,
        CancellationToken cancellationToken)
    {
        var host = await edgeHostRepository.GetSingleOrDefaultAsync(
            new EdgeHostByIdSpec(request.EdgeHostId),
            cancellationToken);
        if (host is null)
        {
            return Result.NotFound("上位机不存在。");
        }

        var scope = await currentUserDeviceAccessService.GetAccessibleDeviceIdsAsync(cancellationToken);
        if (!scope.IsSuccess)
        {
            return Result.Failure(scope.Errors?.ToArray() ?? ["用户凭证异常"]);
        }

        var items = new List<EdgeHostPlcCapacitySummaryDto>();
        foreach (var binding in host.PlcBindings
                     .OrderBy(binding => binding.DisplayOrder)
                     .ThenBy(binding => binding.PlcCode, StringComparer.OrdinalIgnoreCase))
        {
            items.Add(await BuildCapacitySummaryAsync(binding, request.Date, scope.Value, cancellationToken));
        }

        return Result.Success((IReadOnlyList<EdgeHostPlcCapacitySummaryDto>)items);
    }

    private async Task<EdgeHostPlcCapacitySummaryDto> BuildCapacitySummaryAsync(
        EdgeHostPlcBinding binding,
        DateOnly date,
        IReadOnlyList<Guid>? allowedDeviceIds,
        CancellationToken cancellationToken)
    {
        if (!binding.Enabled)
        {
            return ToDto(binding, date, canReadCapacity: false, StatusBindingDisabled, summary: null);
        }

        if (!binding.BusinessDeviceId.HasValue)
        {
            return ToDto(binding, date, canReadCapacity: false, StatusNoBusinessDevice, summary: null);
        }

        if (allowedDeviceIds is not null && !allowedDeviceIds.Contains(binding.BusinessDeviceId.Value))
        {
            return ToDto(binding, date, canReadCapacity: false, StatusNoDeviceAccess, summary: null);
        }

        var summary = await capacityQueryService.GetSummaryByDeviceIdAsync(
            binding.BusinessDeviceId.Value,
            date,
            binding.PlcName,
            cancellationToken);

        return ToDto(
            binding,
            date,
            canReadCapacity: true,
            summary is null ? StatusNoCapacityData : StatusReady,
            summary);
    }

    private static EdgeHostPlcCapacitySummaryDto ToDto(
        EdgeHostPlcBinding binding,
        DateOnly date,
        bool canReadCapacity,
        string capacityStatus,
        DailySummaryDto? summary)
    {
        return new EdgeHostPlcCapacitySummaryDto(
            binding.Id,
            binding.PlcCode,
            binding.PlcName,
            binding.Enabled,
            binding.ProcessId,
            binding.BusinessDeviceId,
            date,
            canReadCapacity,
            capacityStatus,
            summary);
    }
}

public sealed class GetEdgeHostPlcRuntimeStatesHandler(
    IReadRepository<EdgeHost> edgeHostRepository,
    IEdgeHostPlcRuntimeStateStore runtimeStateStore)
    : IQueryHandler<GetEdgeHostPlcRuntimeStatesQuery, Result<IReadOnlyList<EdgeHostPlcRuntimeStateDto>>>
{
    public async Task<Result<IReadOnlyList<EdgeHostPlcRuntimeStateDto>>> Handle(
        GetEdgeHostPlcRuntimeStatesQuery request,
        CancellationToken cancellationToken)
    {
        var host = await edgeHostRepository.GetSingleOrDefaultAsync(
            new EdgeHostByIdSpec(request.EdgeHostId),
            cancellationToken);
        if (host is null)
        {
            return Result.NotFound("上位机不存在。");
        }

        var bindingsById = host.PlcBindings.ToDictionary(binding => binding.Id);
        var bindingsByPlcCode = host.PlcBindings.ToDictionary(
            binding => binding.PlcCode,
            StringComparer.OrdinalIgnoreCase);

        var states = await runtimeStateStore.GetByEdgeHostAsync(host.Id, cancellationToken);
        var items = states
            .Select(state =>
            {
                EdgeHostPlcBinding? binding = null;
                if (state.PlcBindingId.HasValue)
                {
                    bindingsById.TryGetValue(state.PlcBindingId.Value, out binding);
                }

                binding ??= bindingsByPlcCode.GetValueOrDefault(state.PlcCode);
                return EdgeHostMapping.ToRuntimeStateDto(state, binding);
            })
            .OrderByDescending(item => item.LastSeenAtUtc)
            .ThenBy(item => item.PlcCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result.Success((IReadOnlyList<EdgeHostPlcRuntimeStateDto>)items);
    }
}
