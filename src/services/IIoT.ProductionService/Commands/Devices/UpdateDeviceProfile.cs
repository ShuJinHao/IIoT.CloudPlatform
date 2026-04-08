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

[AuthorizeRequirement("Device.Update")]
public record UpdateDeviceProfileCommand(
    Guid DeviceId,
    string DeviceName
) : ICommand<Result<bool>>;

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
                return Result.Failure("越权:您没有该设备所属工序的管理权限");
        }

        device.Rename(deviceName);

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