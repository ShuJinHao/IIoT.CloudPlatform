using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

public record GetEdgeHourlyByDeviceIdQuery(
    Guid DeviceId,
    DateOnly Date,
    string? PlcName = null
) : IDeviceQuery<Result<List<HourlyCapacityDto>>>;

public class GetEdgeHourlyByDeviceIdHandler(
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetEdgeHourlyByDeviceIdQuery, Result<List<HourlyCapacityDto>>>
{
    public async Task<Result<List<HourlyCapacityDto>>> Handle(
        GetEdgeHourlyByDeviceIdQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeys.CapacityHourly(request.DeviceId, request.Date, request.PlcName);

        var cached = await cacheService.GetAsync<List<HourlyCapacityDto>>(cacheKey, cancellationToken);
        if (cached is not null)
            return Result.Success(cached);

        var data = await queryService.GetHourlyByDeviceIdAsync(
            request.DeviceId,
            request.Date,
            request.PlcName,
            cancellationToken);

        if (data.Count > 0)
            await cacheService.SetAsync(cacheKey, data, TimeSpan.FromMinutes(5), cancellationToken);

        return Result.Success(data);
    }
}
