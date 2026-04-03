using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

/// <summary>
/// 按日期范围查询每日汇总（月/年查询使用）
/// 一次请求替代前端循环 N 次，彻底解决年查询 365 次请求问题
/// </summary>
public record GetSummaryRangeQuery(
    Guid DeviceId,
    DateOnly StartDate,
    DateOnly EndDate
) : IQuery<Result<List<DailyRangeSummaryDto>>>;

public class GetSummaryRangeHandler(
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetSummaryRangeQuery, Result<List<DailyRangeSummaryDto>>>
{
    public async Task<Result<List<DailyRangeSummaryDto>>> Handle(
        GetSummaryRangeQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"iiot:capacity:range:v1:{request.DeviceId}:{request.StartDate:yyyyMMdd}:{request.EndDate:yyyyMMdd}";

        var cached = await cacheService.GetAsync<List<DailyRangeSummaryDto>>(cacheKey, cancellationToken);
        if (cached is not null)
            return Result.Success(cached);

        var data = await queryService.GetSummaryRangeAsync(
            request.DeviceId,
            request.StartDate,
            request.EndDate,
            cancellationToken);

        if (data.Count > 0)
            await cacheService.SetAsync(cacheKey, data, TimeSpan.FromMinutes(5), cancellationToken);

        return Result.Success(data);                
    }
}