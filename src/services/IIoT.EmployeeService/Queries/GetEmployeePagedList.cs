using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Queries;

// 列表展示 DTO：带双维管辖权计数，前端无需再发 N 个额外请求
public record EmployeeListItemDto(
    Guid Id,
    string EmployeeNo,
    string RealName,
    bool IsActive,
    int ProcessCount,  // 工序管辖数量
    int DeviceCount    // 机台管辖数量
);

[AuthorizeRequirement("Employee.Read")]
public record GetEmployeePagedListQuery(Pagination PaginationParams, string? Keyword = null) : IQuery<Result<PagedList<EmployeeListItemDto>>>;

public class GetEmployeePagedListHandler(
    IReadRepository<Employee> employeeRepository
) : IQueryHandler<GetEmployeePagedListQuery, Result<PagedList<EmployeeListItemDto>>>
{
    public async Task<Result<PagedList<EmployeeListItemDto>>> Handle(GetEmployeePagedListQuery request, CancellationToken cancellationToken)
    {
        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var take = request.PaginationParams.PageSize;

        // 统计总数：不启用分页
        var countSpec = new EmployeePagedSpec(0, 0, request.Keyword, isPaging: false);
        var totalCount = await employeeRepository.CountAsync(countSpec, cancellationToken);

        List<Employee> list = [];
        if (totalCount > 0)
        {
            // 🌟 使用 EmployeeWithAccessesSpec 变体：同时 Include 两个导航属性
            // 一次查询拿到员工 + 工序管辖 + 机台管辖，在内存里统计数量，彻底消灭 N+1
            var pagedSpec = new EmployeePagedWithAccessesSpec(skip, take, request.Keyword);
            list = await employeeRepository.GetListAsync(pagedSpec, cancellationToken);
        }

        var dtos = list.Select(e => new EmployeeListItemDto(
            e.Id,
            e.EmployeeNo,
            e.RealName,
            e.IsActive,
            e.ProcessAccesses.Count,
            e.DeviceAccesses.Count
        )).ToList();

        var pagedList = new PagedList<EmployeeListItemDto>(dtos, totalCount, request.PaginationParams);

        return Result.Success(pagedList);
    }
}
