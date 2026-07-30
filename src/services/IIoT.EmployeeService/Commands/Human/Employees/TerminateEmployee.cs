using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

[AdminOnly]
[AuthorizeRequirement(CloudPermissionCatalog.Employee.Terminate)]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record TerminateEmployeeCommand(Guid EmployeeId)
    : IHumanCommand<Result>, IAdminOnlyAuditRequest
{
    public string AdminAuditOperationType => "Employee.Terminate";

    public string AdminAuditTargetType => "Employee";

    public string AdminAuditTargetIdOrKey => EmployeeId.ToString();
}

public class TerminateEmployeeHandler(
    IRepository<Employee> employeeRepository,
    IIdentityAccountStore identityAccountStore,
    IUnitOfWork unitOfWork,
    IHumanSessionRevocationService sessionRevocationService,
    IAdminTargetGuard adminTargetGuard,
    IEmployeeMutationObservationReader mutationObservationReader)
    : ICommandHandler<TerminateEmployeeCommand, Result>
{
    public async Task<Result> Handle(
        TerminateEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        var targetResult =
            await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
                request.EmployeeId,
                cancellationToken);
        if (!targetResult.IsSuccess)
        {
            return Result.Failure(targetResult.Errors?.ToArray()
                ?? [AdminTargetProtectionErrors.TargetNotFound]);
        }

        EmployeeMutationObservation? baseline = null;
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
            var current = await EmployeeWriteCommitRecovery.TryObserveAttemptAsync(
                mutationObservationReader,
                request.EmployeeId,
                transactionCancellationToken);
            if (current is null)
            {
                throw new EmployeeWriteCommitUnknownException();
            }

            if (baseline is null)
            {
                baseline = current;
            }
            else if (EmployeeWriteCommitRecovery.IsTerminationTarget(current))
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

            var account = await identityAccountStore.GetByIdAsync(
                request.EmployeeId,
                transactionCancellationToken);

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(request.EmployeeId),
                transactionCancellationToken);

            if (employee is null || account is null)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Failure(AdminTargetProtectionErrors.TargetNotFound);
            }

            employee.Terminate();
            employeeRepository.Delete(employee);
            await employeeRepository.SaveChangesAsync(transactionCancellationToken);

            var identityResult = await identityAccountStore.DeleteAsync(
                request.EmployeeId,
                transactionCancellationToken);
            if (!identityResult.IsSuccess || !identityResult.Value)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Failure(identityResult.Errors?.ToArray() ?? ["账号销毁失败"]);
            }

            await sessionRevocationService.RevokeAllAsync(
                request.EmployeeId,
                "employee-terminated",
                transactionCancellationToken);
            commitAttempted = true;
            await unitOfWork.CommitAsync(transactionCancellationToken);

            return Result.Success();
        }

        async Task<Result> ResolveCommitAsync()
        {
            var observation =
                await EmployeeWriteCommitRecovery.TryObserveCommitAsync(
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

            if (EmployeeWriteCommitRecovery.IsTerminationTarget(observation))
            {
                return Result.Success();
            }

            throw new EmployeeWriteConflictException();
        }
    }
}
