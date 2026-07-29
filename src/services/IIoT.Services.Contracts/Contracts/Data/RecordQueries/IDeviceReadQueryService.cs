namespace IIoT.Services.Contracts.RecordQueries;

public interface IDeviceReadQueryService
{
    /// <summary>
    /// Reads and locks the matching formal device rows for the caller's active transaction.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken cancellationToken = default);

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

    Task<bool> NameExistsAsync(
        string name,
        Guid? excludingDeviceId = null,
        CancellationToken cancellationToken = default);
}
