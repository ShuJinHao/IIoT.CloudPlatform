using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Uploads;
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
        var recipes = await dbContext.Recipes.AnyAsync(recipe => recipe.DeviceId == deviceId, cancellationToken);
        var capacities = await CountTableAsync("hourly_capacity", deviceId, cancellationToken) > 0;
        var logs = await CountTableAsync("device_logs", deviceId, cancellationToken) > 0;
        var passStations = await CountTableAsync("pass_station_records", deviceId, cancellationToken) > 0;
        return new DeviceDeletionDependencies(recipes, capacities, logs, passStations);
    }

    public async Task<DeviceDeletionImpact> GetImpactAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var recipes = await dbContext.Recipes.LongCountAsync(recipe => recipe.DeviceId == deviceId, cancellationToken);
        var capacities = await CountTableAsync("hourly_capacity", deviceId, cancellationToken);
        var deviceLogs = await CountTableAsync("device_logs", deviceId, cancellationToken);
        var passStations = await CountTableAsync("pass_station_records", deviceId, cancellationToken);
        var clientStates = await dbContext.DeviceClientStates.LongCountAsync(state => state.DeviceId == deviceId, cancellationToken);
        var clientVersionSnapshots = await dbContext.DeviceClientVersionSnapshots.LongCountAsync(snapshot => snapshot.DeviceId == deviceId, cancellationToken);
        var clientPluginVersions = await dbContext.Set<DeviceClientPluginVersion>()
            .LongCountAsync(
                plugin => dbContext.DeviceClientVersionSnapshots
                    .Where(snapshot => snapshot.DeviceId == deviceId)
                    .Select(snapshot => snapshot.Id)
                    .Contains(plugin.DeviceClientVersionSnapshotId),
                cancellationToken);
        var runtimeHeartbeats = await dbContext.EdgeDeviceRuntimeHeartbeats.LongCountAsync(heartbeat => heartbeat.DeviceId == deviceId, cancellationToken);
        var uploadReceiveRegistrations = await dbContext.UploadReceiveRegistrations.LongCountAsync(registration => registration.DeviceId == deviceId, cancellationToken);
        var employeeDeviceAccesses = await dbContext.Set<EmployeeDeviceAccess>().LongCountAsync(access => access.DeviceId == deviceId, cancellationToken);
        var refreshTokenSessions = await dbContext.RefreshTokenSessions.LongCountAsync(
            session => session.ActorType == IIoTClaimTypes.EdgeDeviceActor && session.SubjectId == deviceId,
            cancellationToken);

        return new DeviceDeletionImpact(
            recipes,
            capacities,
            deviceLogs,
            passStations,
            clientStates,
            clientVersionSnapshots,
            clientPluginVersions,
            uploadReceiveRegistrations,
            employeeDeviceAccesses,
            refreshTokenSessions,
            runtimeHeartbeats);
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

            await dbContext.Set<DeviceClientPluginVersion>()
                .Where(plugin => dbContext.DeviceClientVersionSnapshots
                    .Where(snapshot => snapshot.DeviceId == deviceId)
                    .Select(snapshot => snapshot.Id)
                    .Contains(plugin.DeviceClientVersionSnapshotId))
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.DeviceClientStates
                .Where(state => state.DeviceId == deviceId)
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.DeviceClientVersionSnapshots
                .Where(snapshot => snapshot.DeviceId == deviceId)
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.EdgeDeviceRuntimeHeartbeats
                .Where(heartbeat => heartbeat.DeviceId == deviceId)
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.UploadReceiveRegistrations
                .Where(registration => registration.DeviceId == deviceId)
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.Set<EmployeeDeviceAccess>()
                .Where(access => access.DeviceId == deviceId)
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.RefreshTokenSessions
                .Where(session => session.ActorType == IIoTClaimTypes.EdgeDeviceActor && session.SubjectId == deviceId)
                .ExecuteDeleteAsync(cancellationToken);

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"delete from pass_station_records where device_id = {deviceId}",
                cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"delete from device_logs where device_id = {deviceId}",
                cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"delete from hourly_capacity where device_id = {deviceId}",
                cancellationToken);
            await dbContext.Recipes
                .Where(recipe => recipe.DeviceId == deviceId)
                .ExecuteDeleteAsync(cancellationToken);

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

    private async Task<long> CountTableAsync(
        string tableName,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        return tableName switch
        {
            "hourly_capacity" => await dbContext.Database
                .SqlQuery<long>($"select count(*)::bigint as \"Value\" from hourly_capacity where device_id = {deviceId}")
                .SingleAsync(cancellationToken),
            "device_logs" => await dbContext.Database
                .SqlQuery<long>($"select count(*)::bigint as \"Value\" from device_logs where device_id = {deviceId}")
                .SingleAsync(cancellationToken),
            "pass_station_records" => await dbContext.Database
                .SqlQuery<long>($"select count(*)::bigint as \"Value\" from pass_station_records where device_id = {deviceId}")
                .SingleAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(tableName), tableName, "不支持的设备级联统计表。")
        };
    }
}
