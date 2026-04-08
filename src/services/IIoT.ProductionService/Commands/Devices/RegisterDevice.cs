using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.ValueObjects;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

/// <summary>
/// 业务指令:注册全新上位机实例。
/// 云端唯一身份 = MacAddress + ClientCode 联合,
/// 同一台宿主机可承载多个上位机实例。
/// </summary>
[AuthorizeRequirement("Device.Create")]
[DistributedLock("iiot:lock:device-register:{MacAddress}:{ClientCode}", TimeoutSeconds = 5)]
public record RegisterDeviceCommand(
    string DeviceName,
    string MacAddress,
    string ClientCode,
    Guid ProcessId
) : ICommand<Result<Guid>>;

public class RegisterDeviceHandler(
    IRepository<Device> deviceRepository,
    IRepository<IIoT.Core.Employee.Aggregates.MfgProcesses.MfgProcess> processRepository,
    ICacheService cacheService
) : ICommandHandler<RegisterDeviceCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        RegisterDeviceCommand request,
        CancellationToken cancellationToken)
    {
        // 输入归一化
        var deviceName = request.DeviceName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(deviceName))
            return Result.Failure("设备名称不能为空");
        if (request.ProcessId == Guid.Empty)
            return Result.Failure("归属工序不能为空");

        // 构造身份值对象 (内部做非空 + Trim + ToUpper(MAC) 归一化)
        if (!ClientInstanceId.TryCreate(request.MacAddress, request.ClientCode, out var instance))
            return Result.Failure("设备身份信息不完整:MacAddress 与 ClientCode 都必须提供");

        // 校验 A:归属工序必须合法存在
        var processExists = await processRepository.AnyAsync(
            p => p.Id == request.ProcessId,
            cancellationToken);

        if (!processExists)
            return Result.Failure("设备注册失败:指定的归属工序不存在");

        // 校验 B:MacAddress + ClientCode 联合身份在全厂必须唯一
        var instanceOccupied = await deviceRepository.AnyAsync(
            d => d.Instance.MacAddress == instance.MacAddress
              && d.Instance.ClientCode == instance.ClientCode,
            cancellationToken);

        if (instanceOccupied)
            return Result.Failure(
                $"设备注册失败:实例 [{instance}] 已被其他设备占用");

        var device = new Device(deviceName, instance, request.ProcessId);

        deviceRepository.Add(device);
        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync("iiot:devices:v1:all-active", cancellationToken);
            await cacheService.RemoveAsync(
                $"iiot:devices:process:v1:{device.ProcessId}", cancellationToken);
        }

        return Result.Success(device.Id);
    }
}