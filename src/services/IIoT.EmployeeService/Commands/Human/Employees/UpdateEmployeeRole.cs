using System.Text.Json;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

[AuthorizeRequirement(CloudPermissionCatalog.Employee.UpdateAccess)]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public sealed record UpdateEmployeeRoleCommand(
    Guid EmployeeId,
    string? RoleName
) : IHumanCommand<Result<bool>>;

public sealed class UpdateEmployeeRoleHandler(
    IIdentityAccountStore identityAccountStore,
    IRolePolicyService rolePolicyService,
    IUnitOfWork unitOfWork,
    IHumanSessionRevocationService sessionRevocationService,
    IAdminTargetGuard adminTargetGuard,
    IEmployeeLookupService employeeLookupService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<UpdateEmployeeRoleCommand, Result<bool>>
{
    private const string OperationType = "Employee.Role.Update";

    public async Task<Result<bool>> Handle(
        UpdateEmployeeRoleCommand request,
        CancellationToken cancellationToken)
    {
        if (!IsAuthenticatedHumanActor(currentUser, out var actorUserId))
        {
            return await RejectAsync(
                request,
                [],
                [],
                NormalizeRequestedRole(request.RoleName),
                null,
                "HumanActorRequired",
                ["只有已认证的人类用户可以修改员工角色。"],
                cancellationToken);
        }

        var requestedRoleName = NormalizeRequestedRole(request.RoleName);
        if (request.RoleName is not null && requestedRoleName is null)
        {
            return await RejectAsync(
                request,
                [],
                [],
                null,
                null,
                "RoleNameBlank",
                ["roleName 不能为空或纯空白；使用 null 明确清除角色。"],
                cancellationToken);
        }

        if (SystemRoles.IsAdminLike(requestedRoleName))
        {
            return await RejectAsync(
                request,
                [],
                [],
                requestedRoleName,
                null,
                "AdminRoleNotAssignable",
                ["管理员角色禁止通过员工角色入口分配。"],
                cancellationToken);
        }

        string? canonicalRoleName = null;
        if (requestedRoleName is not null)
        {
            var formalRoles = await rolePolicyService.GetAllRolesAsync();
            canonicalRoleName = formalRoles.FirstOrDefault(role =>
                string.Equals(
                    role?.Trim(),
                    requestedRoleName,
                    StringComparison.OrdinalIgnoreCase));

            if (canonicalRoleName is null)
            {
                return await RejectAsync(
                    request,
                    [],
                    [],
                    requestedRoleName,
                    null,
                    "RoleNotFound",
                    ["角色不存在。"],
                    cancellationToken);
            }

            if (SystemRoles.IsAdminLike(canonicalRoleName))
            {
                return await RejectAsync(
                    request,
                    [],
                    [],
                    requestedRoleName,
                    canonicalRoleName,
                    "AdminRoleNotAssignable",
                    ["管理员角色禁止通过员工角色入口分配。"],
                    cancellationToken);
            }
        }

        var callerIsAdmin = SystemRoles.IsAuthenticatedHumanAdmin(
            currentUser.IsAuthenticated,
            currentUser.ActorType,
            currentUser.Roles);
        if (!callerIsAdmin && actorUserId == request.EmployeeId)
        {
            return await RejectAsync(
                request,
                [],
                [],
                requestedRoleName,
                canonicalRoleName,
                "SelfRoleChangeForbidden",
                ["非管理员禁止修改自己的角色。"],
                cancellationToken);
        }

        var targetResult = await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
            request.EmployeeId,
            cancellationToken);
        if (!targetResult.IsSuccess)
        {
            var errors = targetResult.Errors?.ToArray()
                ?? [AdminTargetProtectionErrors.TargetNotFound];
            var targetRoles = errors.Contains(AdminTargetProtectionErrors.AdminTargetProtected)
                ? await identityAccountStore.GetRolesAsync(request.EmployeeId, cancellationToken)
                : [];
            return await RejectAsync(
                request,
                targetRoles,
                targetRoles,
                requestedRoleName,
                canonicalRoleName,
                errors.Contains(AdminTargetProtectionErrors.TargetNotFound)
                    ? "TargetNotFound"
                    : "AdminTargetProtected",
                errors,
                cancellationToken);
        }

        var targetEmployee = await employeeLookupService.GetByIdAsync(
            request.EmployeeId,
            cancellationToken);
        if (targetEmployee is null)
        {
            return await RejectAsync(
                request,
                [],
                [],
                requestedRoleName,
                canonicalRoleName,
                "TargetNotFound",
                [AdminTargetProtectionErrors.TargetNotFound],
                cancellationToken);
        }

        var currentRoles = await identityAccountStore.GetRolesAsync(
            request.EmployeeId,
            cancellationToken);
        if (SystemRoles.ContainsAdminLike(currentRoles))
        {
            return await RejectAsync(
                request,
                currentRoles,
                currentRoles,
                requestedRoleName,
                canonicalRoleName,
                "AdminTargetProtected",
                [AdminTargetProtectionErrors.AdminTargetProtected],
                cancellationToken);
        }

        var beforeRoles = NormalizeAssignableRoles(currentRoles);
        string[] afterRoles = canonicalRoleName is null ? [] : [canonicalRoleName];
        if (RolesAreEquivalent(beforeRoles, afterRoles))
        {
            await WriteAuditAsync(
                request,
                beforeRoles,
                afterRoles,
                requestedRoleName,
                canonicalRoleName,
                succeeded: true,
                resultCode: "NoChange",
                failureReason: null,
                cancellationToken);
            return Result.Success(true);
        }

        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);

            var roleResult = await identityAccountStore.ReplaceAssignableRoleAsync(
                request.EmployeeId,
                canonicalRoleName,
                cancellationToken);
            if (!roleResult.IsSuccess || !roleResult.Value)
            {
                return await RollbackAndRejectAsync(
                    request,
                    beforeRoles,
                    requestedRoleName,
                    canonicalRoleName,
                    "RolePersistenceFailed",
                    roleResult.Errors?.ToArray() ?? ["员工角色写入失败。"],
                    cancellationToken);
            }

            var versionResult = await identityAccountStore.RotateSecurityStampAsync(
                request.EmployeeId,
                cancellationToken);
            if (!versionResult.IsSuccess || !versionResult.Value)
            {
                return await RollbackAndRejectAsync(
                    request,
                    beforeRoles,
                    requestedRoleName,
                    canonicalRoleName,
                    "StatusVersionRotationFailed",
                    versionResult.Errors?.ToArray() ?? ["身份状态版本轮换失败。"],
                    cancellationToken);
            }

            await sessionRevocationService.RevokeAllAsync(
                request.EmployeeId,
                "employee-role-changed",
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            await WriteAuditAsync(
                request,
                beforeRoles,
                beforeRoles,
                requestedRoleName,
                canonicalRoleName,
                succeeded: false,
                resultCode: "Canceled",
                failureReason: "员工角色更新已取消。",
                CancellationToken.None);
            throw;
        }
        catch (Exception)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            await WriteAuditAsync(
                request,
                beforeRoles,
                beforeRoles,
                requestedRoleName,
                canonicalRoleName,
                succeeded: false,
                resultCode: "TransactionFailed",
                failureReason: "员工角色更新事务失败。",
                CancellationToken.None);
            throw;
        }

        await WriteAuditAsync(
            request,
            beforeRoles,
            afterRoles,
            requestedRoleName,
            canonicalRoleName,
            succeeded: true,
            resultCode: "Succeeded",
            failureReason: null,
            cancellationToken);
        return Result.Success(true);
    }

    private async Task<Result<bool>> RollbackAndRejectAsync(
        UpdateEmployeeRoleCommand request,
        IReadOnlyCollection<string> beforeRoles,
        string? requestedRoleName,
        string? canonicalRoleName,
        string resultCode,
        string[] errors,
        CancellationToken cancellationToken)
    {
        await unitOfWork.RollbackAsync(cancellationToken);
        return await RejectAsync(
            request,
            beforeRoles,
            beforeRoles,
            requestedRoleName,
            canonicalRoleName,
            resultCode,
            errors,
            cancellationToken);
    }

    private async Task<Result<bool>> RejectAsync(
        UpdateEmployeeRoleCommand request,
        IEnumerable<string> beforeRoles,
        IEnumerable<string> afterRoles,
        string? requestedRoleName,
        string? canonicalRoleName,
        string resultCode,
        string[] errors,
        CancellationToken cancellationToken)
    {
        await WriteAuditAsync(
            request,
            beforeRoles,
            afterRoles,
            requestedRoleName,
            canonicalRoleName,
            succeeded: false,
            resultCode,
            failureReason: errors.FirstOrDefault() ?? "员工角色更新被拒绝。",
            cancellationToken);
        return Result.Failure(errors);
    }

    private Task WriteAuditAsync(
        UpdateEmployeeRoleCommand request,
        IEnumerable<string> beforeRoles,
        IEnumerable<string> afterRoles,
        string? requestedRoleName,
        string? canonicalRoleName,
        bool succeeded,
        string resultCode,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        return auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                OperationType,
                "Employee",
                request.EmployeeId.ToString(),
                DateTime.UtcNow,
                succeeded,
                EmployeeRoleAuditSummary.Serialize(
                    beforeRoles,
                    afterRoles,
                    requestedRoleName,
                    canonicalRoleName,
                    succeeded ? "Succeeded" : "Rejected",
                    resultCode),
                failureReason),
            cancellationToken);
    }

    private static bool IsAuthenticatedHumanActor(
        ICurrentUser user,
        out Guid actorUserId)
    {
        actorUserId = Guid.Empty;
        return user.IsAuthenticated
               && string.Equals(
                   user.ActorType,
                   IIoTClaimTypes.HumanActor,
                   StringComparison.Ordinal)
               && Guid.TryParse(user.Id, out actorUserId);
    }

    private static Guid? ParseActorUserId(string? rawUserId)
        => Guid.TryParse(rawUserId, out var actorUserId)
            ? actorUserId
            : null;

    private static string? NormalizeRequestedRole(string? roleName)
    {
        if (roleName is null)
        {
            return null;
        }

        var normalized = roleName.Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static string[] NormalizeAssignableRoles(IEnumerable<string> roles)
        => roles
            .Select(role => role?.Trim())
            .Where(role =>
                !string.IsNullOrWhiteSpace(role)
                && !SystemRoles.IsAdminLike(role))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool RolesAreEquivalent(
        IReadOnlyCollection<string> beforeRoles,
        IReadOnlyCollection<string> afterRoles)
    {
        return beforeRoles.Count == afterRoles.Count
               && beforeRoles
                   .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                   .SequenceEqual(
                       afterRoles.OrderBy(role => role, StringComparer.OrdinalIgnoreCase),
                       StringComparer.OrdinalIgnoreCase);
    }
}

internal static class EmployeeRoleAuditSummary
{
    public static string Serialize(
        IEnumerable<string> beforeRoles,
        IEnumerable<string> afterRoles,
        string? requestedRole,
        string? canonicalRole,
        string outcome,
        string resultCode)
    {
        return JsonSerializer.Serialize(new
        {
            action = "EmployeeRoleUpdate",
            beforeRoles = NormalizeRoles(beforeRoles),
            afterRoles = NormalizeRoles(afterRoles),
            requestedRole,
            canonicalRole,
            outcome,
            resultCode
        });
    }

    private static string[] NormalizeRoles(IEnumerable<string> roles)
        => roles
            .Select(role => role?.Trim())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
