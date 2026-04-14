using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.ValueObjects;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

[AuthorizeRequirement("Device.Create")]
[DistributedLock("iiot:lock:device-register:{MacAddress}:{ClientCode}", TimeoutSeconds = 5)]
public record RegisterDeviceCommand(
    string DeviceName,
    string MacAddress,
    string ClientCode,
    Guid ProcessId
) : IHumanCommand<Result<Guid>>;

public class RegisterDeviceHandler(
    IRepository<Device> deviceRepository,
    IDataQueryService dataQueryService,
    ICacheService cacheService
) : ICommandHandler<RegisterDeviceCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        RegisterDeviceCommand request,
        CancellationToken cancellationToken)
    {
        var deviceName = request.DeviceName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(deviceName))
            return Result.Failure("设备名称不能为空");
        if (request.ProcessId == Guid.Empty)
            return Result.Failure("归属工序不能为空");

        if (!ClientInstanceId.TryCreate(request.MacAddress, request.ClientCode, out var instance))
            return Result.Failure("设备身份信息不完整:MacAddress 与 ClientCode 都必须提供");

        var processExists = await dataQueryService.AnyAsync(
            dataQueryService.MfgProcesses.Where(p => p.Id == request.ProcessId));

        if (!processExists)
            return Result.Failure("设备注册失败:指定的归属工序不存在");

        var instanceOccupied = await dataQueryService.AnyAsync(
            dataQueryService.Devices.Where(d =>
                d.Instance.MacAddress == instance.MacAddress
             && d.Instance.ClientCode == instance.ClientCode));

        if (instanceOccupied)
            return Result.Failure($"设备注册失败:实例 [{instance}] 已被其他设备占用");

        var device = new Device(deviceName, instance, request.ProcessId);

        deviceRepository.Add(device);
        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync(CacheKeys.AllDevices(), cancellationToken);
            await cacheService.RemoveAsync(
                CacheKeys.DevicesByProcess(device.ProcessId), cancellationToken);
        }

        return Result.Success(device.Id);
    }
}
