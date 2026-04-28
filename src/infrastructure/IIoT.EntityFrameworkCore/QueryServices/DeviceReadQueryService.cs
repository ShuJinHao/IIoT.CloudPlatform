using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.QueryServices;

public sealed class DeviceReadQueryService(IIoTDbContext dbContext) : IDeviceReadQueryService
{
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
}
