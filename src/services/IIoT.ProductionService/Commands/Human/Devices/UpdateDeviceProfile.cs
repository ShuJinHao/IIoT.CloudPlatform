using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
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
    IRepository<Device> deviceRepository,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService)
    : ICommandHandler<UpdateDeviceProfileCommand, Result<bool>>
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

        var deviceAccess = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            device.Id,
            cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return Result.Failure(deviceAccess.Errors?.ToArray() ?? ["越权:未授权访问该设备"]);
        }

        device.Rename(deviceName);

        deviceRepository.Update(device);
        await deviceRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
