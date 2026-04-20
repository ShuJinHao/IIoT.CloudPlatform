namespace IIoT.Services.Contracts.RecordQueries;

public interface IProcessReadQueryService
{
    Task<bool> ExistsAsync(
        Guid processId,
        CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(
        string processCode,
        Guid? excludingProcessId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetDeviceIdsAsync(
        Guid processId,
        CancellationToken cancellationToken = default);

    Task<bool> HasDevicesAsync(
        Guid processId,
        CancellationToken cancellationToken = default);

    Task<bool> HasRecipesAsync(
        Guid processId,
        CancellationToken cancellationToken = default);
}
