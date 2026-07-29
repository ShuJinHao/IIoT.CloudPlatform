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
    IEmployeeMutationObservationReader mutationObservationReader,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<UpdateEmployeeRoleCommand, Result<bool>>
{
    private const string OperationType = "Employee.Role.Update";
    private static readonly TimeSpan CommitObservationTimeout = TimeSpan.FromSeconds(5);

    public async Task<Result<bool>> Handle(
        UpdateEmployeeRoleCommand request,
        CancellationToken cancellationToken)
    {
        var operation = RoleOperationContext.Create();
        if (!IsAuthenticatedHumanActor(currentUser, out var actorUserId))
        {
            return await RejectAsync(
                operation,
                request,
                [],
                [],
                NormalizeRequestedRole(request.RoleName),
                null,
                "HumanActorRequired",
                ["只有已认证的人类用户可以修改员工角色。"]);
        }

        var requestedRoleName = NormalizeRequestedRole(request.RoleName);
        if (request.RoleName is not null && requestedRoleName is null)
        {
            return await RejectAsync(
                operation,
                request,
                [],
                [],
                null,
                null,
                "RoleNameBlank",
                ["roleName 不能为空或纯空白；使用 null 明确清除角色。"]);
        }

        if (request.RoleName is { Length: > 256 })
        {
            return await RejectAsync(
                operation,
                request,
                [],
                [],
                requestedRoleName,
                null,
                "RoleNameTooLong",
                ["roleName 长度不能超过 256 个字符。"]);
        }

        if (SystemRoles.IsAdminLike(requestedRoleName))
        {
            return await RejectAsync(
                operation,
                request,
                [],
                [],
                requestedRoleName,
                null,
                "AdminRoleNotAssignable",
                ["管理员角色禁止通过员工角色入口分配。"]);
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
                    operation,
                    request,
                    [],
                    [],
                    requestedRoleName,
                    null,
                    "RoleNotFound",
                    ["角色不存在。"]);
            }

            if (SystemRoles.IsAdminLike(canonicalRoleName))
            {
                return await RejectAsync(
                    operation,
                    request,
                    [],
                    [],
                    requestedRoleName,
                    canonicalRoleName,
                    "AdminRoleNotAssignable",
                    ["管理员角色禁止通过员工角色入口分配。"]);
            }
        }

        var callerIsAdmin = SystemRoles.IsAuthenticatedHumanAdmin(
            currentUser.IsAuthenticated,
            currentUser.ActorType,
            currentUser.Roles);
        if (!callerIsAdmin && actorUserId == request.EmployeeId)
        {
            return await RejectAsync(
                operation,
                request,
                [],
                [],
                requestedRoleName,
                canonicalRoleName,
                "SelfRoleChangeForbidden",
                ["非管理员禁止修改自己的角色。"]);
        }

        string[] expectedRoles = canonicalRoleName is null ? [] : [canonicalRoleName];
        RoleMutationBaseline? baseline = null;
        var commitAttempted = false;
        RoleTransactionOutcome outcome;
        try
        {
            outcome = await unitOfWork.ExecuteResilientAsync(
                ExecuteTransactionAsync,
                cancellationToken);
        }
        catch (Exception exception)
            when (commitAttempted && exception is not OperationCanceledException)
        {
            outcome = await ResolveCommitAsync(
                request.EmployeeId,
                baseline,
                expectedRoles,
                operation.TargetSecurityStamp);
        }
        catch (OperationCanceledException)
        {
            await WriteAuditAsync(
                operation,
                request,
                baseline?.Roles ?? [],
                baseline?.Roles ?? [],
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
                operation,
                request,
                baseline?.Roles ?? [],
                baseline?.Roles ?? [],
                requestedRoleName,
                canonicalRoleName,
                succeeded: false,
                resultCode: "TransactionFailed",
                failureReason: "员工角色更新事务失败。",
                CancellationToken.None);
            throw;
        }

        await WriteAuditAsync(
            operation,
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
            CancellationToken.None);
        return outcome.Kind switch
        {
            RoleTransactionOutcomeKind.Succeeded => Result.Success(true),
            RoleTransactionOutcomeKind.Conflict =>
                throw new EmployeeRoleUpdateConflictException(),
            RoleTransactionOutcomeKind.CommitUnknown =>
                throw new EmployeeRoleUpdateCommitUnknownException(),
            _ => Result.Failure(outcome.Errors)
        };

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
            var accountState = await identityAccountStore.GetStateSnapshotAsync(
                request.EmployeeId,
                transactionCancellationToken);
            if (accountState is null)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Failure(
                    beforeRoles,
                    "TargetNotFound",
                    [AdminTargetProtectionErrors.TargetNotFound]);
            }

            var current = new RoleMutationBaseline(
                beforeRoles,
                targetEmployee.IsActive,
                accountState);
            if (baseline is null)
            {
                baseline = current;
            }
            else if (MatchesRoleTarget(
                         current,
                         baseline,
                         expectedRoles,
                         operation.TargetSecurityStamp))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Success(
                    baseline.Roles,
                    expectedRoles,
                    "CommitRecovered");
            }
            else if (!MatchesBaseline(current, baseline))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Conflict(
                    baseline.Roles,
                    beforeRoles);
            }

            if (RolesAreEquivalent(beforeRoles, expectedRoles))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Success(
                    baseline.Roles,
                    expectedRoles,
                    "NoChange");
            }

            var roleResult = await identityAccountStore.ReplaceAssignableRoleAsync(
                request.EmployeeId,
                canonicalRoleName,
                transactionCancellationToken);
            if (!roleResult.IsSuccess || !roleResult.Value)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Failure(
                    baseline.Roles,
                    "RolePersistenceFailed",
                    roleResult.Errors?.ToArray() ?? ["员工角色写入失败。"]);
            }

            var versionResult = await identityAccountStore.CompareExchangeStateAsync(
                request.EmployeeId,
                accountState,
                accountState.IsEnabled,
                operation.TargetSecurityStamp,
                transactionCancellationToken);
            if (!versionResult.IsSuccess)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Failure(
                    baseline.Roles,
                    "StatusVersionRotationFailed",
                    versionResult.Errors?.ToArray() ?? ["身份状态版本轮换失败。"]);
            }
            if (versionResult.Value != IdentityAccountCompareExchangeOutcome.Applied)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return RoleTransactionOutcome.Conflict(
                    baseline.Roles,
                    beforeRoles);
            }

            await sessionRevocationService.RevokeAllAsync(
                request.EmployeeId,
                "employee-role-changed",
                transactionCancellationToken);
            commitAttempted = true;
            await unitOfWork.CommitAsync(transactionCancellationToken);
            return RoleTransactionOutcome.Success(
                baseline.Roles,
                expectedRoles,
                "Succeeded");
        }
    }

    private async Task<Result<bool>> RejectAsync(
        RoleOperationContext operation,
        UpdateEmployeeRoleCommand request,
        IEnumerable<string> beforeRoles,
        IEnumerable<string> afterRoles,
        string? requestedRoleName,
        string? canonicalRoleName,
        string resultCode,
        string[] errors)
    {
        await WriteAuditAsync(
            operation,
            request,
            beforeRoles,
            afterRoles,
            requestedRoleName,
            canonicalRoleName,
            succeeded: false,
            resultCode,
            failureReason: errors.FirstOrDefault() ?? "员工角色更新被拒绝。",
            CancellationToken.None);
        return Result.Failure(errors);
    }

    private Task WriteAuditAsync(
        RoleOperationContext operation,
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
                operation.AuditTimeUtc,
                succeeded,
                EmployeeRoleAuditSummary.Serialize(
                    operation.OperationId,
                    beforeRoles,
                    afterRoles,
                    requestedRoleName,
                    canonicalRoleName,
                    succeeded ? "Succeeded" : "Rejected",
                    resultCode),
                failureReason,
                operation.AuditIdempotencyKey),
            cancellationToken);
    }

    private async Task<RoleTransactionOutcome> ResolveCommitAsync(
        Guid employeeId,
        RoleMutationBaseline? baseline,
        IReadOnlyCollection<string> expectedRoles,
        string targetSecurityStamp)
    {
        if (baseline is null)
        {
            return RoleTransactionOutcome.CommitUnknown([]);
        }

        using var timeout = new CancellationTokenSource(CommitObservationTimeout);
        EmployeeMutationObservation observation;
        try
        {
            observation = await mutationObservationReader.ObserveAsync(
                employeeId,
                timeout.Token);
        }
        catch
        {
            return RoleTransactionOutcome.CommitUnknown(baseline.Roles);
        }

        if (MatchesRoleTarget(
                observation,
                baseline,
                expectedRoles,
                targetSecurityStamp))
        {
            return RoleTransactionOutcome.Success(
                baseline.Roles,
                expectedRoles,
                "CommitRecovered");
        }

        if (MatchesBaseline(observation, baseline))
        {
            return RoleTransactionOutcome.CommitUnknown(baseline.Roles);
        }

        return RoleTransactionOutcome.Conflict(
            baseline.Roles,
            observation.Roles);
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

    private static bool MatchesBaseline(
        RoleMutationBaseline current,
        RoleMutationBaseline baseline)
        => current.EmployeeIsActive == baseline.EmployeeIsActive
           && current.Account.IsEnabled == baseline.Account.IsEnabled
           && string.Equals(
               current.Account.SecurityStamp,
               baseline.Account.SecurityStamp,
               StringComparison.Ordinal)
           && RolesAreEquivalent(current.Roles, baseline.Roles);

    private static bool MatchesBaseline(
        EmployeeMutationObservation observation,
        RoleMutationBaseline baseline)
        => observation.EmployeeExists
           && observation.AccountExists
           && observation.EmployeeIsActive == baseline.EmployeeIsActive
           && observation.AccountIsEnabled == baseline.Account.IsEnabled
           && string.Equals(
               observation.AccountSecurityStamp,
               baseline.Account.SecurityStamp,
               StringComparison.Ordinal)
           && RolesAreEquivalent(
               NormalizeAssignableRoles(observation.Roles),
               baseline.Roles);

    private static bool MatchesRoleTarget(
        RoleMutationBaseline current,
        RoleMutationBaseline baseline,
        IReadOnlyCollection<string> expectedRoles,
        string targetSecurityStamp)
        => current.EmployeeIsActive == baseline.EmployeeIsActive
           && current.Account.IsEnabled == baseline.Account.IsEnabled
           && string.Equals(
               current.Account.SecurityStamp,
               targetSecurityStamp,
               StringComparison.Ordinal)
           && RolesAreEquivalent(current.Roles, expectedRoles);

    private static bool MatchesRoleTarget(
        EmployeeMutationObservation observation,
        RoleMutationBaseline baseline,
        IReadOnlyCollection<string> expectedRoles,
        string targetSecurityStamp)
        => observation.EmployeeExists
           && observation.AccountExists
           && observation.EmployeeIsActive == baseline.EmployeeIsActive
           && observation.AccountIsEnabled == baseline.Account.IsEnabled
           && string.Equals(
               observation.AccountSecurityStamp,
               targetSecurityStamp,
               StringComparison.Ordinal)
           && RolesAreEquivalent(
               NormalizeAssignableRoles(observation.Roles),
               expectedRoles);

    private sealed record RoleOperationContext(
        Guid OperationId,
        string TargetSecurityStamp,
        DateTime AuditTimeUtc,
        string AuditIdempotencyKey)
    {
        public static RoleOperationContext Create()
        {
            var operationId = Guid.NewGuid();
            return new RoleOperationContext(
                operationId,
                Guid.NewGuid().ToString("N"),
                DateTime.UtcNow,
                $"employee-role-update:{operationId:N}");
        }
    }

    private sealed record RoleMutationBaseline(
        string[] Roles,
        bool EmployeeIsActive,
        IdentityAccountStateSnapshot Account);

    private enum RoleTransactionOutcomeKind
    {
        Succeeded,
        Rejected,
        Conflict,
        CommitUnknown
    }

    private sealed record RoleTransactionOutcome(
        RoleTransactionOutcomeKind Kind,
        string[] BeforeRoles,
        string[] AfterRoles,
        string ResultCode,
        string[] Errors)
    {
        public bool Succeeded => Kind == RoleTransactionOutcomeKind.Succeeded;

        public static RoleTransactionOutcome Success(
            IEnumerable<string> beforeRoles,
            IEnumerable<string> afterRoles,
            string resultCode)
            => new(
                RoleTransactionOutcomeKind.Succeeded,
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
                RoleTransactionOutcomeKind.Rejected,
                normalizedRoles,
                normalizedRoles,
                resultCode,
                errors);
        }

        public static RoleTransactionOutcome Conflict(
            IEnumerable<string> beforeRoles,
            IEnumerable<string> observedRoles)
            => new(
                RoleTransactionOutcomeKind.Conflict,
                NormalizeAssignableRoles(beforeRoles),
                NormalizeAssignableRoles(observedRoles),
                "CommitConflict",
                [EmployeeRoleUpdateConflictException.PublicMessage]);

        public static RoleTransactionOutcome CommitUnknown(
            IEnumerable<string> beforeRoles)
        {
            var normalizedRoles = NormalizeAssignableRoles(beforeRoles);
            return new(
                RoleTransactionOutcomeKind.CommitUnknown,
                normalizedRoles,
                normalizedRoles,
                "CommitUnknown",
                [EmployeeRoleUpdateCommitUnknownException.PublicMessage]);
        }
    }
}

internal static class EmployeeRoleAuditSummary
{
    public static string Serialize(
        Guid operationId,
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
            operationId,
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
