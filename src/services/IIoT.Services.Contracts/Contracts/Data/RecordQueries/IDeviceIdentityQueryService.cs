using IIoT.SharedKernel.Architecture;

namespace IIoT.Services.Contracts.RecordQueries;

public sealed record DeviceIdentitySnapshot(
    Guid DeviceId,
    string Code,
    Guid? ProcessId = null,
    string? ProcessCode = null);

public interface IDeviceIdentityQueryService : IReadOnlyQueryPort
{
    Task<DeviceIdentitySnapshot?> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);
}
