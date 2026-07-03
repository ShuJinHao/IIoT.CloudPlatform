using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.ClientReleases;

public sealed class EfDeviceClientStateStore(IIoTDbContext dbContext) : IDeviceClientStateStore
{
    public async Task<DeviceClientVersionSnapshot?> GetVersionSnapshotByDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.DeviceClientVersionSnapshots
            .Include(snapshot => snapshot.InstalledPlugins)
            .SingleOrDefaultAsync(snapshot => snapshot.DeviceId == deviceId, cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceClientVersionSnapshot>> GetVersionSnapshotsByDevicesAsync(
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.DeviceClientVersionSnapshots
            .Include(snapshot => snapshot.InstalledPlugins)
            .Where(snapshot => deviceIds == null || deviceIds.Contains(snapshot.DeviceId))
            .OrderBy(snapshot => snapshot.ClientCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<EdgeDeviceRuntimeHeartbeat?> GetRuntimeHeartbeatByIdentityAsync(
        Guid deviceId,
        string clientCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedClientCode = NormalizeClientCode(clientCode);
        return await dbContext.EdgeDeviceRuntimeHeartbeats
            .SingleOrDefaultAsync(
                heartbeat => heartbeat.DeviceId == deviceId && heartbeat.ClientCode == normalizedClientCode,
                cancellationToken);
    }

    public async Task<DeviceClientState?> GetStateByIdentityAsync(
        Guid deviceId,
        string clientCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedClientCode = NormalizeClientCode(clientCode);
        return await dbContext.DeviceClientStates
            .SingleOrDefaultAsync(
                state => state.DeviceId == deviceId && state.ClientCode == normalizedClientCode,
                cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceClientState>> GetStatesByDevicesAsync(
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.DeviceClientStates
            .Where(state => deviceIds == null || deviceIds.Contains(state.DeviceId))
            .OrderBy(state => state.ClientCode)
            .ToListAsync(cancellationToken);
    }

    public void AddVersionSnapshot(DeviceClientVersionSnapshot snapshot)
    {
        dbContext.DeviceClientVersionSnapshots.Add(snapshot);
    }

    public void AddRuntimeHeartbeat(EdgeDeviceRuntimeHeartbeat heartbeat)
    {
        dbContext.EdgeDeviceRuntimeHeartbeats.Add(heartbeat);
    }

    public void AddState(DeviceClientState state)
    {
        dbContext.DeviceClientStates.Add(state);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeClientCode(string clientCode)
    {
        return clientCode.Trim().ToUpperInvariant();
    }
}
