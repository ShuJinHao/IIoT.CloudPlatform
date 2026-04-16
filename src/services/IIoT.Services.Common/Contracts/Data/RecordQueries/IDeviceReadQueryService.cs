namespace IIoT.Services.Common.Contracts.RecordQueries;

public interface IDeviceReadQueryService
{
    Task<bool> ExistsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsInProcessAsync(
        Guid deviceId,
        Guid processId,
        CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(
        string code,
        Guid? excludingDeviceId = null,
        CancellationToken cancellationToken = default);
}
