using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

[AuthorizeRequirement("Employee.Terminate")]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record TerminateEmployeeCommand(Guid EmployeeId) : IHumanCommand<Result>;

public class TerminateEmployeeHandler(
    IRepository<Employee> employeeRepository,
    IIdentityAccountStore identityAccountStore
) : ICommandHandler<TerminateEmployeeCommand, Result>
{
    public async Task<Result> Handle(
        TerminateEmployeeCommand request,
        CancellationToken cancellationToken)
    {
        var employee = await employeeRepository.GetSingleOrDefaultAsync(
            new EmployeeWithAccessesSpec(request.EmployeeId),
            cancellationToken);

        if (employee is not null)
        {
            employeeRepository.Delete(employee);
            await employeeRepository.SaveChangesAsync(cancellationToken);
        }

        var identityResult = await identityAccountStore.DeleteAsync(request.EmployeeId, cancellationToken);
        if (!identityResult.IsSuccess)
        {
            return Result.Failure(identityResult.Errors?.ToArray() ?? ["员工账号注销失败"]);
        }

        return Result.Success();
    }
}
