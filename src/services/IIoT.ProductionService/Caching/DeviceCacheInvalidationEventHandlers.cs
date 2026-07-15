using IIoT.Core.Production.Aggregates.Devices.Events;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Caching;
using MediatR;

namespace IIoT.ProductionService.Caching;

public sealed class DeviceRegisteredCacheInvalidationHandler(
    IDeviceCacheInvalidationService cacheInvalidationService,
    IDomainEventDispatchContext dispatchContext)
    : INotificationHandler<DeviceRegisteredDomainEvent>
{
    public Task Handle(
        DeviceRegisteredDomainEvent notification,
        CancellationToken cancellationToken)
    {
        return cacheInvalidationService.InvalidateListsAfterRegisterOnceAsync(
            dispatchContext.MessageId,
            notification.ProcessId,
            cancellationToken);
    }
}

public sealed class DeviceRenamedCacheInvalidationHandler(
    IDeviceCacheInvalidationService cacheInvalidationService,
    IDomainEventDispatchContext dispatchContext)
    : INotificationHandler<DeviceRenamedDomainEvent>
{
    public Task Handle(
        DeviceRenamedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        return cacheInvalidationService.InvalidateAfterRenameOnceAsync(
            dispatchContext.MessageId,
            new DeviceCacheDescriptor(
                notification.DeviceId,
                notification.ProcessId),
            cancellationToken: cancellationToken);
    }
}

public sealed class DeviceDeletedCacheInvalidationHandler(
    IDeviceCacheInvalidationService cacheInvalidationService,
    IDomainEventDispatchContext dispatchContext)
    : INotificationHandler<DeviceDeletedDomainEvent>
{
    public Task Handle(
        DeviceDeletedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        return cacheInvalidationService.InvalidateAfterDeleteOnceAsync(
            dispatchContext.MessageId,
            new DeviceCacheDescriptor(
                notification.DeviceId,
                notification.ProcessId),
            cancellationToken: cancellationToken);
    }
}
