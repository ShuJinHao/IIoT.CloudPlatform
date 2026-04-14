namespace IIoT.Services.Common.Contracts.RecordQueries;

public sealed record DeviceIdentitySnapshot(
    Guid DeviceId,
    string MacAddress,
    string ClientCode);

public interface IDeviceIdentityQueryService
{
    Task<DeviceIdentitySnapshot?> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);
}
