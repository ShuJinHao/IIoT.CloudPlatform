namespace IIoT.Services.Contracts.RecordQueries;

public sealed record ProcessReadItem(
    Guid Id,
    string ProcessCode,
    string ProcessName);

public interface IProcessReadQueryService
{
    Task<(IReadOnlyList<ProcessReadItem> Items, int TotalCount)> GetPagedAsync(
        Guid? processId,
        string? keyword,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

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
