using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

public record DeviceListItemDto(
    Guid Id,
    string DeviceName,
    Guid ProcessId
);

[AuthorizeRequirement("Device.Read")]
public record GetMyDevicesPagedQuery(Pagination PaginationParams, string? Keyword = null) : IHumanQuery<Result<PagedList<DeviceListItemDto>>>;

public class GetMyDevicesPagedHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IReadRepository<Device> deviceRepository
) : IQueryHandler<GetMyDevicesPagedQuery, Result<PagedList<DeviceListItemDto>>>
{
    public async Task<Result<PagedList<DeviceListItemDto>>> Handle(GetMyDevicesPagedQuery request, CancellationToken cancellationToken)
    {
        List<Guid>? allowedDeviceIds = null;

        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId)) return Result.Failure("用户凭证异常");

            var employeeSpec = new EmployeeWithAccessesSpec(userId);
            var employee = await employeeRepository.GetSingleOrDefaultAsync(employeeSpec, cancellationToken);

            if (employee == null) return Result.Failure("系统中未找到您的员工档案");

            allowedDeviceIds = employee.DeviceAccesses.Select(d => d.DeviceId).ToList();

            if (allowedDeviceIds.Count == 0)
            {
                var emptyList = new PagedList<DeviceListItemDto>([], 0, request.PaginationParams);
                return Result.Success(emptyList);
            }
        }

        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var take = request.PaginationParams.PageSize;

        var countSpec = new DevicePagedSpec(0, 0, allowedDeviceIds, request.Keyword, isPaging: false);
        var totalCount = await deviceRepository.CountAsync(countSpec, cancellationToken);

        List<Device> list = [];
        if (totalCount > 0)
        {
            var pagedSpec = new DevicePagedSpec(skip, take, allowedDeviceIds, request.Keyword, isPaging: true);
            list = await deviceRepository.GetListAsync(pagedSpec, cancellationToken);
        }

        var dtos = list.Select(d => new DeviceListItemDto(
            d.Id,
            d.DeviceName,
            d.ProcessId
        )).ToList();

        var pagedList = new PagedList<DeviceListItemDto>(dtos, totalCount, request.PaginationParams);

        return Result.Success(pagedList);
    }
}
