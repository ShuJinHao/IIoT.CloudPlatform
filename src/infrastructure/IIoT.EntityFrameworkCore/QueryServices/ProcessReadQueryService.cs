using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.QueryServices;

public sealed class ProcessReadQueryService(IIoTDbContext dbContext) : IProcessReadQueryService
{
    public async Task<(IReadOnlyList<ProcessReadItem> Items, int TotalCount)> GetPagedAsync(
        Guid? processId,
        string? keyword,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var normalizedKeyword = keyword?.Trim();
        var query = dbContext.MfgProcesses.AsNoTracking();
        if (processId.HasValue)
        {
            query = query.Where(process => process.Id == processId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            query = query.Where(process =>
                process.ProcessCode.Contains(normalizedKeyword)
                || process.ProcessName.Contains(normalizedKeyword));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(process => process.ProcessCode)
            .ThenBy(process => process.Id)
            .Skip(skip)
            .Take(take)
            .Select(process => new ProcessReadItem(
                process.Id,
                process.ProcessCode,
                process.ProcessName))
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

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
