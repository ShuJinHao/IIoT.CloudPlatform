using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

/// <summary>
/// 业务指令:全量同步员工的机台管辖权
/// </summary>
[AuthorizeRequirement(CloudPermissionCatalog.Employee.UpdateAccess)]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record UpdateEmployeeAccessCommand(
    Guid EmployeeId,
    List<Guid> DeviceIds
) : IHumanCommand<Result<bool>>;

public static class EmployeeAccessErrors
{
    public const string SelectedDeviceNoLongerExists =
        "所选设备已不存在，请刷新候选列表后重试";
}

public class UpdateEmployeeAccessHandler(
    IRepository<Employee> employeeRepository,
    IAdminTargetGuard adminTargetGuard,
    IDeviceReadQueryService deviceReadQueryService,
    IUnitOfWork unitOfWork,
    IEmployeeMutationObservationReader mutationObservationReader,
    IEmployeeMutationVersionStore mutationVersionStore
) : ICommandHandler<UpdateEmployeeAccessCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateEmployeeAccessCommand request,
        CancellationToken cancellationToken)
    {
        var requestedDeviceIds = request.DeviceIds
            .Distinct()
            .OrderBy(deviceId => deviceId)
            .ToArray();
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
                return Result.Success(true);
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

            if (requestedDeviceIds.Length > 0)
            {
                var formalDeviceIds = await deviceReadQueryService.GetExistingIdsAsync(
                    requestedDeviceIds,
                    transactionCancellationToken);
                if (!formalDeviceIds.ToHashSet().SetEquals(requestedDeviceIds))
                {
                    await unitOfWork.RollbackAsync(transactionCancellationToken);
                    return Result.Failure(EmployeeAccessErrors.SelectedDeviceNoLongerExists);
                }
            }

            // 机台管辖权差集更新
            var existingDeviceIds = employee.DeviceAccesses.Select(d => d.DeviceId).ToList();
            var devicesToRemove = existingDeviceIds.Except(requestedDeviceIds).ToList();
            var devicesToAdd = requestedDeviceIds.Except(existingDeviceIds).ToList();
            if (devicesToRemove.Count == 0 && devicesToAdd.Count == 0)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Success(true);
            }

            foreach (var id in devicesToRemove) employee.RemoveDeviceAccess(id);
            foreach (var id in devicesToAdd) employee.AddDeviceAccess(id);

            employeeRepository.Update(employee);
            await employeeRepository.SaveChangesAsync(transactionCancellationToken);
            targetRowVersion = await mutationVersionStore.TryAdvanceAsync(
                request.EmployeeId,
                baseline.EmployeeRowVersion ?? employee.RowVersion,
                transactionCancellationToken);
            if (!targetRowVersion.HasValue)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                throw new EmployeeWriteConflictException();
            }

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
                       EmployeeDeviceIds = requestedDeviceIds,
                       EmployeeRowVersion = targetRowVersion
                   });
    }
}
