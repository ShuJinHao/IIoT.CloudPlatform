using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
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
    IUnitOfWork unitOfWork
) : ICommandHandler<UpdateEmployeeAccessCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateEmployeeAccessCommand request,
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

        return await unitOfWork.ExecuteResilientAsync(
            ExecuteTransactionAsync,
            cancellationToken);

        async Task<Result<bool>> ExecuteTransactionAsync(
            CancellationToken transactionCancellationToken)
        {
            try
            {
                await unitOfWork.BeginTransactionAsync(transactionCancellationToken);

                var employee = await employeeRepository.GetSingleOrDefaultAsync(
                    new EmployeeWithAccessesSpec(request.EmployeeId),
                    transactionCancellationToken);

                if (employee is null)
                {
                    await unitOfWork.RollbackAsync(transactionCancellationToken);
                    return Result.Failure(AdminTargetProtectionErrors.TargetNotFound);
                }

                var requestedDeviceIds = request.DeviceIds
                    .Distinct()
                    .ToArray();
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
                foreach (var id in devicesToRemove) employee.RemoveDeviceAccess(id);
                foreach (var id in devicesToAdd) employee.AddDeviceAccess(id);

                employeeRepository.Update(employee);
                await employeeRepository.SaveChangesAsync(transactionCancellationToken);
                await unitOfWork.CommitAsync(transactionCancellationToken);

                return Result.Success(true);
            }
            catch
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }
}
