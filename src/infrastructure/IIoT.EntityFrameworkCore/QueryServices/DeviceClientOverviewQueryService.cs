using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace IIoT.EntityFrameworkCore.QueryServices;

public sealed class DeviceClientOverviewQueryService(IIoTDbContext dbContext)
    : IDeviceClientOverviewQueryService
{
    public async Task<DeviceClientOverviewPage> SearchAsync(
        DeviceClientOverviewQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.AllowedDeviceIds is { Count: 0 })
        {
            return new DeviceClientOverviewPage([], 0);
        }

        var staleBeforeUtc = request.RuntimeHeartbeatStaleBeforeUtc.ToUniversalTime();
        var futureLimitUtc = request.RuntimeHeartbeatFutureLimitUtc.ToUniversalTime();
        var normalizedKeyword = request.Keyword?.Trim();
        var upperKeyword = normalizedKeyword?.ToUpperInvariant();
        var jsonKeyword = string.IsNullOrWhiteSpace(normalizedKeyword)
            ? null
            : JsonSerializer.Serialize(new[] { normalizedKeyword });

        var query =
            from device in dbContext.Devices.AsNoTracking()
            join state in dbContext.DeviceClientStates.AsNoTracking()
                on new { DeviceId = device.Id, ClientCode = device.Code }
                equals new { state.DeviceId, state.ClientCode }
                into stateGroup
            from state in stateGroup.DefaultIfEmpty()
            where request.AllowedDeviceIds == null || request.AllowedDeviceIds.Contains(device.Id)
            select new
            {
                DeviceId = device.Id,
                device.DeviceName,
                ClientCode = device.Code,
                CurrentVersion = state == null
                    ? null
                    : state.RuntimeHostVersion ?? state.HostVersion,
                LastRuntimeHeartbeatAtUtc = state == null
                    ? null
                    : state.LastRuntimeHeartbeatAtUtc,
                SoftwareStatusSortOrder = state == null || state.LastRuntimeHeartbeatAtUtc == null
                    ? 0
                    : state.LastRuntimeHeartbeatAtUtc > futureLimitUtc
                        ? 5
                        : state.LastRuntimeHeartbeatAtUtc < staleBeforeUtc
                            ? 1
                            : state.RuntimeStatus == "Starting"
                                ? 2
                                : state.RuntimeStatus == "Running"
                                    ? 3
                                    : state.RuntimeStatus == "Stopping" || state.RuntimeStatus == "Stopped"
                                        ? 4
                                        : 5,
                RuntimeStatus = state == null ? null : state.RuntimeStatus,
                RuntimeLocalIpAddressesJson = state == null ? null : state.RuntimeLocalIpAddressesJson,
                RuntimeRemoteIpAddress = state == null ? null : state.RuntimeRemoteIpAddress,
                VersionLocalIpAddressesJson = state == null ? null : state.VersionLocalIpAddressesJson,
                VersionRemoteIpAddress = state == null ? null : state.VersionRemoteIpAddress
            };

        if (!string.IsNullOrWhiteSpace(upperKeyword))
        {
            query = query.Where(row =>
                row.DeviceName.ToUpper().Contains(upperKeyword)
                || row.ClientCode.Contains(upperKeyword)
                || (row.CurrentVersion != null && row.CurrentVersion.ToUpper().Contains(upperKeyword))
                || (row.RuntimeStatus != null && row.RuntimeStatus.ToUpper().Contains(upperKeyword))
                || (row.RuntimeLocalIpAddressesJson != null
                    && EF.Functions.JsonContains(row.RuntimeLocalIpAddressesJson, jsonKeyword!))
                || (row.RuntimeRemoteIpAddress != null
                    && row.RuntimeRemoteIpAddress.ToUpper().Contains(upperKeyword))
                || (row.VersionLocalIpAddressesJson != null
                    && EF.Functions.JsonContains(row.VersionLocalIpAddressesJson, jsonKeyword!))
                || (row.VersionRemoteIpAddress != null
                    && row.VersionRemoteIpAddress.ToUpper().Contains(upperKeyword)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return new DeviceClientOverviewPage([], 0);
        }

        var ordered = request.SortField switch
        {
            DeviceClientOverviewSortField.SoftwareStatus => request.Descending
                ? query.OrderByDescending(row => row.SoftwareStatusSortOrder)
                : query.OrderBy(row => row.SoftwareStatusSortOrder),
            DeviceClientOverviewSortField.CurrentVersion => request.Descending
                ? query.OrderByDescending(row => row.CurrentVersion)
                : query.OrderBy(row => row.CurrentVersion),
            DeviceClientOverviewSortField.LastRuntimeHeartbeatAtUtc => request.Descending
                ? query.OrderByDescending(row => row.LastRuntimeHeartbeatAtUtc)
                : query.OrderBy(row => row.LastRuntimeHeartbeatAtUtc),
            _ => request.Descending
                ? query.OrderByDescending(row => row.DeviceName)
                : query.OrderBy(row => row.DeviceName)
        };

        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 100);
        var rows = await ordered
            .ThenBy(row => row.DeviceName)
            .ThenBy(row => row.ClientCode)
            .ThenBy(row => row.DeviceId)
            .Skip(skip)
            .Take(take)
            .Select(row => new DeviceClientOverviewDeviceRow(
                row.DeviceId,
                row.DeviceName,
                row.ClientCode))
            .ToListAsync(cancellationToken);

        return new DeviceClientOverviewPage(rows, totalCount);
    }
}
