using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

[AuthorizeRequirement(CloudPermissionCatalog.Employee.Update)]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record UpdateEmployeeProfileCommand(
    Guid EmployeeId,
    string RealName,
    bool IsActive,
    string? RoleName = null
) : IHumanCommand<Result<bool>>;

public class UpdateEmployeeProfileHandler(
    IRepository<Employee> employeeRepository,
    IIdentityAccountStore identityAccountStore,
    IUnitOfWork unitOfWork,
    IRefreshTokenService refreshTokenService,
    ICurrentUser currentUser,
    IPermissionProvider permissionProvider,
    IAdminTargetGuard adminTargetGuard)
    : ICommandHandler<UpdateEmployeeProfileCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateEmployeeProfileCommand request,
        CancellationToken cancellationToken)
    {
        var targetResult = await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
            request.EmployeeId,
            cancellationToken);
        if (!targetResult.IsSuccess)
        {
            return Result.Failure(targetResult.Errors?.ToArray()
                ?? [AdminTargetProtectionErrors.TargetNotFound]);
        }

        var realName = request.RealName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(realName))
        {
            return Result.Failure("员工姓名不能为空");
        }

        var normalizedRoleName = request.RoleName?.Trim();
        if (request.RoleName is not null)
        {
            var rolePermissionResult = await EnsurePermissionAsync(
                CloudPermissionCatalog.Employee.UpdateAccess,
                "角色设置需要 Employee.UpdateAccess 权限",
                cancellationToken);
            if (!rolePermissionResult.IsSuccess)
            {
                return Result.Failure(rolePermissionResult.Errors?.ToArray()
                    ?? ["角色设置需要 Employee.UpdateAccess 权限"]);
            }

            if (SystemRoles.IsAdmin(normalizedRoleName))
            {
                return Result.Failure("管理员角色禁止通过员工编辑维护");
            }
        }

        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(request.EmployeeId),
                cancellationToken);

            if (employee is null)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return Result.Failure(AdminTargetProtectionErrors.TargetNotFound);
            }

            var statusChanged = employee.IsActive != request.IsActive;
            if (statusChanged)
            {
                var statusPermissionResult = await EnsurePermissionAsync(
                    CloudPermissionCatalog.Employee.Deactivate,
                    "人员状态变更需要 Employee.Deactivate 权限",
                    cancellationToken);
                if (!statusPermissionResult.IsSuccess)
                {
                    await unitOfWork.RollbackAsync(cancellationToken);
                    return Result.Failure(statusPermissionResult.Errors?.ToArray()
                        ?? ["人员状态变更需要 Employee.Deactivate 权限"]);
                }
            }

            employee.Rename(employee.EmployeeNo, realName);
            if (request.IsActive)
            {
                employee.Activate();
            }
            else
            {
                employee.Deactivate();
            }

            employeeRepository.Update(employee);
            await employeeRepository.SaveChangesAsync(cancellationToken);

            var identityResult = await identityAccountStore.SetEnabledAsync(
                request.EmployeeId,
                request.IsActive,
                cancellationToken);

            if (!identityResult.IsSuccess)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return Result.Failure(identityResult.Errors?.ToArray() ?? ["账号状态同步失败"]);
            }

            if (request.RoleName is not null)
            {
                var roleResult = await identityAccountStore.ReplaceAssignableRoleAsync(
                    request.EmployeeId,
                    normalizedRoleName,
                    cancellationToken);
                if (!roleResult.IsSuccess)
                {
                    await unitOfWork.RollbackAsync(cancellationToken);
                    return Result.Failure(roleResult.Errors?.ToArray() ?? ["角色设置失败"]);
                }

                await refreshTokenService.RevokeSubjectTokensAsync(
                    IIoTClaimTypes.HumanActor,
                    request.EmployeeId,
                    "employee-role-changed",
                    cancellationToken);
            }

            if (statusChanged && !request.IsActive)
            {
                await refreshTokenService.RevokeSubjectTokensAsync(
                    IIoTClaimTypes.HumanActor,
                    request.EmployeeId,
                    "employee-deactivated",
                    cancellationToken);
            }

            await unitOfWork.CommitAsync(cancellationToken);
            return Result.Success(true);
        }
        catch (Exception)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<Result> EnsurePermissionAsync(
        string requiredPermission,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        if (SystemRoles.ContainsAdmin(currentUser.Roles))
        {
            return Result.Success();
        }

        if (!Guid.TryParse(currentUser.Id, out var userId))
        {
            return Result.Failure("拒绝访问：用户凭证格式异常");
        }

        var permissions = await permissionProvider.GetPermissionsAsync(userId, cancellationToken);
        return permissions.Any(permission => string.Equals(
                permission?.Trim(),
                requiredPermission,
                StringComparison.OrdinalIgnoreCase))
            ? Result.Success()
            : Result.Failure(failureMessage);
    }
}
