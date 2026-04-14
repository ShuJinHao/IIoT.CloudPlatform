using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Core.Production.ValueObjects;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

/// <summary>
/// 设备身份寻址 DTO,边缘端开机自证身份后取回业务 ID 等基础档案。
/// </summary>
public record DeviceIdentityDto(
    Guid Id,
    string DeviceName,
    Guid ProcessId
);

/// <summary>
/// 查询:按上位机实例联合身份(MacAddress + ClientCode)寻址设备业务档案。
/// 同一台宿主机上可承载多个上位机实例,因此 MAC 单独不足以唯一标识。
/// </summary>
public record GetDeviceByInstanceQuery(
    string MacAddress,
    string ClientCode
) : IAnonymousBootstrapQuery<Result<DeviceIdentityDto>>;

public class GetDeviceByInstanceHandler(
    IReadRepository<Device> deviceRepository,
    ICacheService cacheService
) : IQueryHandler<GetDeviceByInstanceQuery, Result<DeviceIdentityDto>>
{
    public async Task<Result<DeviceIdentityDto>> Handle(
        GetDeviceByInstanceQuery request,
        CancellationToken cancellationToken)
    {
        // 构造身份值对象 (内部做非空 + Trim + ToUpper(MAC) 归一化)
        if (!ClientInstanceId.TryCreate(request.MacAddress, request.ClientCode, out var instance))
            return Result.Failure("寻址失败:身份信息不完整(MacAddress + ClientCode 必填)");

        var cacheKey = CacheKeys.DeviceInstance(instance);

        var cachedDto = await cacheService.GetAsync<DeviceIdentityDto>(cacheKey, cancellationToken);
        if (cachedDto != null) return Result.Success(cachedDto);

        var spec = new DeviceByClientInstanceIdSpec(instance);
        var device = await deviceRepository.GetSingleOrDefaultAsync(spec, cancellationToken);

        if (device == null)
            return Result.Failure($"寻址失败:未找到实例 [{instance}] 的设备注册档案");

        var dto = new DeviceIdentityDto(
            device.Id,
            device.DeviceName,
            device.ProcessId
        );

        await cacheService.SetAsync(cacheKey, dto, TimeSpan.FromHours(2), cancellationToken);

        return Result.Success(dto);
    }
}
