using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

[AuthorizeRequirement("Employee.Deactivate")]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record DeactivateEmployeeCommand(Guid EmployeeId) : IHumanCommand<Result>;

public class DeactivateEmployeeHandler(
    IRepository<Employee> employeeRepository,
    IIdentityAccountStore identityAccountStore)
    : ICommandHandler<DeactivateEmployeeCommand, Result>
{
    public async Task<Result> Handle(
        DeactivateEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        var employee = await employeeRepository.GetSingleOrDefaultAsync(
            new EmployeeWithAccessesSpec(request.EmployeeId),
            cancellationToken);

        if (employee is null)
        {
            return Result.Failure("未找到目标员工档案");
        }

        if (!employee.IsActive)
        {
            return Result.Success();
        }

        employee.Deactivate();

        employeeRepository.Update(employee);
        await employeeRepository.SaveChangesAsync(cancellationToken);

        var identityResult = await identityAccountStore.SetEnabledAsync(
            request.EmployeeId,
            false,
            cancellationToken);

        if (!identityResult.IsSuccess)
        {
            return Result.Failure(identityResult.Errors?.ToArray() ?? ["员工身份账号停用失败"]);
        }

        return Result.Success();
    }
}
