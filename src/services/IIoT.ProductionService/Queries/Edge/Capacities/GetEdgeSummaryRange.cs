using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

public record GetEdgeSummaryRangeQuery(
    Guid DeviceId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? PlcName = null
) : IDeviceQuery<Result<List<DailyRangeSummaryDto>>>;

public class GetEdgeSummaryRangeHandler(
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetEdgeSummaryRangeQuery, Result<List<DailyRangeSummaryDto>>>
{
    public async Task<Result<List<DailyRangeSummaryDto>>> Handle(
        GetEdgeSummaryRangeQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeys.CapacityRange(
            request.DeviceId,
            request.StartDate,
            request.EndDate,
            request.PlcName);

        var cached = await cacheService.GetAsync<List<DailyRangeSummaryDto>>(cacheKey, cancellationToken);
        if (cached is not null)
            return Result.Success(cached);

        var data = await queryService.GetSummaryRangeAsync(
            request.DeviceId,
            request.StartDate,
            request.EndDate,
            request.PlcName,
            cancellationToken);

        if (data.Count > 0)
            await cacheService.SetAsync(cacheKey, data, TimeSpan.FromMinutes(5), cancellationToken);

        return Result.Success(data);
    }
}
