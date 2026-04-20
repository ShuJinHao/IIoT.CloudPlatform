using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Employees.Specifications;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands.Employees;

/// <summary>
/// 业务指令:全量同步员工的机台管辖权
/// </summary>
[AuthorizeRequirement("Employee.UpdateAccess")]
[DistributedLock("iiot:lock:employee:{EmployeeId}", TimeoutSeconds = 5)]
public record UpdateEmployeeAccessCommand(
    Guid EmployeeId,
    List<Guid> DeviceIds
) : IHumanCommand<Result<bool>>;

public class UpdateEmployeeAccessHandler(
    IRepository<Employee> employeeRepository,
    ICacheService cacheService
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

        // 机台管辖权差集更新
        var existingDeviceIds = employee.DeviceAccesses.Select(d => d.DeviceId).ToList();
        var devicesToRemove = existingDeviceIds.Except(request.DeviceIds).ToList();
        var devicesToAdd = request.DeviceIds.Except(existingDeviceIds).ToList();
        var shouldClearCache = devicesToRemove.Count > 0 || devicesToAdd.Count > 0;

        foreach (var id in devicesToRemove) employee.RemoveDeviceAccess(id);
        foreach (var id in devicesToAdd) employee.AddDeviceAccess(id);

        employeeRepository.Update(employee);
        await employeeRepository.SaveChangesAsync(cancellationToken);

        if (shouldClearCache)
        {
            await cacheService.RemoveAsync(CacheKeys.DeviceAccessesByUser(request.EmployeeId), cancellationToken);
        }

        return Result.Success(true);
    }
}
