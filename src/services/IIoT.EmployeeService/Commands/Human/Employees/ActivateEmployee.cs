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

[AuthorizeRequirement(CloudPermissionCatalog.Employee.Deactivate)]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public sealed record ActivateEmployeeCommand(Guid EmployeeId) : IHumanCommand<Result>;

public sealed class ActivateEmployeeHandler(
    IRepository<Employee> employeeRepository,
    IIdentityAccountStore identityAccountStore,
    IUnitOfWork unitOfWork,
    IHumanSessionRevocationService sessionRevocationService,
    IAdminTargetGuard adminTargetGuard,
    IEmployeeMutationObservationReader mutationObservationReader)
    : ICommandHandler<ActivateEmployeeCommand, Result>
{
    private static readonly TimeSpan CommitObservationTimeout = TimeSpan.FromSeconds(5);

    public async Task<Result> Handle(
        ActivateEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        var activationSecurityStamp = Guid.NewGuid().ToString("N");
        ActivationBaseline? baseline = null;
        var commitAttempted = false;
        ActivationTransactionOutcome outcome;
        try
        {
            outcome = await unitOfWork.ExecuteResilientAsync(
                ExecuteTransactionAsync,
                cancellationToken);
        }
        catch (Exception)
            when (commitAttempted)
        {
            outcome = await ResolveCommitAsync(
                request.EmployeeId,
                baseline,
                activationSecurityStamp);
        }

        return outcome.Kind switch
        {
            ActivationTransactionOutcomeKind.Succeeded => Result.Success(),
            ActivationTransactionOutcomeKind.Conflict =>
                throw new EmployeeActivationConflictException(),
            ActivationTransactionOutcomeKind.CommitUnknown =>
                throw new EmployeeActivationCommitUnknownException(),
            _ => Result.Failure(outcome.Errors)
        };

        async Task<ActivationTransactionOutcome> ExecuteTransactionAsync(
            CancellationToken transactionCancellationToken)
        {
            await unitOfWork.BeginTransactionAsync(transactionCancellationToken);

            var targetResult = await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
                request.EmployeeId,
                transactionCancellationToken);
            if (!targetResult.IsSuccess)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return ActivationTransactionOutcome.Failure(
                    targetResult.Errors?.ToArray()
                    ?? [AdminTargetProtectionErrors.TargetNotFound]);
            }

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(request.EmployeeId),
                transactionCancellationToken);
            if (employee is null)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return ActivationTransactionOutcome.Failure(
                    [AdminTargetProtectionErrors.TargetNotFound]);
            }

            var accountState = await identityAccountStore.GetStateSnapshotAsync(
                request.EmployeeId,
                transactionCancellationToken);
            if (accountState is null)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return ActivationTransactionOutcome.Failure(
                    ["员工身份账号启用失败"]);
            }

            var currentRoles = NormalizeRoles(
                await identityAccountStore.GetRolesAsync(
                    request.EmployeeId,
                    transactionCancellationToken));
            var current = new ActivationBaseline(
                employee.IsActive,
                accountState,
                currentRoles);
            if (baseline is null)
            {
                baseline = current;
            }
            else if (MatchesActivationTarget(
                         current,
                         baseline,
                         activationSecurityStamp))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return ActivationTransactionOutcome.Success("CommitRecovered");
            }
            else if (!MatchesBaseline(current, baseline))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return ActivationTransactionOutcome.Conflict();
            }

            if (!employee.IsActive)
            {
                employee.Activate();
                employeeRepository.Update(employee);
                await employeeRepository.SaveChangesAsync(transactionCancellationToken);
            }

            var identityResult = await identityAccountStore.CompareExchangeStateAsync(
                request.EmployeeId,
                accountState,
                isEnabled: true,
                securityStamp: activationSecurityStamp,
                cancellationToken: transactionCancellationToken);
            if (!identityResult.IsSuccess)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return ActivationTransactionOutcome.Failure(
                    identityResult.Errors?.ToArray()
                    ?? ["员工身份账号启用失败"]);
            }
            if (identityResult.Value != IdentityAccountCompareExchangeOutcome.Applied)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return ActivationTransactionOutcome.Conflict();
            }

            await sessionRevocationService.RevokeAllAsync(
                request.EmployeeId,
                "employee-activated-relogin-required",
                transactionCancellationToken);
            commitAttempted = true;
            await unitOfWork.CommitAsync(transactionCancellationToken);

            return ActivationTransactionOutcome.Success("Succeeded");
        }
    }

    private async Task<ActivationTransactionOutcome> ResolveCommitAsync(
        Guid employeeId,
        ActivationBaseline? baseline,
        string activationSecurityStamp)
    {
        if (baseline is null)
        {
            return ActivationTransactionOutcome.CommitUnknown();
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
            return ActivationTransactionOutcome.CommitUnknown();
        }

        if (MatchesActivationTarget(
                observation,
                baseline,
                activationSecurityStamp))
        {
            return ActivationTransactionOutcome.Success("CommitRecovered");
        }

        return MatchesBaseline(observation, baseline)
            ? ActivationTransactionOutcome.CommitUnknown()
            : ActivationTransactionOutcome.Conflict();
    }

    private static bool MatchesBaseline(
        ActivationBaseline current,
        ActivationBaseline baseline)
        => current.EmployeeIsActive == baseline.EmployeeIsActive
           && current.Account.IsEnabled == baseline.Account.IsEnabled
           && string.Equals(
               current.Account.SecurityStamp,
               baseline.Account.SecurityStamp,
               StringComparison.Ordinal)
           && RolesAreEquivalent(current.Roles, baseline.Roles);

    private static bool MatchesBaseline(
        EmployeeMutationObservation observation,
        ActivationBaseline baseline)
        => observation.EmployeeExists
           && observation.AccountExists
           && observation.EmployeeIsActive == baseline.EmployeeIsActive
           && observation.AccountIsEnabled == baseline.Account.IsEnabled
           && string.Equals(
               observation.AccountSecurityStamp,
               baseline.Account.SecurityStamp,
               StringComparison.Ordinal)
           && RolesAreEquivalent(
               NormalizeRoles(observation.Roles),
               baseline.Roles);

    private static bool MatchesActivationTarget(
        ActivationBaseline current,
        ActivationBaseline baseline,
        string activationSecurityStamp)
        => current.EmployeeIsActive
           && current.Account.IsEnabled
           && string.Equals(
               current.Account.SecurityStamp,
               activationSecurityStamp,
               StringComparison.Ordinal)
           && RolesAreEquivalent(current.Roles, baseline.Roles);

    private static bool MatchesActivationTarget(
        EmployeeMutationObservation observation,
        ActivationBaseline baseline,
        string activationSecurityStamp)
        => observation.EmployeeExists
           && observation.AccountExists
           && observation.EmployeeIsActive
           && observation.AccountIsEnabled
           && string.Equals(
               observation.AccountSecurityStamp,
               activationSecurityStamp,
               StringComparison.Ordinal)
           && RolesAreEquivalent(
               NormalizeRoles(observation.Roles),
               baseline.Roles);

    private static string[] NormalizeRoles(IEnumerable<string> roles)
        => roles
            .Select(role => role?.Trim())
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool RolesAreEquivalent(
        IReadOnlyCollection<string> left,
        IReadOnlyCollection<string> right)
        => left.Count == right.Count
           && left
               .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
               .SequenceEqual(
                   right.OrderBy(role => role, StringComparer.OrdinalIgnoreCase),
                   StringComparer.OrdinalIgnoreCase);

    private sealed record ActivationBaseline(
        bool EmployeeIsActive,
        IdentityAccountStateSnapshot Account,
        string[] Roles);

    private enum ActivationTransactionOutcomeKind
    {
        Succeeded,
        Rejected,
        Conflict,
        CommitUnknown
    }

    private sealed record ActivationTransactionOutcome(
        ActivationTransactionOutcomeKind Kind,
        string ResultCode,
        string[] Errors)
    {
        public static ActivationTransactionOutcome Success(string resultCode)
            => new(ActivationTransactionOutcomeKind.Succeeded, resultCode, []);

        public static ActivationTransactionOutcome Failure(string[] errors)
            => new(ActivationTransactionOutcomeKind.Rejected, "Rejected", errors);

        public static ActivationTransactionOutcome Conflict()
            => new(
                ActivationTransactionOutcomeKind.Conflict,
                "CommitConflict",
                [EmployeeActivationConflictException.PublicMessage]);

        public static ActivationTransactionOutcome CommitUnknown()
            => new(
                ActivationTransactionOutcomeKind.CommitUnknown,
                "CommitUnknown",
                [EmployeeActivationCommitUnknownException.PublicMessage]);
    }
}
