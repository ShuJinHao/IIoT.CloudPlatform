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
    IPermissionProvider permissionProvider,
    IEmployeeMutationObservationReader mutationObservationReader)
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
        var commitAttempted = false;
        try
        {
            return await unitOfWork.ExecuteResilientAsync(
                ExecuteTransactionAsync,
                cancellationToken);
        }
        catch (EmployeeMutationException)
        {
            throw;
        }
        catch (Exception)
            when (commitAttempted)
        {
            return await ResolveCommitAsync();
        }

        async Task<Result<Guid>> ExecuteTransactionAsync(
            CancellationToken transactionCancellationToken)
        {
            if (creationAttempted)
            {
                var replayObservation =
                    await EmployeeWriteCommitRecovery.TryObserveAttemptAsync(
                        mutationObservationReader,
                        sharedId,
                        transactionCancellationToken);
                if (replayObservation is null)
                {
                    throw new EmployeeWriteCommitUnknownException();
                }

                if (EmployeeWriteCommitRecovery.IsOnboardTarget(
                        replayObservation,
                        normalizedEmployeeNo))
                {
                    return Result.Success(sharedId);
                }

                if (!EmployeeWriteCommitRecovery.IsAbsentBaseline(
                        replayObservation))
                {
                    throw new EmployeeWriteConflictException();
                }
            }

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
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                if (creationAttempted)
                {
                    throw new EmployeeWriteConflictException();
                }

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

            commitAttempted = true;
            await unitOfWork.CommitAsync(transactionCancellationToken);

            return Result.Success(sharedId);
        }

        async Task<Result<Guid>> ResolveCommitAsync()
        {
            var observation =
                await EmployeeWriteCommitRecovery.TryObserveCommitAsync(
                    mutationObservationReader,
                    sharedId);
            if (observation is null
                || EmployeeWriteCommitRecovery.IsAbsentBaseline(observation))
            {
                throw new EmployeeWriteCommitUnknownException();
            }

            if (EmployeeWriteCommitRecovery.IsOnboardTarget(
                    observation,
                    normalizedEmployeeNo))
            {
                return Result.Success(sharedId);
            }

            throw new EmployeeWriteConflictException();
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
