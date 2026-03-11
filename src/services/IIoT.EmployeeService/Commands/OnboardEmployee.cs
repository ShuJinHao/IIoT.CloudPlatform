using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IIoT.EmployeeService.Commands;

[AuthorizeRequirement("Employee.Onboard")]
public record OnboardEmployeeCommand(
    string EmployeeNo,
    string RealName,
    string Password,
    string? RoleName = null,
    List<Guid>? ProcessIds = null,
    List<Guid>? DeviceIds = null
) : ICommand<Result<Guid>>;

public class OnboardEmployeeHandler(
    IIdentityService identityService,
    IRepository<Employee> employeeRepository
) : ICommandHandler<OnboardEmployeeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(OnboardEmployeeCommand request, CancellationToken cancellationToken)
    {
        // 防线：坚决禁止通过员工入职接口分配最高管理员权限
        if (!string.IsNullOrWhiteSpace(request.RoleName) && request.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("系统保护：禁止通过员工入职接口分配最高管理员权限！");
        }

        var sharedId = Guid.NewGuid();

        // ==========================================
        // 第一步：在【身份中心】办理通行证 (底层瞬间落盘)
        // ==========================================

        var identityResult = await identityService.CreateUserAsync(sharedId, request.EmployeeNo, request.Password);
        if (!identityResult.IsSuccess)
            return Result.Failure(identityResult.Errors?.ToArray() ?? ["身份账号创建失败"]);

        // 🌟 防线 1：角色绑定如果失败，立刻触发补偿机制删除刚刚创建的空账号
        if (!string.IsNullOrWhiteSpace(request.RoleName))
        {
            var roleResult = await identityService.AssignRoleToUserAsync(request.EmployeeNo, request.RoleName);
            if (!roleResult.IsSuccess)
            {
                // 🚨 补偿动作：角色绑不上，号我也不要了！
                await identityService.DeleteUserAsync(sharedId);
                return Result.Failure(roleResult.Errors?.ToArray() ?? ["系统角色绑定失败，已撤销账号创建"]);
            }
        }

        // ==========================================
        // 第二步：在【业务中心】建立档案
        // ==========================================

        var employee = new Employee(sharedId, request.EmployeeNo, request.RealName);

        if (request.ProcessIds != null && request.ProcessIds.Any())
        {
            foreach (var pId in request.ProcessIds) employee.AddProcessAccess(pId);
        }

        if (request.DeviceIds != null && request.DeviceIds.Any())
        {
            foreach (var dId in request.DeviceIds) employee.AddDeviceAccess(dId);
        }

        employeeRepository.Add(employee);

        // ==========================================
        // 🌟 第三步：终极补偿事务防线 (Saga Pattern)
        // ==========================================
        try
        {
            // 尝试把业务数据落库
            await employeeRepository.SaveChangesAsync(cancellationToken);

            // 一切顺利，完美返回
            return Result.Success(sharedId);
        }
        catch (Exception ex)
        {
            // 🚨 灾难发生：比如字段超长、外键约束失败、或者数据库网络瞬间断开
            // 此时内存里的 Employee 实体没有存进数据库，但 Identity 表里已经多了一个“幽灵账号”

            // 🔪 斩首行动：调用 Identity 提供的销毁接口，把刚才第一步创建的账号硬删除！
            await identityService.DeleteUserAsync(sharedId);

            // 返回明确的错误信息给前端，告知数据未污染
            return Result.Failure($"员工业务档案落库失败，已触发补偿机制撤销底层身份账号，保持系统数据干净。底层死因: {ex.Message}");
        }
    }
}