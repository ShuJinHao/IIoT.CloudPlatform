using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Queries;

// 1. 定义一个专门返回给前端的纯净 DTO
public record EmployeeAccessDto(
    List<Guid> ProcessIds,
    List<Guid> DeviceIds
);

// 2. 查询指令定义
[AuthorizeRequirement("Employee.Read")]
public record GetEmployeeAccessQuery(Guid EmployeeId) : IQuery<Result<EmployeeAccessDto>>;

// 3. 处理器实现
public class GetEmployeeAccessHandler(
    IReadRepository<Employee> employeeRepository // 🌟 查询只注入读仓储即可
) : IQueryHandler<GetEmployeeAccessQuery, Result<EmployeeAccessDto>>
{
    public async Task<Result<EmployeeAccessDto>> Handle(GetEmployeeAccessQuery request, CancellationToken cancellationToken)
    {
        // 🌟 1. 直接复用我们写好的规约图纸！不需要再写 Include 了
        var spec = new EmployeeWithAccessesSpec(request.EmployeeId);
        var employee = await employeeRepository.GetSingleOrDefaultAsync(spec, cancellationToken);

        if (employee == null) return Result.Failure("未找到该员工档案");

        // 2. 组装前端需要的纯净 ID 列表
        var dto = new EmployeeAccessDto(
            employee.ProcessAccesses.Select(p => p.ProcessId).ToList(),
            employee.DeviceAccesses.Select(d => d.DeviceId).ToList()
        );

        return Result.Success(dto);
    }
}