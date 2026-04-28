namespace IIoT.Services.Contracts.RecordQueries;

public interface IRecipeReadQueryService
{
    Task<bool> VersionExistsAsync(
        Guid processId,
        Guid deviceId,
        string recipeName,
        string version,
        CancellationToken cancellationToken = default);
}
