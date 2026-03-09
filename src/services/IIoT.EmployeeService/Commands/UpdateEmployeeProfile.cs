using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Services.Common.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands;

/// <summary>
/// 业务层指令：维护员工基础人事档案
/// </summary>
// 🌟 权限拦截：执行此操作必须具备 Employee.Update 权限点
[AuthorizeRequirement("Employee.Update")]
public record UpdateEmployeeProfileCommand(
    Guid EmployeeId,  // 目标员工的灵魂契约 ID
    string RealName,  // 修改后的真实姓名
    bool IsActive     // 修改后的状态 (比如休假、停职可以设为 false)
) : ICommand<Result<bool>>;

public class UpdateEmployeeProfileHandler(
    IRepository<Employee> employeeRepository // 🌟 注意：这里只需要人事科仓储，不需要保安科了！
) : ICommandHandler<UpdateEmployeeProfileCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateEmployeeProfileCommand request, CancellationToken cancellationToken)
    {
        // 1. 从业务库捞出这个人的实体
        var employee = await employeeRepository.GetByIdAsync(request.EmployeeId, cancellationToken);

        if (employee == null)
            return Result.Failure("未找到该员工的业务档案");

        // 2. 更新业务领域字段
        // 💡 架构师进阶建议：如果在严格的 DDD 模式下，这里最好是在 Employee 实体里写一个 employee.UpdateProfile(name, isActive) 方法来封装，而不是直接改属性。
        // 但目前你的属性 setter 是 public 的，直接赋值也完全没问题。
        employee.RealName = request.RealName;
        employee.IsActive = request.IsActive;

        // 3. 业务数据落库
        employeeRepository.Update(employee);
        await employeeRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}