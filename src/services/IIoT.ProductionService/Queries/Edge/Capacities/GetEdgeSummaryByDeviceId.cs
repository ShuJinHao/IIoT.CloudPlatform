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
        var cacheKey = CacheKeys.CapacitySummary(request.DeviceId, request.Date, request.PlcName);

        var cached = await cacheService.GetAsync<DailySummaryDto>(cacheKey, cancellationToken);
        if (cached is not null)
            return Result.Success<DailySummaryDto?>(cached);

        var data = await queryService.GetSummaryByDeviceIdAsync(
            request.DeviceId,
            request.Date,
            request.PlcName,
            cancellationToken);

        if (data is not null)
            await cacheService.SetAsync(cacheKey, data, TimeSpan.FromMinutes(5), cancellationToken);

        return Result.Success<DailySummaryDto?>(data);
    }
}
