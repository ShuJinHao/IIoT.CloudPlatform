using IIoT.Core.Employees.Aggregates.Employees;
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
    IRepository<Employee> employeeRepository,
    IUnitOfWork unitOfWork)
    : ICommandHandler<OnboardEmployeeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        OnboardEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RoleName)
            && request.RoleName.Equals(
                IIoT.Services.Common.Contracts.Authorization.SystemRoles.Admin,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("管理员角色禁止通过该接口创建");
        }

        var existingAccount = await identityAccountStore.GetByEmployeeNoAsync(request.EmployeeNo, cancellationToken);
        if (existingAccount is not null)
        {
            return Result.Failure("员工账号已存在");
        }

        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);

            var sharedId = Guid.NewGuid();
            var account = IdentityAccount.Create(sharedId, request.EmployeeNo);

            var identityResult = await identityAccountStore.CreateAsync(account, cancellationToken);
            if (!identityResult.IsSuccess)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return Result.Failure(identityResult.Errors?.ToArray() ?? ["账号创建失败"]);
            }

            var passwordResult = await identityPasswordService.SetPasswordAsync(
                sharedId,
                request.Password,
                cancellationToken);

            if (!passwordResult.IsSuccess)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return Result.Failure(passwordResult.Errors?.ToArray() ?? ["密码设置失败"]);
            }

            if (!string.IsNullOrWhiteSpace(request.RoleName))
            {
                var roleResult = await identityAccountStore.AssignRoleAsync(
                    sharedId,
                    request.RoleName,
                    cancellationToken);

                if (!roleResult.IsSuccess)
                {
                    await unitOfWork.RollbackAsync(cancellationToken);
                    return Result.Failure(roleResult.Errors?.ToArray() ?? ["角色设置失败"]);
                }
            }

            var employee = new Employee(sharedId, request.EmployeeNo, request.RealName);
            employeeRepository.Add(employee);
            await employeeRepository.SaveChangesAsync(cancellationToken);

            await unitOfWork.CommitAsync(cancellationToken);

            return Result.Success(sharedId);
        }
        catch (Exception ex)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            return Result.Failure($"员工入职失败: {ex.Message}");
        }
    }
}

