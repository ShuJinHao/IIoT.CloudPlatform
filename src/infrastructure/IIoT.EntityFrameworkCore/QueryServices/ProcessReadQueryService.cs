using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.QueryServices;

public sealed class ProcessReadQueryService(IIoTDbContext dbContext) : IProcessReadQueryService
{
    public Task<bool> ExistsAsync(
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.MfgProcesses
            .AsNoTracking()
            .AnyAsync(process => process.Id == processId, cancellationToken);
    }

    public Task<bool> CodeExistsAsync(
        string processCode,
        Guid? excludingProcessId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.MfgProcesses
            .AsNoTracking()
            .Where(process => process.ProcessCode == processCode);

        if (excludingProcessId.HasValue)
        {
            query = query.Where(process => process.Id != excludingProcessId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetDeviceIdsAsync(
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Devices
            .AsNoTracking()
            .Where(device => device.ProcessId == processId)
            .Select(device => device.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasDevicesAsync(
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Devices
            .AsNoTracking()
            .AnyAsync(device => device.ProcessId == processId, cancellationToken);
    }

    public Task<bool> HasRecipesAsync(
        Guid processId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.Recipes
            .AsNoTracking()
            .AnyAsync(recipe => recipe.ProcessId == processId, cancellationToken);
    }
}
