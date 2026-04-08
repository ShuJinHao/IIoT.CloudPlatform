using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

/// <summary>
/// 业务指令:员工软性离职/停用 (保留所有历史追溯数据)
/// </summary>
[AuthorizeRequirement("Employee.Deactivate")]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record DeactivateEmployeeCommand(Guid EmployeeId) : ICommand<Result>;

public class DeactivateEmployeeHandler(
    IRepository<Employee> employeeRepository
) : ICommandHandler<DeactivateEmployeeCommand, Result>
{
    public async Task<Result> Handle(
        DeactivateEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        var employee = await employeeRepository.GetSingleOrDefaultAsync(
            new EmployeeWithAccessesSpec(request.EmployeeId),
            cancellationToken);

        if (employee is null)
            return Result.Failure("未找到目标员工档案");

        // 已停用直接返回成功(幂等)
        if (!employee.IsActive)
            return Result.Success();

        employee.Deactivate();

        employeeRepository.Update(employee);
        await employeeRepository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}