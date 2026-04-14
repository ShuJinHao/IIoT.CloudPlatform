using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.DeviceLogs;

[AuthorizeRequirement("Device.Read")]
public record GetDeviceLogsQuery(
    Pagination PaginationParams,
    Guid DeviceId,
    string? Level = null,
    string? Keyword = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null
) : IHumanQuery<Result<PagedList<DeviceLogListItemDto>>>;

public class GetDeviceLogsHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IDeviceLogQueryService queryService)
    : IQueryHandler<GetDeviceLogsQuery, Result<PagedList<DeviceLogListItemDto>>>
{
    public async Task<Result<PagedList<DeviceLogListItemDto>>> Handle(
        GetDeviceLogsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.DeviceId == Guid.Empty)
            return Result.Failure("DeviceId 不能为空");

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
                return Result.Failure("无权查看该设备日志");
        }

        var (items, totalCount) = await queryService.GetLogsByConditionAsync(
            request.PaginationParams,
            request.DeviceId,
            request.Level,
            request.Keyword,
            request.StartTime,
            request.EndTime,
            cancellationToken);

        var pagedList = new PagedList<DeviceLogListItemDto>(
            items, totalCount, request.PaginationParams);

        return Result.Success(pagedList);
    }
}
