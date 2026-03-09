using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Services.Common.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands;

/// <summary>
/// 业务层指令：员工软性离职/停用 (保留所有历史追溯数据)
/// </summary>
// 🌟 权限拦截：必须具备停用员工的权限
[AuthorizeRequirement("Employee.Deactivate")]
public record DeactivateEmployeeCommand(Guid EmployeeId) : ICommand<Result>;

public class DeactivateEmployeeHandler(
    IRepository<Employee> employeeRepository // 软离职只需要操作业务库
) : ICommandHandler<DeactivateEmployeeCommand, Result>
{
    public async Task<Result> Handle(DeactivateEmployeeCommand request, CancellationToken cancellationToken)
    {
        // 1. 查出员工的业务档案
        var employee = await employeeRepository.GetByIdAsync(request.EmployeeId, cancellationToken);

        if (employee == null)
        {
            return Result.Failure("未找到目标员工档案");
        }

        if (!employee.IsActive)
        {
            return Result.Success(); // 已经离职了，直接返回成功 (幂等性)
        }

        // ==========================================
        // 🌟 软离职核心动作：挂起业务状态
        // ==========================================
        employee.IsActive = false;

        // 2. 业务数据落库
        employeeRepository.Update(employee);
        await employeeRepository.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}