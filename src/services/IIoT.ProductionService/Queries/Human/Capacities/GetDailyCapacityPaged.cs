using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

/// <summary>
/// 设备日报能力汇总列表
/// </summary>
[AuthorizeRequirement("Device.Read")]
public record GetDailyCapacityPagedQuery(
    Pagination PaginationParams,
    DateOnly? Date = null,
    Guid? DeviceId = null
) : IHumanQuery<Result<PagedList<DailyCapacityPagedItemDto>>>;

public class GetDailyCapacityPagedHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetDailyCapacityPagedQuery, Result<PagedList<DailyCapacityPagedItemDto>>>
{
    public async Task<Result<PagedList<DailyCapacityPagedItemDto>>> Handle(
        GetDailyCapacityPagedQuery request,
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
            return Result.Success(new PagedList<DailyCapacityPagedItemDto>([], 0, request.PaginationParams));
        }

        if (request.DeviceId.HasValue
            && allowedDeviceIds is not null
            && !allowedDeviceIds.Contains(request.DeviceId.Value))
        {
            return Result.Failure("无权查看该设备的日报报表");
        }

        var cacheKey = CacheKeys.CapacityPaged(
            request.Date,
            request.DeviceId,
            request.PaginationParams.PageNumber,
            request.PaginationParams.PageSize);

        var canUseCache = currentUserDeviceAccessService.IsAdministrator || request.DeviceId.HasValue;

        if (canUseCache)
        {
            var cached = await cacheService.GetAsync<PagedList<DailyCapacityPagedItemDto>>(
                cacheKey, cancellationToken);
            if (cached is not null)
                return Result.Success(cached);
        }

        var (items, totalCount) = await queryService.GetDailyPagedAsync(
            request.PaginationParams,
            request.Date,
            request.DeviceId,
            request.DeviceId.HasValue ? null : allowedDeviceIds,
            cancellationToken);

        var pagedList = new PagedList<DailyCapacityPagedItemDto>(
            items, totalCount, request.PaginationParams);

        if (canUseCache)
        {
            await cacheService.SetAsync(
                cacheKey, pagedList, TimeSpan.FromMinutes(5), cancellationToken);
        }

        return Result.Success(pagedList);
    }
}
