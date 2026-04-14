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
/// 查询指定设备在某天的小时产能明细。
/// 传入 plcName 时只返回对应 PLC 的数据。
/// </summary>
[AuthorizeRequirement("Device.Read")]
public record GetHourlyByDeviceIdQuery(
    Guid DeviceId,
    DateOnly Date,
    string? PlcName = null
) : IHumanQuery<Result<List<HourlyCapacityDto>>>;

public class GetHourlyByDeviceIdHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetHourlyByDeviceIdQuery, Result<List<HourlyCapacityDto>>>
{
    public async Task<Result<List<HourlyCapacityDto>>> Handle(
        GetHourlyByDeviceIdQuery request,
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
