using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

/// <summary>
/// 业务指令:新员工入职
/// 跨服务编排:先创建 Identity 账号,再写入 Employee 业务档案。
/// 失败时通过手动补偿(DeleteUserAsync)回滚 Identity,保持系统一致性。
/// </summary>
[AuthorizeRequirement("Employee.Onboard")]
[DistributedLock("iiot:lock:employee-onboard:{EmployeeNo}", TimeoutSeconds = 5)]
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
    public async Task<Result<Guid>> Handle(
        OnboardEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RoleName)
            && request.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("系统保护:禁止通过员工入职接口分配最高管理员权限");
        }

        var sharedId = Guid.NewGuid();

        // 第一步:创建 Identity 账号
        var identityResult = await accountService.CreateUserAsync(
            sharedId, request.EmployeeNo, request.Password);

        if (!identityResult.IsSuccess)
            return Result.Failure(identityResult.Errors?.ToArray() ?? ["身份账号创建失败"]);

        // 第二步:绑定角色(如有);失败则补偿 Identity
        if (!string.IsNullOrWhiteSpace(request.RoleName))
        {
            var roleResult = await accountService.AssignRoleToUserAsync(
                request.EmployeeNo, request.RoleName);

            if (!roleResult.IsSuccess)
            {
                await accountService.DeleteUserAsync(sharedId);
                return Result.Failure(
                    roleResult.Errors?.ToArray() ?? ["系统角色绑定失败,已撤销账号创建"]);
            }
        }

        // 第三步:落库 Employee 业务档案;失败则补偿 Identity
        var employee = new Employee(sharedId, request.EmployeeNo, request.RealName);

        if (request.ProcessIds is { Count: > 0 })
        {
            foreach (var processId in request.ProcessIds)
                employee.AddProcessAccess(processId);
        }

        if (request.DeviceIds is { Count: > 0 })
        {
            foreach (var deviceId in request.DeviceIds)
                employee.AddDeviceAccess(deviceId);
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
            return Result.Failure(
                $"员工业务档案落库失败,已触发补偿撤销底层身份账号。底层原因: {ex.Message}");
        }
    }
}