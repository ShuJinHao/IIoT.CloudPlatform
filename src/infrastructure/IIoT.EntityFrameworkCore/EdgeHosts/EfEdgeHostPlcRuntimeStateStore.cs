using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Contracts.EdgeHosts;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.EdgeHosts;

public sealed class EfEdgeHostPlcRuntimeStateStore(IIoTDbContext dbContext) : IEdgeHostPlcRuntimeStateStore
{
    public async Task<IReadOnlyList<EdgeHostPlcRuntimeState>> GetByIdentityAsync(
        Guid deviceId,
        string clientCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedClientCode = EdgeHostPlcRuntimeState.NormalizeClientCode(clientCode);
        return await dbContext.EdgeHostPlcRuntimeStates
            .Where(state => state.DeviceId == deviceId && state.ClientCode == normalizedClientCode)
            .OrderBy(state => state.PlcCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EdgeHostPlcRuntimeState>> GetByDevicesAsync(
        IReadOnlyCollection<Guid>? deviceIds = null,
        CancellationToken cancellationToken = default)
    {
        if (deviceIds is { Count: 0 })
        {
            return [];
        }

        return await dbContext.EdgeHostPlcRuntimeStates
            .Where(state => deviceIds == null || deviceIds.Contains(state.DeviceId))
            .OrderByDescending(state => state.LastSeenAtUtc)
            .ThenBy(state => state.PlcCode)
            .ToListAsync(cancellationToken);
    }

    public void Add(EdgeHostPlcRuntimeState state)
    {
        dbContext.EdgeHostPlcRuntimeStates.Add(state);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.SaveChangesAsync(cancellationToken);
    }
}
