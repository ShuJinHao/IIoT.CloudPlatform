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

namespace IIoT.EmployeeService.Commands.Employees;

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
    IAccountService accountService,
    IRepository<Employee> employeeRepository
) : ICommandHandler<OnboardEmployeeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(OnboardEmployeeCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RoleName) && request.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("系统保护：禁止通过员工入职接口分配最高管理员权限！");
        }

        var sharedId = Guid.NewGuid();

        var identityResult = await accountService.CreateUserAsync(sharedId, request.EmployeeNo, request.Password);
        if (!identityResult.IsSuccess)
            return Result.Failure(identityResult.Errors?.ToArray() ?? ["身份账号创建失败"]);

        if (!string.IsNullOrWhiteSpace(request.RoleName))
        {
            var roleResult = await accountService.AssignRoleToUserAsync(request.EmployeeNo, request.RoleName);
            if (!roleResult.IsSuccess)
            {
                await accountService.DeleteUserAsync(sharedId);
                return Result.Failure(roleResult.Errors?.ToArray() ?? ["系统角色绑定失败，已撤销账号创建"]);
            }
        }

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

        try
        {
            await employeeRepository.SaveChangesAsync(cancellationToken);
            return Result.Success(sharedId);
        }
        catch (Exception ex)
        {
            await accountService.DeleteUserAsync(sharedId);
            return Result.Failure($"员工业务档案落库失败，已触发补偿机制撤销底层身份账号，保持系统数据干净。底层死因: {ex.Message}");
        }
    }
}
