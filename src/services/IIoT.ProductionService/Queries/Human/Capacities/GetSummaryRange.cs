using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

/// <summary>
/// 查询指定设备在日期范围内的每日汇总产能。
/// 传入 plcName 时只汇总对应 PLC 的数据。
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
    IReadRepository<Employee> employeeRepository,
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetSummaryRangeQuery, Result<List<DailyRangeSummaryDto>>>
{
    public async Task<Result<List<DailyRangeSummaryDto>>> Handle(
        GetSummaryRangeQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(userId),
                cancellationToken);

            if (employee == null)
                return Result.Failure("系统中未找到您的员工档案");

            if (!employee.DeviceAccesses.Any(d => d.DeviceId == request.DeviceId))
                return Result.Failure("无权查看该设备产能");
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
