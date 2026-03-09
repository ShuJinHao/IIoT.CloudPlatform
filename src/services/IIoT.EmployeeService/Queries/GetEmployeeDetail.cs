using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Queries;

// 1. 定义纯净的展示 DTO
public record EmployeeDetailDto(
    Guid Id,
    string EmployeeNo,
    string RealName,
    bool IsActive,
    List<Guid> ProcessIds,
    List<Guid> DeviceIds
);

// 2. 查询指令定义
[AuthorizeRequirement("Employee.Read")]
public record GetEmployeeDetailQuery(Guid EmployeeId) : IQuery<Result<EmployeeDetailDto>>;

// 3. 处理器实现
public class GetEmployeeDetailHandler(
    IReadRepository<Employee> employeeRepository // 🌟 纯粹的读操作，注入读仓储即可
) : IQueryHandler<GetEmployeeDetailQuery, Result<EmployeeDetailDto>>
{
    public async Task<Result<EmployeeDetailDto>> Handle(GetEmployeeDetailQuery request, CancellationToken cancellationToken)
    {
        // 🌟 完美复用查管辖权的规约图纸
        var spec = new EmployeeWithAccessesSpec(request.EmployeeId);
        var employee = await employeeRepository.GetSingleOrDefaultAsync(spec, cancellationToken);

        if (employee == null) return Result.Failure("未找到该员工档案");

        // 组装 DTO
        var dto = new EmployeeDetailDto(
            employee.Id,
            employee.EmployeeNo,
            employee.RealName,
            employee.IsActive,
            employee.ProcessAccesses.Select(p => p.ProcessId).ToList(),
            employee.DeviceAccesses.Select(d => d.DeviceId).ToList()
        );

        return Result.Success(dto);
    }
}