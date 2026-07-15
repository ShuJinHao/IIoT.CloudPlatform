using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.RecordQueries;
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
        return Result.Success(await CapacityQueryCache.GetSummaryRangeAsync(
            queryService, cacheService, request.DeviceId, request.StartDate,
            request.EndDate, request.PlcName, cancellationToken));
    }
}
