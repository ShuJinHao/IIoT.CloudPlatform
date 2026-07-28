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
        var targetResult = await adminTargetGuard.EnsureMutableNonAdminTargetAsync(
            request.EmployeeId,
            cancellationToken);
        if (!targetResult.IsSuccess)
        {
            return Result.Failure(targetResult.Errors?.ToArray()
                ?? [AdminTargetProtectionErrors.TargetNotFound]);
        }

        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken);

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(request.EmployeeId),
                cancellationToken);

            if (employee is null)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return Result.Failure(AdminTargetProtectionErrors.TargetNotFound);
            }

            employee.Terminate();
            employeeRepository.Delete(employee);
            await employeeRepository.SaveChangesAsync(cancellationToken);

            var identityResult = await identityAccountStore.DeleteAsync(request.EmployeeId, cancellationToken);
            if (!identityResult.IsSuccess)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return Result.Failure(identityResult.Errors?.ToArray() ?? ["账号销毁失败"]);
            }

            await sessionRevocationService.RevokeAllAsync(
                request.EmployeeId,
                "employee-terminated",
                cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return Result.Success();
        }
        catch (Exception)
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
