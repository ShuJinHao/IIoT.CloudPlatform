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

        if (request.RoleName is { Length: > 256 })
        {
            return await RejectAsync(
                request,
                [],
                [],
                requestedRoleName,
                null,
                "RoleNameTooLong",
                ["roleName 长度不能超过 256 个字符。"],
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

        string[]? firstObservedRoles = null;
        var roleChangeAttempted = false;
        RoleTransactionOutcome outcome;
        try
        {
            outcome = await unitOfWork.ExecuteResilientAsync(
                ExecuteTransactionAsync,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await WriteAuditAsync(
                request,
                firstObservedRoles ?? [],
                firstObservedRoles ?? [],
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
            await WriteAuditAsync(
                request,
                firstObservedRoles ?? [],
                firstObservedRoles ?? [],
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
            outcome.BeforeRoles,
            outcome.AfterRoles,
            requestedRoleName,
            canonicalRoleName,
            outcome.Succeeded,
            outcome.ResultCode,
            outcome.Succeeded
                ? null
                : outcome.Errors.FirstOrDefault() ?? "员工角色更新被拒绝。",
            outcome.Succeeded ? CancellationToken.None : cancellationToken);
        return outcome.Succeeded
            ? Result.Success(true)
            : Result.Failure(outcome.Errors);

        async Task<RoleTransactionOutcome> ExecuteTransactionAsync(
            CancellationToken transactionCancellationToken)
        {
            await unitOfWork.BeginTransactionAsync(transactionCancellationToken);

            var targetResult = await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
                request.EmployeeId,
                transactionCancellationToken);
            if (!targetResult.IsSuccess)
            {
                var errors = targetResult.Errors?.ToArray()
                    ?? [AdminTargetProtectionErrors.TargetNotFound];
                var targetRoles = errors.Contains(
                    AdminTargetProtectionErrors.AdminTargetProtected)
                    ? await identityAccountStore.GetRolesAsync(
                        request.EmployeeId,
                        transactionCancellationToken)
                    : [];
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Failure(
                    targetRoles,
                    errors.Contains(AdminTargetProtectionErrors.TargetNotFound)
                        ? "TargetNotFound"
                        : "AdminTargetProtected",
                    errors);
            }

            var targetEmployee = await employeeLookupService.GetByIdAsync(
                request.EmployeeId,
                transactionCancellationToken);
            if (targetEmployee is null)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Failure(
                    [],
                    "TargetNotFound",
                    [AdminTargetProtectionErrors.TargetNotFound]);
            }

            var currentRoles = await identityAccountStore.GetRolesAsync(
                request.EmployeeId,
                transactionCancellationToken);
            if (SystemRoles.ContainsAdminLike(currentRoles))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Failure(
                    currentRoles,
                    "AdminTargetProtected",
                    [AdminTargetProtectionErrors.AdminTargetProtected]);
            }

            var beforeRoles = NormalizeAssignableRoles(currentRoles);
            firstObservedRoles ??= beforeRoles;
            string[] afterRoles = canonicalRoleName is null ? [] : [canonicalRoleName];
            if (RolesAreEquivalent(beforeRoles, afterRoles))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Success(
                    firstObservedRoles,
                    afterRoles,
                    roleChangeAttempted ? "Succeeded" : "NoChange");
            }

            roleChangeAttempted = true;
            var roleResult = await identityAccountStore.ReplaceAssignableRoleAsync(
                request.EmployeeId,
                canonicalRoleName,
                transactionCancellationToken);
            if (!roleResult.IsSuccess || !roleResult.Value)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Failure(
                    firstObservedRoles,
                    "RolePersistenceFailed",
                    roleResult.Errors?.ToArray() ?? ["员工角色写入失败。"]);
            }

            var versionResult = await identityAccountStore.RotateSecurityStampAsync(
                request.EmployeeId,
                transactionCancellationToken);
            if (!versionResult.IsSuccess || !versionResult.Value)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Failure(
                    firstObservedRoles,
                    "StatusVersionRotationFailed",
                    versionResult.Errors?.ToArray() ?? ["身份状态版本轮换失败。"]);
            }

            await sessionRevocationService.RevokeAllAsync(
                request.EmployeeId,
                "employee-role-changed",
                transactionCancellationToken);
            await unitOfWork.CommitAsync(transactionCancellationToken);
            return RoleTransactionOutcome.Success(
                firstObservedRoles,
                afterRoles,
                "Succeeded");
        }
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

    private sealed record RoleTransactionOutcome(
        bool Succeeded,
        string[] BeforeRoles,
        string[] AfterRoles,
        string ResultCode,
        string[] Errors)
    {
        public static RoleTransactionOutcome Success(
            IEnumerable<string> beforeRoles,
            IEnumerable<string> afterRoles,
            string resultCode)
            => new(
                true,
                NormalizeAssignableRoles(beforeRoles),
                NormalizeAssignableRoles(afterRoles),
                resultCode,
                []);

        public static RoleTransactionOutcome Failure(
            IEnumerable<string> beforeRoles,
            string resultCode,
            string[] errors)
        {
            var normalizedRoles = NormalizeAssignableRoles(beforeRoles);
            return new(
                false,
                normalizedRoles,
                normalizedRoles,
                resultCode,
                errors);
        }
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
