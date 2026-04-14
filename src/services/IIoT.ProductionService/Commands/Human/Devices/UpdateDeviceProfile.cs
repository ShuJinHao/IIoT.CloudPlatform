using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

[AuthorizeRequirement("Device.Update")]
public record UpdateDeviceProfileCommand(
    Guid DeviceId,
    string DeviceName
) : IHumanCommand<Result<bool>>;

public class UpdateDeviceProfileHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IRepository<Device> deviceRepository,
    ICacheService cacheService
) : ICommandHandler<UpdateDeviceProfileCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        UpdateDeviceProfileCommand request,
        CancellationToken cancellationToken)
    {
        var deviceName = request.DeviceName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(deviceName))
            return Result.Failure("设备名称不能为空");

        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(request.DeviceId),
            cancellationToken);

        if (device is null)
            return Result.Failure("目标设备不存在");

        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(userId),
                cancellationToken);

            if (employee is null)
                return Result.Failure("系统中未找到您的员工档案");

            var hasDeviceAccess = employee.DeviceAccesses.Any(d => d.DeviceId == device.Id);
            if (!hasDeviceAccess)
                return Result.Failure("越权:您没有该设备的管辖权");
        }

        device.Rename(deviceName);

        deviceRepository.Update(device);
        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync(
                CacheKeys.DeviceInstance(device.Instance), cancellationToken);
            await cacheService.RemoveAsync(
                CacheKeys.DevicesByProcess(device.ProcessId), cancellationToken);
            await cacheService.RemoveAsync(CacheKeys.AllDevices(), cancellationToken);
            await cacheService.RemoveAsync(
                CacheKeys.DeviceIdentity(device.Id), cancellationToken);
        }

        return Result.Success(true);
    }
}
