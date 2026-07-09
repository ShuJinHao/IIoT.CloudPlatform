using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.QueryServices;

public sealed class EdgeHostOverviewQueryService(IIoTDbContext dbContext) : IEdgeHostOverviewQueryService
{
    public async Task<EdgeHostOverviewPage> SearchAccessibleDevicesAsync(
        IReadOnlyCollection<Guid>? allowedDeviceIds,
        string? keyword,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (allowedDeviceIds is { Count: 0 })
        {
            return new EdgeHostOverviewPage([], 0);
        }

        var normalizedKeyword = keyword?.Trim();
        var upperKeyword = normalizedKeyword?.ToUpperInvariant();
        var query = dbContext.Devices
            .AsNoTracking()
            .Where(device => allowedDeviceIds == null || allowedDeviceIds.Contains(device.Id));

        if (!string.IsNullOrWhiteSpace(upperKeyword))
        {
            query = query.Where(device =>
                device.DeviceName.ToUpper().Contains(upperKeyword)
                || device.Code.Contains(upperKeyword)
                || dbContext.EdgeHostPlcRuntimeStates.Any(state =>
                    state.DeviceId == device.Id
                    && (state.PlcCode.Contains(upperKeyword)
                        || (state.ReportedPlcName != null && state.ReportedPlcName.ToUpper().Contains(upperKeyword))
                        || (state.Protocol != null && state.Protocol.ToUpper().Contains(upperKeyword))
                        || (state.Address != null && state.Address.ToUpper().Contains(upperKeyword)))));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return new EdgeHostOverviewPage([], 0);
        }

        var rows = await query
            .OrderBy(device => device.DeviceName)
            .ThenBy(device => device.Code)
            .Skip(skip)
            .Take(take)
            .Select(device => new EdgeHostOverviewDeviceRow(
                device.Id,
                device.DeviceName,
                device.Code))
            .ToListAsync(cancellationToken);

        return new EdgeHostOverviewPage(rows, totalCount);
    }
}
