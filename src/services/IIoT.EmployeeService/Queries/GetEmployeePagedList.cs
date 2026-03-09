using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Queries;

// 1. 列表展示的纯净 DTO
public record EmployeeListItemDto(
    Guid Id,
    string EmployeeNo,
    string RealName,
    bool IsActive
);

// 🌟 2. 查询指令：直接接收你的 Pagination 参数对象
[AuthorizeRequirement("Employee.Read")]
public record GetEmployeePagedListQuery(Pagination PaginationParams, string? Keyword = null) : IQuery<Result<PagedList<EmployeeListItemDto>>>;

// 3. 处理器实现
public class GetEmployeePagedListHandler(
    IReadRepository<Employee> employeeRepository
) : IQueryHandler<GetEmployeePagedListQuery, Result<PagedList<EmployeeListItemDto>>>
{
    public async Task<Result<PagedList<EmployeeListItemDto>>> Handle(GetEmployeePagedListQuery request, CancellationToken cancellationToken)
    {
        // 1. 利用你的 Pagination 算出 Skip
        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var take = request.PaginationParams.PageSize;

        // 2. 组装规约图纸
        var pagedSpec = new EmployeePagedSpec(skip, take, request.Keyword);
        var countSpec = new EmployeePagedSpec(0, int.MaxValue, request.Keyword);

        // 3. 并发查数据与总数 (极致性能)
        var countTask = employeeRepository.CountAsync(countSpec, cancellationToken);
        var listTask = employeeRepository.GetListAsync(pagedSpec, cancellationToken);

        await Task.WhenAll(countTask, listTask);

        // 4. 转换 DTO
        var dtos = listTask.Result.Select(e => new EmployeeListItemDto(
            e.Id,
            e.EmployeeNo,
            e.RealName,
            e.IsActive
        )).ToList();

        // 🌟 5. 直接用你的 PagedList 包装并返回！
        var pagedList = new PagedList<EmployeeListItemDto>(dtos, countTask.Result, request.PaginationParams);

        return Result.Success(pagedList);
    }
}