using System.Text.Json.Serialization;
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
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record UpdateEmployeeProfileCommand(
    Guid EmployeeId,
    string RealName
) : IHumanCommand<Result<bool>>;

public class UpdateEmployeeProfileHandler(
    IRepository<Employee> employeeRepository,
    IUnitOfWork unitOfWork,
    IAdminTargetGuard adminTargetGuard,
    IEmployeeMutationObservationReader mutationObservationReader)
    : ICommandHandler<UpdateEmployeeProfileCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateEmployeeProfileCommand request,
        CancellationToken cancellationToken)
    {
        var realName = request.RealName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(realName))
        {
            return Result.Failure("员工姓名不能为空");
        }

        var targetResult = await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
            request.EmployeeId,
            cancellationToken);
        if (!targetResult.IsSuccess)
        {
            return Result.Failure(targetResult.Errors?.ToArray()
                ?? [AdminTargetProtectionErrors.TargetNotFound]);
        }

        EmployeeMutationObservation? baseline = null;
        uint? targetRowVersion = null;
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

        async Task<Result<bool>> ExecuteTransactionAsync(
            CancellationToken transactionCancellationToken)
        {
            await unitOfWork.BeginTransactionAsync(transactionCancellationToken);

            var current = baseline is null
                ? await mutationObservationReader.ObserveAsync(
                    request.EmployeeId,
                    transactionCancellationToken)
                : await EmployeeWriteCommitRecovery.TryObserveAsync(
                    mutationObservationReader,
                    request.EmployeeId);
            if (current is null)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                throw new EmployeeWriteCommitUnknownException();
            }

            if (baseline is null)
            {
                baseline = current;
            }
            else if (MatchesTarget(current))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Success(true);
            }
            else if (!EmployeeWriteCommitRecovery.MatchesExact(
                         current,
                         baseline))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                throw new EmployeeWriteConflictException();
            }

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(request.EmployeeId),
                transactionCancellationToken);

            if (employee is null)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Failure(AdminTargetProtectionErrors.TargetNotFound);
            }

            if (string.Equals(employee.RealName, realName, StringComparison.Ordinal))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Success(true);
            }

            employee.Rename(employee.EmployeeNo, realName);

            employeeRepository.Update(employee);
            await employeeRepository.SaveChangesAsync(transactionCancellationToken);

            targetRowVersion = employee.RowVersion;
            commitAttempted = true;
            await unitOfWork.CommitAsync(transactionCancellationToken);
            return Result.Success(true);
        }

        async Task<Result<bool>> ResolveCommitAsync()
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
                return Result.Success(true);
            }

            throw new EmployeeWriteConflictException();
        }

        bool MatchesTarget(EmployeeMutationObservation observation)
            => baseline is not null
               && targetRowVersion.HasValue
               && EmployeeWriteCommitRecovery.MatchesExact(
                   observation,
                   baseline with
                   {
                       EmployeeRealName = realName,
                       EmployeeRowVersion = targetRowVersion
                   });
    }
}
