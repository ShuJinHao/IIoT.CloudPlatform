using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications; // 🌟 引入刚刚定义的规约图纸
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IIoT.ProductionService.Queries.Devices;

/// <summary>
/// 纯净的寻址返回模型，绝不暴露数据库底层实体
/// </summary>
public record DeviceIdentityDto(
    Guid Id,
    string DeviceName,
    string DeviceCode,
    Guid ProcessId,
    bool IsActive
);

/// <summary>
/// 交互查询：基于 MAC 地址极速寻址 (免权白名单接口)
/// </summary>
public record GetDeviceByMacQuery(string MacAddress) : IQuery<Result<DeviceIdentityDto>>;

public class GetDeviceByMacHandler(
    IReadRepository<Device> deviceRepository, // 纯净的读仓储
    ICacheService cacheService                // 缓存抗压
) : IQueryHandler<GetDeviceByMacQuery, Result<DeviceIdentityDto>>
{
    public async Task<Result<DeviceIdentityDto>> Handle(GetDeviceByMacQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = $"iiot:device:mac:v1:{request.MacAddress}";

        // ==========================================
        // 1. 读链路第一步：极速读取 Redis 缓存
        // ==========================================
        var cachedDto = await cacheService.GetAsync<DeviceIdentityDto>(cacheKey, cancellationToken);
        if (cachedDto != null) return Result.Success(cachedDto);

        // ==========================================
        // 2. 读链路第二步：缓存未命中，使用规约模式查库
        // ==========================================

        // 🌟 见证奇迹：直接丢入图纸，底层仓储配合 SpecificationEvaluator 自动解析！
        var spec = new DeviceByMacSpec(request.MacAddress);
        var device = await deviceRepository.GetSingleOrDefaultAsync(spec, cancellationToken);

        if (device == null)
            return Result.Failure($"寻址失败：未找到物理 MAC 地址为 [{request.MacAddress}] 的设备注册档案");

        // 提取纯净 DTO
        var dto = new DeviceIdentityDto(
            device.Id,
            device.DeviceName,
            device.DeviceCode,
            device.ProcessId,
            device.IsActive
        );

        // ==========================================
        // 3. 写入缓存，防击穿 (设置 2 小时过期)
        // ==========================================
        await cacheService.SetAsync(cacheKey, dto, TimeSpan.FromHours(2), cancellationToken);

        return Result.Success(dto);
    }
}