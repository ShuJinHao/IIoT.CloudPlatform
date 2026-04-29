namespace IIoT.Services.Contracts.Caching;

public sealed record DeviceCacheDescriptor(
    Guid DeviceId,
    Guid ProcessId,
    string DeviceCode);

public sealed record DeviceProcessCacheDescriptor(
    Guid DeviceId,
    string DeviceCode,
    Guid OldProcessId,
    Guid NewProcessId);

public interface IDeviceCacheInvalidationService
{
    Task InvalidateListsAfterRegisterAsync(
        Guid processId,
        CancellationToken cancellationToken = default);

    Task InvalidateAfterRenameAsync(
        DeviceCacheDescriptor device,
        CancellationToken cancellationToken = default);

    Task InvalidateAfterProcessChangeAsync(
        DeviceProcessCacheDescriptor device,
        CancellationToken cancellationToken = default);

    Task InvalidateAfterDeleteAsync(
        DeviceCacheDescriptor device,
        CancellationToken cancellationToken = default);
}
