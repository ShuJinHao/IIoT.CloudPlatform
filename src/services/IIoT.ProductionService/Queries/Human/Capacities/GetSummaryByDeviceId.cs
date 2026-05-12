using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

/// <summary>
/// 查询指定设备每日汇总
/// </summary>
[AuthorizeRequirement("Device.Read")]
public record GetSummaryByDeviceIdQuery(
    Guid DeviceId,
    DateOnly Date,
    string? PlcName = null
) : IHumanQuery<Result<DailySummaryDto?>>;

public class GetSummaryByDeviceIdHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetSummaryByDeviceIdQuery, Result<DailySummaryDto?>>
{
    public async Task<Result<DailySummaryDto?>> Handle(
        GetSummaryByDeviceIdQuery request,
        CancellationToken cancellationToken)
    {
        var deviceAccess = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            request.DeviceId,
            cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return Result.Failure(deviceAccess.Errors?.ToArray() ?? ["无权查看该设备的汇总报表"]);
        }

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
