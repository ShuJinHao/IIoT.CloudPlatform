using System.Text.Json.Serialization;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Specifications;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
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
    IAdminTargetGuard adminTargetGuard)
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

        return await unitOfWork.ExecuteResilientAsync(
            ExecuteTransactionAsync,
            cancellationToken);

        async Task<Result<bool>> ExecuteTransactionAsync(
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

            if (string.Equals(employee.RealName, realName, StringComparison.Ordinal))
            {
                await unitOfWork.RollbackAsync(transactionCancellationToken);
                return Result.Success(true);
            }

            employee.Rename(employee.EmployeeNo, realName);

            employeeRepository.Update(employee);
            await employeeRepository.SaveChangesAsync(transactionCancellationToken);

            await unitOfWork.CommitAsync(transactionCancellationToken);
            return Result.Success(true);
        }
    }
}
