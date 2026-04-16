using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

public record DeviceIdentityDto(
    Guid Id,
    string DeviceName,
    Guid ProcessId
);

public record GetDeviceByInstanceQuery(
    string Code
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
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure("Bootstrap 查询失败：设备 Code 不能为空。");

        var cacheKey = CacheKeys.DeviceCode(code);
        var cachedDto = await cacheService.GetAsync<DeviceIdentityDto>(cacheKey, cancellationToken);
        if (cachedDto is not null)
            return Result.Success(cachedDto);

        var spec = new DeviceByCodeSpec(code);
        var device = await deviceRepository.GetSingleOrDefaultAsync(spec, cancellationToken);

        if (device is null)
            return Result.Failure($"Bootstrap 查询失败：未找到 Code 为 [{code}] 的设备。");

        var dto = new DeviceIdentityDto(
            device.Id,
            device.DeviceName,
            device.ProcessId
        );

        await cacheService.SetAsync(cacheKey, dto, TimeSpan.FromHours(2), cancellationToken);

        return Result.Success(dto);
    }
}
