using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Specifications;
using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
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
    IRolePolicyService rolePolicyService,
    IRepository<Employee> employeeRepository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IPermissionProvider permissionProvider)
    : ICommandHandler<OnboardEmployeeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        OnboardEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        var normalizedEmployeeNo = request.EmployeeNo.Trim();
        var normalizedRealName = request.RealName.Trim();
        var normalizedRoleName = request.RoleName?.Trim();
        if (!string.IsNullOrEmpty(normalizedRoleName))
        {
            if (SystemRoles.IsAdminLike(request.RoleName))
            {
                return Result.Failure("管理员角色禁止通过该接口创建");
            }

            var rolePermissionResult = await EnsureCanUpdateAccessAsync(cancellationToken);
            if (!rolePermissionResult.IsSuccess)
            {
                return Result.Failure(rolePermissionResult.Errors?.ToArray() ?? ["角色设置需要 Employee.UpdateAccess 权限"]);
            }

            if (!await rolePolicyService.RoleExistsAsync(normalizedRoleName))
            {
                return Result.Failure("角色未定义");
            }
        }

        var sharedId = Guid.NewGuid();
        var creationAttempted = false;
        return await unitOfWork.ExecuteResilientAsync(
            ExecuteTransactionAsync,
            cancellationToken);

        async Task<Result<Guid>> ExecuteTransactionAsync(
            CancellationToken transactionCancellationToken)
        {
            await unitOfWork.BeginTransactionAsync(transactionCancellationToken);

            var accountByEmployeeNo = await identityAccountStore.GetByEmployeeNoAsync(
                normalizedEmployeeNo,
                transactionCancellationToken);
            var accountById = await identityAccountStore.GetByIdAsync(
                sharedId,
                transactionCancellationToken);
            var employeeByEmployeeNo = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeByEmployeeNoSpec(normalizedEmployeeNo),
                transactionCancellationToken);
            var employeeById = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(sharedId),
                transactionCancellationToken);

            if (accountByEmployeeNo is not null
                || accountById is not null
                || employeeByEmployeeNo is not null
                || employeeById is not null)
            {
                if (creationAttempted
                    && IsCommittedReplay(
                        accountByEmployeeNo,
                        accountById,
                        employeeByEmployeeNo,
                        employeeById))
                {
                    await unitOfWork.RollbackAsync(transactionCancellationToken);
                    return Result.Success(sharedId);
                }

                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return accountByEmployeeNo is not null
                    ? Result.Failure("员工账号已存在")
                    : Result.Failure("员工入职状态不完整，已停止重试");
            }

            creationAttempted = true;
            var account = IdentityAccount.Create(sharedId, normalizedEmployeeNo);

            var identityResult = await identityAccountStore.CreateAsync(
                account,
                transactionCancellationToken);
            if (!identityResult.IsSuccess)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Failure(identityResult.Errors?.ToArray() ?? ["账号创建失败"]);
            }

            var passwordResult = await identityPasswordService.SetPasswordAsync(
                sharedId,
                request.Password,
                transactionCancellationToken);

            if (!passwordResult.IsSuccess)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Failure(passwordResult.Errors?.ToArray() ?? ["密码设置失败"]);
            }

            if (!string.IsNullOrEmpty(normalizedRoleName))
            {
                var roleResult = await identityAccountStore.AssignRoleAsync(
                    sharedId,
                    normalizedRoleName,
                    transactionCancellationToken);

                if (!roleResult.IsSuccess)
                {
                    await unitOfWork.RollbackAsync(transactionCancellationToken);
                    return Result.Failure(roleResult.Errors?.ToArray() ?? ["角色设置失败"]);
                }
            }

            var employee = new Employee(sharedId, normalizedEmployeeNo, normalizedRealName);
            employeeRepository.Add(employee);
            await employeeRepository.SaveChangesAsync(transactionCancellationToken);

            await unitOfWork.CommitAsync(transactionCancellationToken);

            return Result.Success(sharedId);
        }

        bool IsCommittedReplay(
            IdentityAccount? accountByEmployeeNo,
            IdentityAccount? accountById,
            Employee? employeeByEmployeeNo,
            Employee? employeeById)
        {
            // The shared id is private to this handler invocation. Once both
            // aggregates exist under that id and employee number, the original
            // transaction committed. Follow-up profile, status, role, or device
            // access writes must not turn a lost commit acknowledgement into a
            // false onboarding failure.
            return accountByEmployeeNo is not null
                   && accountById is not null
                   && employeeByEmployeeNo is not null
                   && employeeById is not null
                   && accountByEmployeeNo.Id == sharedId
                   && accountById.Id == sharedId
                   && employeeByEmployeeNo.Id == sharedId
                   && employeeById.Id == sharedId
                   && string.Equals(
                       accountById.EmployeeNo,
                       normalizedEmployeeNo,
                       StringComparison.Ordinal)
                   && string.Equals(
                       employeeById.EmployeeNo,
                       normalizedEmployeeNo,
                       StringComparison.Ordinal);
        }
    }

    private async Task<Result> EnsureCanUpdateAccessAsync(CancellationToken cancellationToken)
    {
        if (SystemRoles.IsAuthenticatedHumanAdmin(
                currentUser.IsAuthenticated,
                currentUser.ActorType,
                currentUser.Roles))
        {
            return Result.Success();
        }

        if (!Guid.TryParse(currentUser.Id, out var userId))
        {
            return Result.Failure("拒绝访问：用户凭证格式异常");
        }

        var permissions = await permissionProvider.GetPermissionsAsync(userId, cancellationToken);
        return permissions.Contains(CloudPermissionCatalog.Employee.UpdateAccess)
            ? Result.Success()
            : Result.Failure("角色设置需要 Employee.UpdateAccess 权限");
    }
}
