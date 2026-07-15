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

    Task<DeviceDeletionImpact> GetImpactAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<DeviceCascadeDeletionResult> DeleteCascadeAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);
}

public sealed record DeviceDeletionImpact(
    long Recipes,
    long Capacities,
    long DeviceLogs,
    long PassStations,
    long ClientStates,
    long ClientVersionSnapshots,
    long ClientPluginVersions,
    long UploadReceiveRegistrations,
    long EmployeeDeviceAccesses,
    long RefreshTokenSessions,
    long RuntimeHeartbeats = 0,
    long EdgeHostPlcRuntimeStates = 0)
{
    public long TotalAssociatedRows =>
        Recipes
        + Capacities
        + DeviceLogs
        + PassStations
        + ClientStates
        + ClientVersionSnapshots
        + ClientPluginVersions
        + RuntimeHeartbeats
        + UploadReceiveRegistrations
        + EmployeeDeviceAccesses
        + RefreshTokenSessions
        + EdgeHostPlcRuntimeStates;
}

public sealed record DeviceCascadeDeletionResult(
    bool DeviceDeleted,
    DeviceDeletionImpact Impact);
