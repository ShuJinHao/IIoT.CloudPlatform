using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.ClientReleases;

public sealed class EfClientReleaseComponentDeletionStore(IIoTDbContext dbContext)
    : IClientReleaseComponentDeletionStore
{
    public async Task<ClientReleaseComponentDeletion?> GetByIdAsync(
        Guid deletionId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ClientReleaseComponentDeletions
            .Include(deletion => deletion.Files)
            .SingleOrDefaultAsync(deletion => deletion.Id == deletionId, cancellationToken);
    }

    public async Task<IReadOnlyList<ClientReleaseComponentDeletion>> GetPendingAsync(
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ClientReleaseComponentDeletions
            .Include(deletion => deletion.Files)
            .OrderBy(deletion => deletion.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClientReleaseComponentDeletion>> GetByChannelAsync(
        string channel,
        CancellationToken cancellationToken = default)
    {
        var normalizedChannel = channel.Trim();
        return await dbContext.ClientReleaseComponentDeletions
            .Include(deletion => deletion.Files)
            .Where(deletion => deletion.Channel == normalizedChannel)
            .ToListAsync(cancellationToken);
    }

    public void Add(ClientReleaseComponentDeletion deletion)
    {
        dbContext.ClientReleaseComponentDeletions.Add(deletion);
    }

    public void Remove(ClientReleaseComponentDeletion deletion)
    {
        dbContext.ClientReleaseComponentDeletions.Remove(deletion);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.SaveChangesAsync(cancellationToken);
    }
}
