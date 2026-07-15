using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

/// <summary>
/// 查询指定设备在日期区间内的日报汇总
/// </summary>
[AuthorizeRequirement("Device.Read")]
public record GetSummaryRangeQuery(
    Guid DeviceId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? PlcName = null
) : IHumanQuery<Result<List<DailyRangeSummaryDto>>>;

public class GetSummaryRangeHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetSummaryRangeQuery, Result<List<DailyRangeSummaryDto>>>
{
    public async Task<Result<List<DailyRangeSummaryDto>>> Handle(
        GetSummaryRangeQuery request,
        CancellationToken cancellationToken)
    {
        var deviceAccess = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            request.DeviceId,
            cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return Result.Failure(deviceAccess.Errors?.ToArray() ?? ["无权查看该设备的区间汇总"]);
        }

        return Result.Success(await CapacityQueryCache.GetSummaryRangeAsync(
            queryService, cacheService, request.DeviceId, request.StartDate,
            request.EndDate, request.PlcName, cancellationToken));
    }
}
