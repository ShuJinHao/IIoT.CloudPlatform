using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.QueryServices;

public sealed class DeviceReadQueryService(IIoTDbContext dbContext) : IDeviceReadQueryService
{
    public async Task<IReadOnlyList<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> deviceIds,
        CancellationToken cancellationToken = default)
    {
        if (deviceIds.Count == 0)
        {
            return [];
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Batch device validation requires an active transaction.");
        }

        var distinctDeviceIds = deviceIds.Distinct().ToArray();
        if (dbContext.Database.IsNpgsql())
        {
            return await dbContext.Database
                .SqlQuery<Guid>($"""
                    SELECT id AS "Value"
                    FROM devices
                    WHERE id = ANY ({distinctDeviceIds})
                    FOR KEY SHARE
                    """)
                .ToListAsync(cancellationToken);
        }

        return await dbContext.Devices
            .AsNoTracking()
            .Where(device => distinctDeviceIds.Contains(device.Id))
            .Select(device => device.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Devices
            .AsNoTracking()
            .AnyAsync(device => device.Id == deviceId, cancellationToken);
    }

    public Task<bool> ExistsInProcessAsync(
        Guid deviceId,
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Devices
            .AsNoTracking()
            .AnyAsync(
                device => device.Id == deviceId && device.ProcessId == processId,
                cancellationToken);
    }

    public Task<bool> CodeExistsAsync(
        string code,
        Guid? excludingDeviceId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();
        var query = dbContext.Devices
            .AsNoTracking()
            .Where(device => device.Code == normalizedCode);

        if (excludingDeviceId.HasValue)
        {
            query = query.Where(device => device.Id != excludingDeviceId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }

    public Task<bool> NameExistsAsync(
        string name,
        Guid? excludingDeviceId = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = name.Trim();
        var query = dbContext.Devices
            .AsNoTracking()
            .Where(device => device.DeviceName == normalizedName);

        if (excludingDeviceId.HasValue)
        {
            query = query.Where(device => device.Id != excludingDeviceId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }
}
