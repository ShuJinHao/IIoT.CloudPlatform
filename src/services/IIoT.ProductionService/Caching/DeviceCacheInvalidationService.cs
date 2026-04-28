using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Caching;
using IIoT.Services.CrossCutting.Caching;

namespace IIoT.ProductionService.Caching;

public sealed class DeviceCacheInvalidationService(
    ICacheService cacheService) : IDeviceCacheInvalidationService
{
    public async Task InvalidateListsAfterRegisterAsync(
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        await cacheService.RemoveAsync(CacheKeys.AllDevices(), cancellationToken);
        await cacheService.RemoveAsync(CacheKeys.DevicesByProcess(processId), cancellationToken);
    }

    public async Task InvalidateAfterDeleteAsync(
        DeviceCacheDescriptor device,
        CancellationToken cancellationToken = default)
    {
        await cacheService.RemoveAsync(CacheKeys.AllDevices(), cancellationToken);
        await cacheService.RemoveAsync(CacheKeys.DevicesByProcess(device.ProcessId), cancellationToken);
        await cacheService.RemoveAsync(CacheKeys.DeviceCode(device.DeviceCode), cancellationToken);
        await cacheService.RemoveAsync(CacheKeys.DeviceIdentity(device.DeviceId), cancellationToken);
        await cacheService.RemoveAsync(CacheKeys.RecipesByDevice(device.DeviceId), cancellationToken);
        await cacheService.RemoveByPatternAsync(CacheKeys.CapacityHourlyPattern(device.DeviceId), cancellationToken);
        await cacheService.RemoveByPatternAsync(CacheKeys.CapacitySummaryPattern(device.DeviceId), cancellationToken);
        await cacheService.RemoveByPatternAsync(CacheKeys.CapacityRangePattern(device.DeviceId), cancellationToken);
        await cacheService.RemoveByPatternAsync(CacheKeys.CapacityPagedByDevicePattern(device.DeviceId), cancellationToken);
    }
}
