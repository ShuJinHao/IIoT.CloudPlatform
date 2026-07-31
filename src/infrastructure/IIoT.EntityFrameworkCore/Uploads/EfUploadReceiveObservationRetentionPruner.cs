using IIoT.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Uploads;

public sealed class EfUploadReceiveObservationRetentionPruner(
    IIoTDbContext dbContext)
    : IUploadReceiveObservationRetentionPruner
{
    internal static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    internal const int CleanupBatchSize = 512;
    private readonly Func<IIoTDbContext> _createContext =
        dbContext.CreateFreshContext;

    public async Task<int> PruneExpiredAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cutoffUtc = observedAtUtc.ToUniversalTime() - Retention;
        await using var strategyContext = _createContext();
        var strategy = strategyContext.Database.CreateExecutionStrategy();
        var totalDeleted = 0;

        while (true)
        {
            var deleted = await strategy.ExecuteAsync(
                callbackToken => PruneBatchAttemptAsync(
                    cutoffUtc,
                    callbackToken),
                cancellationToken);
            totalDeleted = checked(totalDeleted + deleted);
            if (deleted < CleanupBatchSize)
            {
                return totalDeleted;
            }
        }
    }

    private async Task<int> PruneBatchAttemptAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        if (context.Database.IsNpgsql())
        {
            return await context.Database.ExecuteSqlInterpolatedAsync($"""
                with expired as
                (
                    select id
                    from upload_receive_observations
                    where seen_at_utc < {cutoffUtc}
                    order by seen_at_utc, id
                    limit {CleanupBatchSize}
                )
                delete from upload_receive_observations as observation
                using expired
                where observation.id = expired.id;
                """, cancellationToken);
        }

        var expired = (await context.UploadReceiveObservations
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Where(observation => observation.SeenAtUtc < cutoffUtc)
            .OrderBy(observation => observation.SeenAtUtc)
            .ThenBy(observation => observation.Id)
            .Take(CleanupBatchSize)
            .ToArray();
        if (expired.Length == 0)
        {
            return 0;
        }

        context.UploadReceiveObservations.RemoveRange(expired);
        await context.SaveChangesAsync(cancellationToken);
        return expired.Length;
    }
}
