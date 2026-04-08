using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

/// <summary>
/// 业务指令:维护员工基础人事档案
/// </summary>
[AuthorizeRequirement("Employee.Update")]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record UpdateEmployeeProfileCommand(
    Guid EmployeeId,
    string RealName,
    bool IsActive
) : ICommand<Result<bool>>;

public class UpdateEmployeeProfileHandler(
    IRepository<Employee> employeeRepository
) : ICommandHandler<UpdateEmployeeProfileCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateEmployeeProfileCommand request,
        CancellationToken cancellationToken)
    {
        var realName = request.RealName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(realName))
            return Result.Failure("员工姓名不能为空");

        var employee = await employeeRepository.GetSingleOrDefaultAsync(
            new EmployeeWithAccessesSpec(request.EmployeeId),
            cancellationToken);

        if (employee is null)
            return Result.Failure("未找到该员工的业务档案");

        // EmployeeNo 在档案维护场景下不变更,沿用当前值
        employee.Rename(employee.EmployeeNo, realName);

        if (request.IsActive)
            employee.Activate();
        else
            employee.Deactivate();

        employeeRepository.Update(employee);
        await employeeRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}