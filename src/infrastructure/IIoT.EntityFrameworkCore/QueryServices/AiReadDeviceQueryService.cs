using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.QueryServices;

public sealed class AiReadDeviceQueryService(IIoTDbContext dbContext) : IAiReadDeviceQueryService
{
    public async Task<(IReadOnlyList<AiReadDeviceQueryItem> Items, int TotalCount)> GetPagedAsync(
        AiReadDeviceQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedDeviceCode = string.IsNullOrWhiteSpace(request.DeviceCode)
            ? null
            : request.DeviceCode.Trim().ToUpperInvariant();
        var keyword = request.Keyword?.Trim();
        var normalizedKeyword = keyword?.ToUpperInvariant();
        var allowedDeviceIds = request.AllowedDeviceIds?.ToArray();

        var query = dbContext.Devices
            .AsNoTracking()
            .Where(device => allowedDeviceIds == null || allowedDeviceIds.Contains(device.Id));

        if (request.DeviceId.HasValue)
        {
            query = query.Where(device => device.Id == request.DeviceId.Value);
        }

        if (normalizedDeviceCode is not null)
        {
            query = query.Where(device => device.Code == normalizedDeviceCode);
        }

        if (request.ProcessId.HasValue)
        {
            query = query.Where(device => device.ProcessId == request.ProcessId.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(device =>
                device.DeviceName.Contains(keyword)
                || device.Code.Contains(normalizedKeyword!));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(device => device.DeviceName)
            .ThenBy(device => device.Id)
            .Skip(request.Skip)
            .Take(request.Take)
            .Select(device => new AiReadDeviceQueryItem(
                device.Id,
                device.Code,
                device.DeviceName,
                device.ProcessId))
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
