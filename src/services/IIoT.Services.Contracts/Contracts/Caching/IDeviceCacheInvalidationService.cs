namespace IIoT.Services.Contracts.Caching;

public sealed record DeviceCacheDescriptor(
    Guid DeviceId,
    Guid ProcessId);

public interface IDeviceCacheInvalidationService
{
    Task InvalidateListsAfterRegisterOnceAsync(
        Guid domainEventId,
        Guid processId,
        CancellationToken cancellationToken = default);

    Task InvalidateAfterRenameOnceAsync(
        Guid domainEventId,
        DeviceCacheDescriptor device,
        CancellationToken cancellationToken = default);

    Task InvalidateAfterDeleteOnceAsync(
        Guid domainEventId,
        DeviceCacheDescriptor device,
        CancellationToken cancellationToken = default);
}
