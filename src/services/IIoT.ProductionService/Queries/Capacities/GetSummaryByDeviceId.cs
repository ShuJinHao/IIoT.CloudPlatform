using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.DapperQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

/// <summary>
/// 按日查询汇总（按 deviceId 查询，统一唯一标识）
/// </summary>
public record GetSummaryByDeviceIdQuery(
    Guid DeviceId,
    DateOnly Date
) : IQuery<Result<DailySummaryDto?>>;

public class GetSummaryByDeviceIdHandler(
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetSummaryByDeviceIdQuery, Result<DailySummaryDto?>>
{
    public async Task<Result<DailySummaryDto?>> Handle(
        GetSummaryByDeviceIdQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"iiot:capacity:summary:v1:{request.DeviceId}:{request.Date:yyyyMMdd}";

        var cached = await cacheService.GetAsync<DailySummaryDto>(cacheKey, cancellationToken);
        if (cached is not null)
            return Result.Success<DailySummaryDto?>(cached);

        var data = await queryService.GetSummaryByDeviceIdAsync(
            request.DeviceId,
            request.Date,
            cancellationToken);

        if (data is not null)
            await cacheService.SetAsync(cacheKey, data, TimeSpan.FromMinutes(5), cancellationToken);

        return Result.Success<DailySummaryDto?>(data);
    }
}