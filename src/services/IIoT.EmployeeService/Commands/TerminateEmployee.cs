using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands;

/// <summary>
/// 业务层指令：员工离职与彻底销户 (硬删除 / 斩首行动)
/// </summary>
// 🌟 权限拦截：极其敏感的操作，需要专门的 Employee.Terminate 权限点
[AuthorizeRequirement("Employee.Terminate")]
public record TerminateEmployeeCommand(Guid EmployeeId) : ICommand<Result>;

public class TerminateEmployeeHandler(
    IIdentityService identityService // 🌟 奇迹就在这里：我们甚至不需要注入 Employee 的仓储！
) : ICommandHandler<TerminateEmployeeCommand, Result>
{
    public async Task<Result> Handle(TerminateEmployeeCommand request, CancellationToken cancellationToken)
    {
        // ==========================================
        // 🌟 终极主从级联销毁
        // ==========================================

        // 我们只需要向“保安科 (Identity)”下达销毁底层账号的指令。
        // PostgreSQL 数据库引擎接收到主键删除的动作后，
        // 会瞬间、原子化地把 `employees` 表里的业务档案，
        // 以及 `employee_process_accesses` 和 `employee_device_accesses` 里的权限数据全部清空。
        var result = await identityService.DeleteUserAsync(request.EmployeeId);

        if (!result.IsSuccess)
        {
            return Result.Failure(result.Errors?.ToArray() ?? ["员工账号销毁失败，可能不存在"]);
        }

        return Result.Success();
    }
}