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
public record DeactivateEmployeeCommand(Guid EmployeeId) : IHumanCommand<Result>;

public class DeactivateEmployeeHandler(
    IRepository<Employee> employeeRepository,
    IIdentityAccountStore identityAccountStore,
    IUnitOfWork unitOfWork,
    IHumanSessionRevocationService sessionRevocationService,
    IAdminTargetGuard adminTargetGuard,
    IEmployeeMutationObservationReader mutationObservationReader)
    : ICommandHandler<DeactivateEmployeeCommand, Result>
{
    public async Task<Result> Handle(
        DeactivateEmployeeCommand request,
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

        var deactivationSecurityStamp = Guid.NewGuid().ToString("N");
        EmployeeMutationObservation? baseline = null;
        uint? targetRowVersion = null;
        string? targetSecurityStamp = null;
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

        async Task<Result> ExecuteTransactionAsync(
            CancellationToken transactionCancellationToken)
        {
            var current = await EmployeeWriteCommitRecovery.TryObserveAsync(
                mutationObservationReader,
                request.EmployeeId);
            if (current is null)
            {
                throw new EmployeeWriteCommitUnknownException();
            }

            if (baseline is null)
            {
                baseline = current;
            }
            else if (MatchesTarget(current))
            {
                return Result.Success();
            }
            else if (!EmployeeWriteCommitRecovery.MatchesExact(
                         current,
                         baseline))
            {
                throw new EmployeeWriteConflictException();
            }

            await unitOfWork.BeginTransactionAsync(transactionCancellationToken);

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(request.EmployeeId),
                transactionCancellationToken);

            if (employee is null)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Failure(AdminTargetProtectionErrors.TargetNotFound);
            }

            var accountState = await identityAccountStore.GetStateSnapshotAsync(
                request.EmployeeId,
                transactionCancellationToken);
            if (accountState is null)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Failure("员工身份账号停用失败");
            }

            if (current.EmployeeRowVersion.HasValue
                && employee.RowVersion != current.EmployeeRowVersion.Value)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                throw new EmployeeWriteConflictException();
            }

            if (accountState.IsEnabled != current.AccountIsEnabled
                || !string.Equals(
                    accountState.SecurityStamp,
                    current.AccountSecurityStamp,
                    StringComparison.Ordinal))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                throw new EmployeeWriteConflictException();
            }

            var requiresStateTransition =
                employee.IsActive || accountState.IsEnabled;
            targetRowVersion = employee.RowVersion;
            if (employee.IsActive)
            {
                employee.Deactivate();
                employeeRepository.Update(employee);
                await employeeRepository.SaveChangesAsync(transactionCancellationToken);
                targetRowVersion = employee.RowVersion;
            }

            targetSecurityStamp = requiresStateTransition
                                  || string.IsNullOrWhiteSpace(
                                      accountState.SecurityStamp)
                ? deactivationSecurityStamp
                : accountState.SecurityStamp;
            if (accountState.IsEnabled
                || !string.Equals(
                    accountState.SecurityStamp,
                    targetSecurityStamp,
                    StringComparison.Ordinal))
            {
                var identityResult =
                    await identityAccountStore.CompareExchangeStateAsync(
                    request.EmployeeId,
                    accountState,
                    isEnabled: false,
                    securityStamp: targetSecurityStamp,
                    cancellationToken: transactionCancellationToken);

                if (!identityResult.IsSuccess)
                {
                    await unitOfWork.RollbackAsync(transactionCancellationToken);
                    return Result.Failure(
                        identityResult.Errors?.ToArray() ?? ["员工身份账号停用失败"]);
                }
                if (identityResult.Value
                    != IdentityAccountCompareExchangeOutcome.Applied)
                {
                    await unitOfWork.RollbackAsync(transactionCancellationToken);
                    throw new EmployeeWriteConflictException();
                }
            }

            if (MatchesTarget(current))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Success();
            }

            await sessionRevocationService.RevokeAllAsync(
                request.EmployeeId,
                "employee-deactivated",
                transactionCancellationToken);
            commitAttempted = true;
            await unitOfWork.CommitAsync(transactionCancellationToken);

            return Result.Success();
        }

        async Task<Result> ResolveCommitAsync()
        {
            var observation =
                await EmployeeWriteCommitRecovery.TryObserveAsync(
                    mutationObservationReader,
                    request.EmployeeId);
            if (observation is null
                || baseline is null
                || EmployeeWriteCommitRecovery.MatchesExact(
                    observation,
                    baseline))
            {
                throw new EmployeeWriteCommitUnknownException();
            }

            if (MatchesTarget(observation))
            {
                return Result.Success();
            }

            throw new EmployeeWriteConflictException();
        }

        bool MatchesTarget(EmployeeMutationObservation observation)
            => baseline is not null
               && targetRowVersion.HasValue
               && targetSecurityStamp is not null
               && EmployeeWriteCommitRecovery.MatchesExact(
                   observation,
                   baseline with
                   {
                       EmployeeIsActive = false,
                       EmployeeRowVersion = targetRowVersion,
                       AccountIsEnabled = false,
                       AccountSecurityStamp = targetSecurityStamp,
                       HasActiveHumanSessions = false
                   });
    }
}
