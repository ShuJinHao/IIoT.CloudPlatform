using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

/// <summary>
/// 停用设备(软删除)。设备一旦停用,边缘端将无法从云端拉取配方。
/// </summary>
[AuthorizeRequirement("Device.Deactivate")]
public record DeactivateDeviceCommand(Guid DeviceId) : ICommand<Result<bool>>;

public class DeactivateDeviceHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IRepository<Device> deviceRepository,
    ICacheService cacheService
) : ICommandHandler<DeactivateDeviceCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeactivateDeviceCommand request,
        CancellationToken cancellationToken)
    {
        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(request.DeviceId),
            cancellationToken);

        if (device is null)
            return Result.Failure("目标设备不存在");

        // 已停用直接返回成功(幂等)
        if (!device.IsActive)
            return Result.Success(true);

        // ABAC:非 Admin 必须有该工序的管辖权
        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(userId),
                cancellationToken);

            if (employee is null)
                return Result.Failure("系统中未找到您的员工档案");

            var hasAccess = employee.ProcessAccesses.Any(pa => pa.ProcessId == device.ProcessId);
            if (!hasAccess)
                return Result.Failure("越权:您无权停用其他车间/工序的设备");
        }

        device.Deactivate();

        deviceRepository.Update(device);
        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync(
                $"iiot:device:instance:v1:{device.Instance}", cancellationToken);
            await cacheService.RemoveAsync(
                $"iiot:devices:process:v1:{device.ProcessId}", cancellationToken);
            await cacheService.RemoveAsync("iiot:devices:v1:all-active", cancellationToken);
        }

        return Result.Success(true);
    }
}