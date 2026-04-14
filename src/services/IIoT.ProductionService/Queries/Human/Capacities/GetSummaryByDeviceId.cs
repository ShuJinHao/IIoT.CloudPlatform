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
/// 查询指定设备在某天的汇总产能。
/// 传入 plcName 时只汇总对应 PLC 的数据。
/// </summary>
[AuthorizeRequirement("Device.Read")]
public record GetSummaryByDeviceIdQuery(
    Guid DeviceId,
    DateOnly Date,
    string? PlcName = null
) : IHumanQuery<Result<DailySummaryDto?>>;

public class GetSummaryByDeviceIdHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetSummaryByDeviceIdQuery, Result<DailySummaryDto?>>
{
    public async Task<Result<DailySummaryDto?>> Handle(
        GetSummaryByDeviceIdQuery request,
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
