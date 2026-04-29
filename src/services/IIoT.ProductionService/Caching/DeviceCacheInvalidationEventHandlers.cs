using IIoT.Core.Production.Aggregates.Devices.Events;
using IIoT.Services.Contracts.Caching;
using MediatR;

namespace IIoT.ProductionService.Caching;

public sealed class DeviceRegisteredCacheInvalidationHandler(
    IDeviceCacheInvalidationService cacheInvalidationService)
    : INotificationHandler<DeviceRegisteredDomainEvent>
{
    public Task Handle(
        DeviceRegisteredDomainEvent notification,
        CancellationToken cancellationToken)
    {
        return cacheInvalidationService.InvalidateListsAfterRegisterAsync(
            notification.ProcessId,
            cancellationToken);
    }
}

public sealed class DeviceRenamedCacheInvalidationHandler(
    IDeviceCacheInvalidationService cacheInvalidationService)
    : INotificationHandler<DeviceRenamedDomainEvent>
{
    public Task Handle(
        DeviceRenamedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        return cacheInvalidationService.InvalidateAfterRenameAsync(
            new DeviceCacheDescriptor(
                notification.DeviceId,
                notification.ProcessId,
                notification.Code),
            cancellationToken);
    }
}

public sealed class DeviceProcessChangedCacheInvalidationHandler(
    IDeviceCacheInvalidationService cacheInvalidationService)
    : INotificationHandler<DeviceProcessChangedDomainEvent>
{
    public Task Handle(
        DeviceProcessChangedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        return cacheInvalidationService.InvalidateAfterProcessChangeAsync(
            new DeviceProcessCacheDescriptor(
                notification.DeviceId,
                notification.Code,
                notification.OldProcessId,
                notification.NewProcessId),
            cancellationToken);
    }
}

public sealed class DeviceDeletedCacheInvalidationHandler(
    IDeviceCacheInvalidationService cacheInvalidationService)
    : INotificationHandler<DeviceDeletedDomainEvent>
{
    public Task Handle(
        DeviceDeletedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        return cacheInvalidationService.InvalidateAfterDeleteAsync(
            new DeviceCacheDescriptor(
                notification.DeviceId,
                notification.ProcessId,
                notification.Code),
            cancellationToken);
    }
}
