using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

/// <summary>
/// 查询：单机台产能汇总（最近 N 天，带缓存）
/// </summary>
public record GetDeviceCapacitySummaryQuery(
    Guid DeviceId,
    DateOnly StartDate,
    DateOnly EndDate
) : IQuery<Result<object>>;

public class GetDeviceCapacitySummaryHandler(
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetDeviceCapacitySummaryQuery, Result<object>>
{
    public async Task<Result<object>> Handle(GetDeviceCapacitySummaryQuery request, CancellationToken cancellationToken)
    {
        // 判断是否为默认最近7天查询，是则走缓存
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var isDefaultRange = request.StartDate == today.AddDays(-6) && request.EndDate == today;

        if (isDefaultRange)
        {
            var cacheKey = $"iiot:capacity:summary:v1:{request.DeviceId}";
            var cached = await cacheService.GetAsync<List<dynamic>>(cacheKey, cancellationToken);

            if (cached != null)
                return Result.Success<object>(cached);

            var data = await queryService.GetDeviceSummaryAsync(
                request.DeviceId, request.StartDate, request.EndDate, cancellationToken);

            await cacheService.SetAsync(cacheKey, data, TimeSpan.FromMinutes(5), cancellationToken);

            return Result.Success<object>(data);
        }

        // 非默认范围，直接查库不走缓存
        var result = await queryService.GetDeviceSummaryAsync(
            request.DeviceId, request.StartDate, request.EndDate, cancellationToken);

        return Result.Success<object>(result);
    }
}