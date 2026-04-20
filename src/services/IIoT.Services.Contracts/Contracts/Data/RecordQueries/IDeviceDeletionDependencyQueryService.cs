namespace IIoT.Services.Contracts.RecordQueries;

public sealed record DeviceDeletionDependencies(
    bool HasRecipes,
    bool HasCapacities,
    bool HasDeviceLogs,
    bool HasPassStations)
{
    public bool HasAnyDependency => HasRecipes || HasCapacities || HasDeviceLogs || HasPassStations;
}

public interface IDeviceDeletionDependencyQueryService
{
    Task<DeviceDeletionDependencies> GetDependenciesAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);
}
