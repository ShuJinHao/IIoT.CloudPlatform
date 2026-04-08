using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

/// <summary>
/// 业务指令:全量同步员工的工序级 + 机台级管辖权
/// </summary>
[AuthorizeRequirement("Employee.UpdateAccess")]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record UpdateEmployeeAccessCommand(
    Guid EmployeeId,
    List<Guid> ProcessIds,
    List<Guid> DeviceIds
) : ICommand<Result<bool>>;

public class UpdateEmployeeAccessHandler(
    IRepository<Employee> employeeRepository
) : ICommandHandler<UpdateEmployeeAccessCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateEmployeeAccessCommand request,
        CancellationToken cancellationToken)
    {
        var employee = await employeeRepository.GetSingleOrDefaultAsync(
            new EmployeeWithAccessesSpec(request.EmployeeId),
            cancellationToken);

        if (employee is null)
            return Result.Failure("未找到目标员工档案");

        // 工序管辖权差集更新
        var existingProcessIds = employee.ProcessAccesses.Select(p => p.ProcessId).ToList();
        var processesToRemove = existingProcessIds.Except(request.ProcessIds).ToList();
        var processesToAdd = request.ProcessIds.Except(existingProcessIds).ToList();

        foreach (var id in processesToRemove) employee.RemoveProcessAccess(id);
        foreach (var id in processesToAdd) employee.AddProcessAccess(id);

        // 机台管辖权差集更新
        var existingDeviceIds = employee.DeviceAccesses.Select(d => d.DeviceId).ToList();
        var devicesToRemove = existingDeviceIds.Except(request.DeviceIds).ToList();
        var devicesToAdd = request.DeviceIds.Except(existingDeviceIds).ToList();

        foreach (var id in devicesToRemove) employee.RemoveDeviceAccess(id);
        foreach (var id in devicesToAdd) employee.AddDeviceAccess(id);

        employeeRepository.Update(employee);
        await employeeRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}