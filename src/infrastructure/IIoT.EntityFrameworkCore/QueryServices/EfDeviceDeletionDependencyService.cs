using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Services.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.QueryServices;

public sealed class EfDeviceDeletionDependencyService(IIoTDbContext dbContext)
    : IDeviceDeletionDependencyQueryService
{
    public async Task<DeviceDeletionDependencies> GetDependenciesAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var impact = await GetImpactAsync(deviceId, cancellationToken);
        return new DeviceDeletionDependencies(
            impact.Recipes > 0,
            impact.Capacities > 0,
            impact.DeviceLogs > 0,
            impact.PassStations > 0);
    }

    public async Task<DeviceDeletionImpact> GetImpactAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var impact = await dbContext.Database.SqlQuery<DeviceDeletionImpactRow>($"""
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
                (
                    select count(*)::bigint
                    from edge_host_plc_runtime_states state
                    where state.device_id = {deviceId}
                ) as "EdgeHostPlcRuntimeStates"
            """)
            .SingleAsync(cancellationToken);

        return impact.ToContract();
    }

    public async Task<DeviceCascadeDeletionResult> DeleteCascadeAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var impact = await GetImpactAsync(deviceId, cancellationToken);
            var affectedEmployeeIds = await dbContext.Set<EmployeeDeviceAccess>()
                .Where(access => access.DeviceId == deviceId)
                .Select(access => access.EmployeeId)
                .Distinct()
                .ToListAsync(cancellationToken);

            await DeleteAssociatedRowsAsync(deviceId, cancellationToken);

            var device = await dbContext.Devices.SingleOrDefaultAsync(device => device.Id == deviceId, cancellationToken);
            if (device is not null)
            {
                device.MarkDeleted();
                dbContext.Devices.Remove(device);
            }

            var affectedRows = await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new DeviceCascadeDeletionResult(affectedRows > 0 || device is not null, impact, affectedEmployeeIds);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task DeleteAssociatedRowsAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            delete from edge_host_plc_runtime_states
            where device_id = {deviceId};

            delete from edge_device_client_plugin_versions
            where device_client_version_snapshot_id in (
                select id
                from edge_device_client_version_snapshots
                where device_id = {deviceId}
            );

            delete from edge_device_client_states
            where device_id = {deviceId};

            delete from edge_device_client_version_snapshots
            where device_id = {deviceId};

            delete from edge_device_runtime_heartbeats
            where device_id = {deviceId};

            delete from upload_receive_registrations
            where device_id = {deviceId};

            delete from employee_device_accesses
            where device_id = {deviceId};

            delete from refresh_token_sessions
            where "ActorType" = {IIoTClaimTypes.EdgeDeviceActor} and "SubjectId" = {deviceId};

            delete from pass_station_records
            where device_id = {deviceId};

            delete from device_logs
            where device_id = {deviceId};

            delete from hourly_capacity
            where device_id = {deviceId};

            delete from recipes
            where device_id = {deviceId};
            """, cancellationToken);
    }

    public sealed class DeviceDeletionImpactRow
    {
        public long Recipes { get; set; }

        public long Capacities { get; set; }

        public long DeviceLogs { get; set; }

        public long PassStations { get; set; }

        public long ClientStates { get; set; }

        public long ClientVersionSnapshots { get; set; }

        public long ClientPluginVersions { get; set; }

        public long RuntimeHeartbeats { get; set; }

        public long UploadReceiveRegistrations { get; set; }

        public long EmployeeDeviceAccesses { get; set; }

        public long RefreshTokenSessions { get; set; }

        public long EdgeHostPlcRuntimeStates { get; set; }

        public DeviceDeletionImpact ToContract()
        {
            return new DeviceDeletionImpact(
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
}
