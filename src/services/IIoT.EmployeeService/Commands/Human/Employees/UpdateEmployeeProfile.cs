using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

[AuthorizeRequirement("Employee.Update")]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record UpdateEmployeeProfileCommand(
    Guid EmployeeId,
    string RealName,
    bool IsActive
) : IHumanCommand<Result<bool>>;

public class UpdateEmployeeProfileHandler(
    IRepository<Employee> employeeRepository,
    IIdentityAccountStore identityAccountStore)
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

        var employee = await employeeRepository.GetSingleOrDefaultAsync(
            new EmployeeWithAccessesSpec(request.EmployeeId),
            cancellationToken);

        if (employee is null)
        {
            return Result.Failure("未找到该员工的业务档案");
        }

        employee.Rename(employee.EmployeeNo, realName);

        if (request.IsActive)
        {
            employee.Activate();
        }
        else
        {
            employee.Deactivate();
        }

        employeeRepository.Update(employee);
        await employeeRepository.SaveChangesAsync(cancellationToken);

        var identityResult = await identityAccountStore.SetEnabledAsync(
            request.EmployeeId,
            request.IsActive,
            cancellationToken);

        if (!identityResult.IsSuccess)
        {
            return Result.Failure(identityResult.Errors?.ToArray() ?? ["员工身份账号状态同步失败"]);
        }

        return Result.Success(true);
    }
}
