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
    IAdminTargetGuard adminTargetGuard)
    : ICommandHandler<ActivateEmployeeCommand, Result>
{
    public async Task<Result> Handle(
        ActivateEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        return await unitOfWork.ExecuteResilientAsync(
            ExecuteTransactionAsync,
            cancellationToken);

        async Task<Result> ExecuteTransactionAsync(
            CancellationToken transactionCancellationToken)
        {
            await unitOfWork.BeginTransactionAsync(transactionCancellationToken);

            var targetResult = await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
                request.EmployeeId,
                transactionCancellationToken);
            if (!targetResult.IsSuccess)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Failure(targetResult.Errors?.ToArray()
                    ?? [AdminTargetProtectionErrors.TargetNotFound]);
            }

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(request.EmployeeId),
                transactionCancellationToken);
            if (employee is null)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Failure(AdminTargetProtectionErrors.TargetNotFound);
            }

            if (!employee.IsActive)
            {
                employee.Activate();
                employeeRepository.Update(employee);
                await employeeRepository.SaveChangesAsync(transactionCancellationToken);
            }

            var account = await identityAccountStore.GetByIdAsync(
                request.EmployeeId,
                transactionCancellationToken);
            if (account is null)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Failure("员工身份账号启用失败");
            }

            if (!account.IsEnabled)
            {
                var identityResult = await identityAccountStore.SetEnabledAsync(
                    request.EmployeeId,
                    true,
                    transactionCancellationToken);
                if (!identityResult.IsSuccess || !identityResult.Value)
                {
                    await unitOfWork.RollbackAsync(transactionCancellationToken);
                    return Result.Failure(
                        identityResult.Errors?.ToArray() ?? ["员工身份账号启用失败"]);
                }
            }

            await sessionRevocationService.RevokeAllAsync(
                request.EmployeeId,
                "employee-activated-relogin-required",
                transactionCancellationToken);
            await unitOfWork.CommitAsync(transactionCancellationToken);

            return Result.Success();
        }
    }
}
