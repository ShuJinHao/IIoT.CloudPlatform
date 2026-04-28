namespace IIoT.Services.Contracts.Caching;

public sealed record DeviceCacheDescriptor(
    Guid DeviceId,
    Guid ProcessId,
    string DeviceCode);

public interface IDeviceCacheInvalidationService
{
    Task InvalidateListsAfterRegisterAsync(
        Guid processId,
        CancellationToken cancellationToken = default);

    Task InvalidateAfterDeleteAsync(
        DeviceCacheDescriptor device,
        CancellationToken cancellationToken = default);
}
