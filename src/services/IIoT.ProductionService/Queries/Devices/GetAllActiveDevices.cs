using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

/// <summary>
/// 下拉选择器专用的精简设备 DTO
/// </summary>
public record DeviceSelectDto(
    Guid Id,
    string DeviceName,
    string DeviceCode,
    Guid ProcessId,
    bool IsActive
);

/// <summary>
/// 交互查询：获取全量活跃设备列表 (供员工管辖权分配、配方创建等下拉选择器使用)
/// </summary>
/// <remarks>
/// 此接口不做 ABAC 数据级过滤，仅做行为权限拦截。
/// 管理员在给员工分配设备管辖权时，需要看到全部可用设备。
/// 带 Redis 缓存抗压，设备变更时由 Command 端负责缓存双杀。
/// </remarks>
[AuthorizeRequirement("Device.Read")]
public record GetAllActiveDevicesQuery() : IQuery<Result<List<DeviceSelectDto>>>;

public class GetAllActiveDevicesHandler(
    IReadRepository<Device> deviceRepository,
    ICacheService cacheService
) : IQueryHandler<GetAllActiveDevicesQuery, Result<List<DeviceSelectDto>>>
{
    private const string CacheKey = "iiot:devices:v1:all-active";

    public async Task<Result<List<DeviceSelectDto>>> Handle(GetAllActiveDevicesQuery request, CancellationToken cancellationToken)
    {
        // ==========================================
        // 🌟 读链路：缓存绝对优先 (Cache-Aside 模式)
        // ==========================================
        var cached = await cacheService.GetAsync<List<DeviceSelectDto>>(CacheKey, cancellationToken);
        if (cached != null) return Result.Success(cached);

        // 缓存未命中，使用规约图纸查库 (过滤 + 排序全部封装在 Spec 里)
        var spec = new DeviceAllActiveSpec();
        var list = await deviceRepository.GetListAsync(spec, cancellationToken);

        var dtos = list.Select(d => new DeviceSelectDto(
            d.Id, d.DeviceName, d.DeviceCode, d.ProcessId, d.IsActive
        )).ToList();

        // 回写缓存 (设备数据变动频率较低，2 小时过期)
        await cacheService.SetAsync(CacheKey, dtos, TimeSpan.FromHours(2), cancellationToken);

        return Result.Success(dtos);
    }
}