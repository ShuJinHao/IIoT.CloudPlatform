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

        async Task<PagedList<DailyCapacityPagedItemDto>?> LoadPageAsync(
            CancellationToken factoryCancellationToken)
        {
            var (items, totalCount) = await queryService.GetDailyPagedAsync(
                request.PaginationParams,
                request.Date,
                request.DeviceId,
                request.DeviceId.HasValue ? null : allowedDeviceIds,
                factoryCancellationToken);

            return new PagedList<DailyCapacityPagedItemDto>(
                items,
                totalCount,
                request.PaginationParams);
        }

        var pagedList = canUseCache
            ? await cacheService.GetOrSetAsync(
                cacheKey,
                LoadPageAsync,
                static value => value is not null,
                TimeSpan.FromMinutes(5),
                cancellationToken)
            : await LoadPageAsync(cancellationToken);

        return Result.Success(pagedList
            ?? throw new InvalidOperationException("Capacity page cache factory returned null."));
    }
}
