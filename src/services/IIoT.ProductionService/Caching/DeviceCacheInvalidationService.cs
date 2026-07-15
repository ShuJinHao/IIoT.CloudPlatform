using IIoT.Services.Contracts.Caching;
using IIoT.Services.CrossCutting.Caching;

namespace IIoT.ProductionService.Caching;

public sealed class DeviceCacheInvalidationService(
    IIdempotentCacheInvalidationService idempotentInvalidation) : IDeviceCacheInvalidationService
{
    public async Task InvalidateListsAfterRegisterOnceAsync(
        Guid domainEventId,
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        await idempotentInvalidation.InvalidateOnceAsync(
            domainEventId,
            "device-register",
            [CacheKeys.AllDevices(), CacheKeys.DevicesByProcess(processId)],
            [],
            cancellationToken);
    }

    public async Task InvalidateAfterRenameOnceAsync(
        Guid domainEventId,
        DeviceCacheDescriptor device,
        CancellationToken cancellationToken = default)
    {
        await idempotentInvalidation.InvalidateOnceAsync(
            domainEventId,
            "device-rename",
            [CacheKeys.AllDevices(), CacheKeys.DevicesByProcess(device.ProcessId)],
            [],
            cancellationToken);
    }

    public async Task InvalidateAfterDeleteOnceAsync(
        Guid domainEventId,
        DeviceCacheDescriptor device,
        CancellationToken cancellationToken = default)
    {
        await idempotentInvalidation.InvalidateOnceAsync(
            domainEventId,
            "device-delete",
            [
                CacheKeys.AllDevices(),
                CacheKeys.DevicesByProcess(device.ProcessId),
                CacheKeys.RecipesByDevice(device.DeviceId)
            ],
            [
                CacheKeys.CapacityHourlyPattern(device.DeviceId),
                CacheKeys.CapacitySummaryPattern(device.DeviceId),
                CacheKeys.CapacityRangePattern(device.DeviceId),
                CacheKeys.CapacityPagedByDevicePattern(device.DeviceId)
            ],
            cancellationToken);
    }
}
