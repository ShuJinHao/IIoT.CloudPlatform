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
        CancellationToken cancellationToken = default,
        uint? expectedRowVersion = null);

    Task<DeviceProcessMigrationResult> MigrateProcessAsync(
        Guid deviceId,
        Guid expectedSourceProcessId,
        Guid targetProcessId,
        uint expectedRowVersion,
        DeviceProcessMigrationAuditContext auditContext,
        CancellationToken cancellationToken = default);
}

public sealed record DeviceProcessMigrationAuditContext(
    Guid? ActorUserId,
    string? ActorEmployeeNo,
    DateTime ExecutedAtUtc);

public enum DeviceProcessMigrationStatus
{
    Migrated,
    DeviceNotFound,
    TargetProcessNotFound,
    SameProcess,
    Blocked
}

public sealed record DeviceProcessMigrationResult(
    DeviceProcessMigrationStatus Status,
    Guid DeviceId,
    Guid? SourceProcessId,
    Guid TargetProcessId,
    uint? RowVersion,
    DeviceDeletionImpact Impact)
{
    public bool Migrated => Status == DeviceProcessMigrationStatus.Migrated;
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

public sealed class DeviceDeletionCommitAttemptException(
    Exception innerException,
    DeviceDeletionImpact impact)
    : Exception(
        "Device deletion failed after the database commit attempt started.",
        innerException)
{
    public DeviceDeletionImpact Impact { get; } = impact;
}
