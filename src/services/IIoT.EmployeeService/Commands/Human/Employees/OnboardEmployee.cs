using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

[AuthorizeRequirement("Employee.Onboard")]
[DistributedLock("iiot:lock:employee-onboard:{EmployeeNo}", TimeoutSeconds = 5)]
public record OnboardEmployeeCommand(
    string EmployeeNo,
    string RealName,
    string Password,
    string? RoleName = null
) : IHumanCommand<Result<Guid>>;

public class OnboardEmployeeHandler(
    IIdentityAccountStore identityAccountStore,
    IIdentityPasswordService identityPasswordService,
    IRepository<Employee> employeeRepository)
    : ICommandHandler<OnboardEmployeeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        OnboardEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RoleName)
            && request.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("系统保护：禁止通过员工入职接口分配最高管理员权限");
        }

        var sharedId = Guid.NewGuid();
        var account = IdentityAccount.Create(sharedId, request.EmployeeNo);

        var identityResult = await identityAccountStore.CreateAsync(account, cancellationToken);
        if (!identityResult.IsSuccess)
        {
            return Result.Failure(identityResult.Errors?.ToArray() ?? ["身份账号创建失败"]);
        }

        var passwordResult = await identityPasswordService.SetPasswordAsync(
            sharedId,
            request.Password,
            cancellationToken);

        if (!passwordResult.IsSuccess)
        {
            await identityAccountStore.DeleteAsync(sharedId, cancellationToken);
            return Result.Failure(passwordResult.Errors?.ToArray() ?? ["身份账号密码设置失败"]);
        }

        if (!string.IsNullOrWhiteSpace(request.RoleName))
        {
            var roleResult = await identityAccountStore.AssignRoleAsync(
                sharedId,
                request.RoleName,
                cancellationToken);

            if (!roleResult.IsSuccess)
            {
                await identityAccountStore.DeleteAsync(sharedId, cancellationToken);
                return Result.Failure(roleResult.Errors?.ToArray() ?? ["系统角色绑定失败，已撤销账号创建"]);
            }
        }

        var employee = new Employee(sharedId, request.EmployeeNo, request.RealName);
        employeeRepository.Add(employee);

        try
        {
            await employeeRepository.SaveChangesAsync(cancellationToken);
            return Result.Success(sharedId);
        }
        catch (Exception ex)
        {
            await identityAccountStore.DeleteAsync(sharedId, cancellationToken);
            return Result.Failure($"员工业务档案落库失败，已触发补偿删除身份账号。底层原因: {ex.Message}");
        }
    }
}
