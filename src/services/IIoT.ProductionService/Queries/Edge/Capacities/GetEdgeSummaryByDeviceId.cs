using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

public record GetEdgeSummaryByDeviceIdQuery(
    Guid DeviceId,
    DateOnly Date,
    string? PlcName = null
) : IDeviceQuery<Result<DailySummaryDto?>>;

public class GetEdgeSummaryByDeviceIdHandler(
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetEdgeSummaryByDeviceIdQuery, Result<DailySummaryDto?>>
{
    public async Task<Result<DailySummaryDto?>> Handle(
        GetEdgeSummaryByDeviceIdQuery request,
        CancellationToken cancellationToken)
    {
        return Result.Success<DailySummaryDto?>(await CapacityQueryCache.GetSummaryAsync(
            queryService, cacheService, request.DeviceId, request.Date,
            request.PlcName, cancellationToken));
    }
}
