using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Queries.Employees;

/// <summary>
/// 列表展示 DTO:带机台管辖权计数,前端无需再发 N 个额外请求
/// </summary>
public record EmployeeListItemDto(
    Guid Id,
    string EmployeeNo,
    string RealName,
    bool IsActive,
    int DeviceCount
);

[AuthorizeRequirement("Employee.Read")]
public record GetEmployeePagedListQuery(
    Pagination PaginationParams,
    string? Keyword = null
) : IHumanQuery<Result<PagedList<EmployeeListItemDto>>>;

public class GetEmployeePagedListHandler(
    IReadRepository<Employee> employeeRepository
) : IQueryHandler<GetEmployeePagedListQuery, Result<PagedList<EmployeeListItemDto>>>
{
    public async Task<Result<PagedList<EmployeeListItemDto>>> Handle(
        GetEmployeePagedListQuery request,
        CancellationToken cancellationToken)
    {
        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var take = request.PaginationParams.PageSize;

        // 先统计总数(不启用分页)
        var countSpec = new EmployeePagedSpec(0, 0, request.Keyword, isPaging: false);
        var totalCount = await employeeRepository.CountAsync(countSpec, cancellationToken);

        // 再按需加载分页数据,Include 机台管辖权,在内存里统计避免 N+1
        List<Employee> list = [];
        if (totalCount > 0)
        {
            var pagedSpec = new EmployeePagedWithAccessesSpec(skip, take, request.Keyword);
            list = await employeeRepository.GetListAsync(pagedSpec, cancellationToken);
        }

        var dtos = list.Select(e => new EmployeeListItemDto(
            e.Id,
            e.EmployeeNo,
            e.RealName,
            e.IsActive,
            e.DeviceAccesses.Count
        )).ToList();

        var pagedList = new PagedList<EmployeeListItemDto>(dtos, totalCount, request.PaginationParams);

        return Result.Success(pagedList);
    }
}

