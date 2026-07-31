using System.Data;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Persistence;

public sealed class CloudWriteObservationReader(
    DbContextOptions<IIoTDbContext> options)
    : IProcessWriteObservationReader,
        IDeviceWriteObservationReader,
        IRecipeWriteObservationReader,
        IDeviceReportWriteObservationReader,
        IClientReleaseWriteObservationReader
{
    public Task<ProcessWriteObservation> ObserveProcessAsync(
        Guid processId,
        string processCode,
        CancellationToken cancellationToken)
        => ObserveConsistentAsync(
            async (context, token) =>
            {
                var target = await context.MfgProcesses
                    .AsNoTracking()
                    .Where(process => process.Id == processId)
                    .Select(process => new ProcessWriteState(
                        process.Id,
                        process.ProcessCode,
                        process.ProcessName,
                        process.RowVersion))
                    .SingleOrDefaultAsync(token);
                var codeOwnerId = await context.MfgProcesses
                    .AsNoTracking()
                    .Where(process => process.ProcessCode == processCode)
                    .Select(process => (Guid?)process.Id)
                    .SingleOrDefaultAsync(token);
                var hasDevices = await context.Devices
                    .AsNoTracking()
                    .AnyAsync(device => device.ProcessId == processId, token);
                var hasRecipes = await context.Recipes
                    .AsNoTracking()
                    .AnyAsync(recipe => recipe.ProcessId == processId, token);
                return new ProcessWriteObservation(
                    target,
                    codeOwnerId,
                    hasDevices,
                    hasRecipes);
            },
            cancellationToken);

    public Task<DeviceWriteObservation> ObserveDeviceAsync(
        Guid deviceId,
        string deviceName,
        string clientCode,
        Guid processId,
        CancellationToken cancellationToken)
        => ObserveConsistentAsync(
            async (context, token) =>
            {
                var target = await context.Devices
                    .AsNoTracking()
                    .Where(device => device.Id == deviceId)
                    .Select(device => new DeviceWriteState(
                        device.Id,
                        device.DeviceName,
                        device.Code,
                        device.ProcessId,
                        device.RowVersion))
                    .SingleOrDefaultAsync(token);
                var nameOwnerId = await context.Devices
                    .AsNoTracking()
                    .Where(device => device.DeviceName == deviceName)
                    .Select(device => (Guid?)device.Id)
                    .SingleOrDefaultAsync(token);
                var codeOwnerId = await context.Devices
                    .AsNoTracking()
                    .Where(device => device.Code == clientCode)
                    .Select(device => (Guid?)device.Id)
                    .SingleOrDefaultAsync(token);
                var processExists = await context.MfgProcesses
                    .AsNoTracking()
                    .AnyAsync(process => process.Id == processId, token);
                var impact = await ReadDeviceDeletionImpactAsync(
                    context,
                    deviceId,
                    token);
                return new DeviceWriteObservation(
                    target,
                    nameOwnerId,
                    codeOwnerId,
                    processExists,
                    impact);
            },
            cancellationToken);

    public Task<RecipeWriteObservation> ObserveRecipeAsync(
        Guid recipeId,
        Guid processId,
        Guid deviceId,
        string recipeName,
        CancellationToken cancellationToken)
        => ObserveConsistentAsync(
            async (context, token) =>
            {
                var recipes = await context.Recipes
                    .AsNoTracking()
                    .Where(recipe =>
                        recipe.ProcessId == processId
                        && recipe.DeviceId == deviceId
                        && recipe.RecipeName == recipeName)
                    .OrderBy(recipe => recipe.Id)
                    .ToListAsync(token);
                var family = recipes
                    .Select(recipe => new RecipeWriteState(
                        recipe.Id,
                        recipe.RecipeName,
                        recipe.Version,
                        recipe.ProcessId,
                        recipe.DeviceId,
                        recipe.ParametersJsonb,
                        (int)recipe.Status,
                        recipe.RowVersion))
                    .ToList();
                var target = family.SingleOrDefault(recipe => recipe.Id == recipeId);
                var processExists = await context.MfgProcesses
                    .AsNoTracking()
                    .AnyAsync(process => process.Id == processId, token);
                var deviceExistsInProcess = await context.Devices
                    .AsNoTracking()
                    .AnyAsync(
                        device =>
                            device.Id == deviceId
                            && device.ProcessId == processId,
                        token);
                return new RecipeWriteObservation(
                    target,
                    family,
                    processExists,
                    deviceExistsInProcess);
            },
            cancellationToken);

    public Task<DeviceReportWriteObservation> ObserveReportAsync(
        Guid deviceId,
        string clientCode,
        CancellationToken cancellationToken)
        => ObserveConsistentAsync(
            async (context, token) =>
            {
                var normalizedClientCode = clientCode.Trim().ToUpperInvariant();
                var identityCode = await context.Devices
                    .AsNoTracking()
                    .Where(device => device.Id == deviceId)
                    .Select(device => device.Code)
                    .SingleOrDefaultAsync(token);
                var version = await context.DeviceClientVersionSnapshots
                    .AsNoTracking()
                    .Include(snapshot => snapshot.InstalledPlugins)
                    .SingleOrDefaultAsync(
                        snapshot =>
                            snapshot.DeviceId == deviceId
                            && snapshot.ClientCode == normalizedClientCode,
                        token);
                var heartbeat = await context.EdgeDeviceRuntimeHeartbeats
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        current =>
                            current.DeviceId == deviceId
                            && current.ClientCode == normalizedClientCode,
                        token);
                var state = await context.DeviceClientStates
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        current =>
                            current.DeviceId == deviceId
                            && current.ClientCode == normalizedClientCode,
                        token);
                var plcStates = await context.EdgeHostPlcRuntimeStates
                    .AsNoTracking()
                    .Where(current =>
                        current.DeviceId == deviceId
                        && current.ClientCode == normalizedClientCode)
                    .OrderBy(current => current.PlcCode)
                    .Select(current =>
                        new EdgeHostPlcRuntimeSnapshotContent(
                            current.PlcCode,
                            current.ReportedPlcName,
                            current.IsConnected,
                            current.RuntimeStatus,
                            current.LastSeenAtUtc,
                            current.StationCode,
                            current.Protocol,
                            current.Address,
                            current.LastError))
                    .ToListAsync(token);

                return new DeviceReportWriteObservation(
                    identityCode is not null,
                    identityCode,
                    version is null
                        ? null
                        : new DeviceReportState(
                            version.ReportedAtUtc,
                            version.ReceivedAtUtc,
                            version.GetContentSha256()),
                    heartbeat is null
                        ? null
                        : new DeviceReportState(
                            heartbeat.LastHeartbeatAtUtc,
                            heartbeat.UpdatedAtUtc,
                            heartbeat.GetContentSha256()),
                    state?.PlcSnapshotReportedAtUtc is null
                    || state.PlcSnapshotReceivedAtUtc is null
                    || string.IsNullOrWhiteSpace(
                        state.PlcSnapshotContentSha256)
                        ? null
                        : new DeviceReportState(
                            state.PlcSnapshotReportedAtUtc.Value,
                            state.PlcSnapshotReceivedAtUtc.Value,
                            EdgeHostPlcRuntimeSnapshotFingerprint.Compute(
                                plcStates)));
            },
            cancellationToken);

    public Task<ClientReleaseVersionWriteState?> ObserveVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken)
        => ObserveConsistentAsync(
            async (context, token) =>
            {
                var component = await context.ClientReleaseComponents
                    .AsNoTracking()
                    .Include(current => current.Versions)
                    .ThenInclude(version => version.Artifacts)
                    .AsSingleQuery()
                    .SingleOrDefaultAsync(
                        current => current.Versions.Any(
                            version => version.Id == versionId),
                        token);
                var version = component?.Versions.SingleOrDefault(
                    current => current.Id == versionId);
                return component is null || version is null
                    ? null
                    : ClientReleaseWriteStateFingerprint.ForVersion(
                        component,
                        version);
            },
            cancellationToken);

    public Task<ClientReleaseComponentWriteState?> ObserveComponentAsync(
        Guid componentId,
        CancellationToken cancellationToken)
        => ObserveConsistentAsync(
            async (context, token) =>
            {
                var component = await context.ClientReleaseComponents
                    .AsNoTracking()
                    .Include(current => current.Versions)
                    .ThenInclude(version => version.Artifacts)
                    .AsSingleQuery()
                    .SingleOrDefaultAsync(
                        current => current.Id == componentId,
                        token);
                return component is null
                    ? null
                    : ClientReleaseWriteStateFingerprint.ForComponent(
                        component);
            },
            cancellationToken);

    public Task<IReadOnlyList<ClientReleaseVersionWriteState>>
        ObserveVersionsAsync(
            IReadOnlyCollection<Guid> versionIds,
            CancellationToken cancellationToken)
        => ObserveConsistentAsync<IReadOnlyList<
            ClientReleaseVersionWriteState>>(
            async (context, token) =>
            {
                var requested = versionIds.Distinct().ToArray();
                if (requested.Length == 0)
                {
                    return [];
                }

                var components = await context.ClientReleaseComponents
                    .AsNoTracking()
                    .Where(component => component.Versions.Any(
                        version => requested.Contains(version.Id)))
                    .Include(component => component.Versions.Where(
                        version => requested.Contains(version.Id)))
                    .ThenInclude(version => version.Artifacts)
                    .AsSingleQuery()
                    .ToListAsync(token);
                return components
                    .SelectMany(component => component.Versions.Select(
                        version =>
                            ClientReleaseWriteStateFingerprint.ForVersion(
                                component,
                                version)))
                    .OrderBy(version => version.VersionId)
                    .ToArray();
            },
            cancellationToken);

    public Task<ClientReleaseComponentDeletionWriteObservation>
        ObserveComponentDeletionAsync(
            Guid componentId,
            Guid deletionId,
            CancellationToken cancellationToken)
        => ObserveConsistentAsync(
            async (context, token) =>
            {
                var component = await context.ClientReleaseComponents
                    .AsNoTracking()
                    .Include(current => current.Versions)
                    .ThenInclude(version => version.Artifacts)
                    .AsSingleQuery()
                    .SingleOrDefaultAsync(
                        current => current.Id == componentId,
                        token);
                var deletion = await context.ClientReleaseComponentDeletions
                    .AsNoTracking()
                    .Include(current => current.Files)
                    .SingleOrDefaultAsync(
                        current => current.Id == deletionId,
                        token);
                return new ClientReleaseComponentDeletionWriteObservation(
                    component is null
                        ? null
                        : ClientReleaseWriteStateFingerprint.ForComponent(
                            component),
                    deletion is null
                        ? null
                        : ClientReleaseWriteStateFingerprint.ForDeletion(
                            deletion));
            },
            cancellationToken);

    public Task<ClientReleaseDeletionWriteState?> ObserveDeletionAsync(
        Guid deletionId,
        CancellationToken cancellationToken)
        => ObserveConsistentAsync(
            async (context, token) =>
            {
                var deletion = await context.ClientReleaseComponentDeletions
                    .AsNoTracking()
                    .Include(current => current.Files)
                    .SingleOrDefaultAsync(
                        current => current.Id == deletionId,
                        token);
                return deletion is null
                    ? null
                    : ClientReleaseWriteStateFingerprint.ForDeletion(
                        deletion);
            },
            cancellationToken);

    public Task<ClientReleaseRetentionPolicyWriteState?>
        ObserveRetentionPolicyAsync(
            CancellationToken cancellationToken)
        => ObserveConsistentAsync(
            async (context, token) =>
            {
                var policy = await context.ClientReleaseRetentionPolicies
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        current =>
                            current.Id
                            == ClientReleaseRetentionPolicy.SingletonId,
                        token);
                return policy is null
                    ? null
                    : new ClientReleaseRetentionPolicyWriteState(
                        policy.Id,
                        policy.MaxVersionsPerComponent,
                        NormalizePostgresUtc(policy.UpdatedAtUtc),
                        policy.RowVersion);
            },
            cancellationToken);

    public Task<IReadOnlyList<DeviceBootstrapWriteState>>
        ObserveDeviceBootstrapAsync(
            IReadOnlyCollection<Guid> deviceIds,
            CancellationToken cancellationToken)
        => ObserveConsistentAsync<IReadOnlyList<DeviceBootstrapWriteState>>(
            async (context, token) =>
            {
                var requested = deviceIds.Distinct().ToArray();
                if (requested.Length == 0)
                {
                    return [];
                }

                return await context.Devices
                    .AsNoTracking()
                    .Where(device => requested.Contains(device.Id))
                    .OrderBy(device => device.Id)
                    .Select(device => new DeviceBootstrapWriteState(
                        device.Id,
                        device.DeviceName,
                        device.Code,
                        device.ProcessId,
                        device.BootstrapSecretHash,
                        device.RowVersion))
                    .ToListAsync(token);
            },
            cancellationToken);

    private async Task<T> ObserveConsistentAsync<T>(
        Func<IIoTDbContext, CancellationToken, Task<T>> observeAsync,
        CancellationToken cancellationToken)
    {
        await using var context = new IIoTDbContext(options);
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async strategyCancellationToken =>
            {
                var isolationLevel = context.Database.IsNpgsql()
                    ? IsolationLevel.RepeatableRead
                    : IsolationLevel.Serializable;
                await using var snapshot =
                    await context.Database.BeginTransactionAsync(
                        isolationLevel,
                        strategyCancellationToken);
                var result = await observeAsync(
                    context,
                    strategyCancellationToken);
                await snapshot.CommitAsync(strategyCancellationToken);
                return result;
            },
            cancellationToken);
    }

    private static DateTime NormalizePostgresUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return new DateTime(
            utc.Ticks - utc.Ticks % 10,
            DateTimeKind.Utc);
    }

    private static async Task<DeviceDeletionImpact> ReadDeviceDeletionImpactAsync(
        IIoTDbContext context,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql())
        {
            return new DeviceDeletionImpact(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var row = await context.Database.SqlQuery<DeviceDeletionImpactRow>($"""
            select
                (select count(*)::bigint from recipes where device_id = {deviceId}) as "Recipes",
                (select count(*)::bigint from hourly_capacity where device_id = {deviceId}) as "Capacities",
                (select count(*)::bigint from device_logs where device_id = {deviceId}) as "DeviceLogs",
                (select count(*)::bigint from pass_station_records where device_id = {deviceId}) as "PassStations",
                (select count(*)::bigint from edge_device_client_states where device_id = {deviceId}) as "ClientStates",
                (select count(*)::bigint from edge_device_client_version_snapshots where device_id = {deviceId}) as "ClientVersionSnapshots",
                (
                    select count(*)::bigint
                    from edge_device_client_plugin_versions plugin
                    where plugin.device_client_version_snapshot_id in (
                        select snapshot.id
                        from edge_device_client_version_snapshots snapshot
                        where snapshot.device_id = {deviceId}
                    )
                ) as "ClientPluginVersions",
                (select count(*)::bigint from edge_device_runtime_heartbeats where device_id = {deviceId}) as "RuntimeHeartbeats",
                (select count(*)::bigint from upload_receive_registrations where device_id = {deviceId}) as "UploadReceiveRegistrations",
                (select count(*)::bigint from employee_device_accesses where device_id = {deviceId}) as "EmployeeDeviceAccesses",
                (
                    select count(*)::bigint
                    from refresh_token_sessions
                    where "ActorType" = {IIoTClaimTypes.EdgeDeviceActor} and "SubjectId" = {deviceId}
                ) as "RefreshTokenSessions",
                (select count(*)::bigint from edge_host_plc_runtime_states where device_id = {deviceId}) as "EdgeHostPlcRuntimeStates"
            """)
            .SingleAsync(cancellationToken);
        return row.ToContract();
    }

    private sealed class DeviceDeletionImpactRow
    {
        public long Recipes { get; init; }
        public long Capacities { get; init; }
        public long DeviceLogs { get; init; }
        public long PassStations { get; init; }
        public long ClientStates { get; init; }
        public long ClientVersionSnapshots { get; init; }
        public long ClientPluginVersions { get; init; }
        public long RuntimeHeartbeats { get; init; }
        public long UploadReceiveRegistrations { get; init; }
        public long EmployeeDeviceAccesses { get; init; }
        public long RefreshTokenSessions { get; init; }
        public long EdgeHostPlcRuntimeStates { get; init; }

        public DeviceDeletionImpact ToContract()
            => new(
                Recipes,
                Capacities,
                DeviceLogs,
                PassStations,
                ClientStates,
                ClientVersionSnapshots,
                ClientPluginVersions,
                UploadReceiveRegistrations,
                EmployeeDeviceAccesses,
                RefreshTokenSessions,
                RuntimeHeartbeats,
                EdgeHostPlcRuntimeStates);
    }
}
