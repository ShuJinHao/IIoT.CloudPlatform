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
    IAdminTargetGuard adminTargetGuard)
    : ICommandHandler<TerminateEmployeeCommand, Result>
{
    public async Task<Result> Handle(
        TerminateEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        var deletionAttempted = false;
        return await unitOfWork.ExecuteResilientAsync(
            ExecuteTransactionAsync,
            cancellationToken);

        async Task<Result> ExecuteTransactionAsync(
            CancellationToken transactionCancellationToken)
        {
            await unitOfWork.BeginTransactionAsync(transactionCancellationToken);

            if (!deletionAttempted)
            {
                var initialTargetResult =
                    await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
                        request.EmployeeId,
                        transactionCancellationToken);
                if (!initialTargetResult.IsSuccess)
                {
                    await unitOfWork.RollbackAsync(transactionCancellationToken);
                    return Result.Failure(initialTargetResult.Errors?.ToArray()
                        ?? [AdminTargetProtectionErrors.TargetNotFound]);
                }
            }

            var account = await identityAccountStore.GetByIdAsync(
                request.EmployeeId,
                transactionCancellationToken);

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(request.EmployeeId),
                transactionCancellationToken);

            if (employee is null || account is null)
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                if (deletionAttempted && employee is null && account is null)
                {
                    return Result.Success();
                }

                return Result.Failure(AdminTargetProtectionErrors.TargetNotFound);
            }

            if (deletionAttempted)
            {
                var replayTargetResult =
                    await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
                        request.EmployeeId,
                        transactionCancellationToken);
                if (!replayTargetResult.IsSuccess)
                {
                    await unitOfWork.RollbackAsync(transactionCancellationToken);
                    return Result.Failure(replayTargetResult.Errors?.ToArray()
                        ?? [AdminTargetProtectionErrors.TargetNotFound]);
                }
            }

            deletionAttempted = true;
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
            await unitOfWork.CommitAsync(transactionCancellationToken);

            return Result.Success();
        }
    }
}
