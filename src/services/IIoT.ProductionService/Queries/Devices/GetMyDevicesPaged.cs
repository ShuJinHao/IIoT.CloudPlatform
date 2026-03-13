using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

/// <summary>
/// 纯净的列表展示 DTO
/// </summary>
public record DeviceListItemDto(
    Guid Id,
    string DeviceName,
    string DeviceCode,
    Guid ProcessId,
    bool IsActive
);

/// <summary>
/// 交互查询：获取"我管辖范围内"的设备分页列表
/// </summary>
[AuthorizeRequirement("Device.Read")]
public record GetMyDevicesPagedQuery(Pagination PaginationParams, string? Keyword = null) : IQuery<Result<PagedList<DeviceListItemDto>>>;

public class GetMyDevicesPagedHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IReadRepository<Device> deviceRepository
) : IQueryHandler<GetMyDevicesPagedQuery, Result<PagedList<DeviceListItemDto>>>
{
    public async Task<Result<PagedList<DeviceListItemDto>>> Handle(GetMyDevicesPagedQuery request, CancellationToken cancellationToken)
    {
        List<Guid>? allowedProcessIds = null;

        // ==========================================
        // 第二道门：动态计算数据管辖权 (ABAC)
        // ==========================================
        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId)) return Result.Failure("用户凭证异常");

            // 复用员工模块规约，拉取员工的工序管辖权（含导航属性）
            var employeeSpec = new EmployeeWithAccessesSpec(userId);
            var employee = await employeeRepository.GetSingleOrDefaultAsync(employeeSpec, cancellationToken);

            if (employee == null) return Result.Failure("系统中未找到您的员工档案");

            allowedProcessIds = employee.ProcessAccesses.Select(p => p.ProcessId).ToList();

            // 非 Admin 且无任何工序管辖权，直接返回空列表，不查数据库
            if (allowedProcessIds.Count == 0)
            {
                var emptyList = new PagedList<DeviceListItemDto>([], 0, request.PaginationParams);
                return Result.Success(emptyList);
            }
        }

        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var take = request.PaginationParams.PageSize;

        // 统计总数：不启用分页
        var countSpec = new DevicePagedSpec(0, 0, allowedProcessIds, request.Keyword, isPaging: false);
        var totalCount = await deviceRepository.CountAsync(countSpec, cancellationToken);

        // 先拿 count，再按需查数据，单个 DbContext 串行执行，绝不并发
        List<Device> list = [];
        if (totalCount > 0)
        {
            var pagedSpec = new DevicePagedSpec(skip, take, allowedProcessIds, request.Keyword, isPaging: true);
            list = await deviceRepository.GetListAsync(pagedSpec, cancellationToken);
        }

        var dtos = list.Select(d => new DeviceListItemDto(
            d.Id,
            d.DeviceName,
            d.DeviceCode,
            d.ProcessId,
            d.IsActive
        )).ToList();

        var pagedList = new PagedList<DeviceListItemDto>(dtos, totalCount, request.PaginationParams);

        return Result.Success(pagedList);
    }
}
