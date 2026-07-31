using IIoT.SharedKernel.Architecture;

namespace IIoT.Services.Contracts.RecordQueries;

public sealed record DeviceIdentitySnapshot(
    Guid DeviceId,
    string Code);

public interface IDeviceIdentityQueryService : IReadOnlyQueryPort
{
    Task<DeviceIdentitySnapshot?> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);
}
