using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.Authorization;
using IIoT.Services.Common.Contracts.RecordQueries;
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
    ICurrentUser currentUser,
    IDevicePermissionService devicePermissionService,
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetSummaryRangeQuery, Result<List<DailyRangeSummaryDto>>>
{
    public async Task<Result<List<DailyRangeSummaryDto>>> Handle(
        GetSummaryRangeQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                currentUser.Role,
                IIoT.Services.Common.Contracts.Authorization.SystemRoles.Admin,
                StringComparison.Ordinal))
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
                userId,
                isAdmin: false,
                cancellationToken);
            if (accessibleDeviceIds is null || !accessibleDeviceIds.Contains(request.DeviceId))
                return Result.Failure("无权查看该设备的区间汇总");
        }

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
