using IIoT.Services.Contracts.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IIoT.EntityFrameworkCore.Auditing;

internal sealed class EfAuditTrailService(
    DbContextOptions<IIoTDbContext> dbContextOptions,
    ILogger<EfAuditTrailService> logger) : IAuditTrailService
{
    internal static readonly EventId PersistenceFailed = new(4301, nameof(PersistenceFailed));

    public async Task TryWriteAsync(
        AuditTrailEntry entry,
        CancellationToken cancellationToken = default)
    {
        await TryWriteConfirmedAsync(entry, cancellationToken);
    }

    public async Task<bool> TryWriteConfirmedAsync(
        AuditTrailEntry entry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recordId = Guid.NewGuid();
        var idempotencyKey = NormalizeIdempotencyKey(entry.IdempotencyKey);
        entry = entry with
        {
            IdempotencyKey = idempotencyKey,
            ExecutedAtUtc = NormalizePostgresTimestamp(entry.ExecutedAtUtc)
        };
        bool persisted;
        try
        {
            await using var strategyContext = new IIoTDbContext(dbContextOptions);
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            persisted = await strategy.ExecuteAsync(
                callbackToken => WriteAttemptAsync(
                    recordId,
                    entry,
                    idempotencyKey,
                    callbackToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                PersistenceFailed,
                "Audit trail persistence failed; ErrorType={ErrorType}.",
                ex.GetType().Name);
            persisted = await ObserveCommitOutcomeAsync(recordId, entry, idempotencyKey);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return persisted;
    }

    private async Task<bool> WriteAttemptAsync(
        Guid recordId,
        AuditTrailEntry entry,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new IIoTDbContext(dbContextOptions);
        var existing = await FindExistingAsync(
            dbContext,
            recordId,
            idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return MatchesIdempotentEntry(existing, entry, idempotencyKey);
        }

        dbContext.AuditTrails.Add(AuditTrailRecord.FromEntry(recordId, entry));
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> ObserveCommitOutcomeAsync(
        Guid recordId,
        AuditTrailEntry entry,
        string? idempotencyKey)
    {
        using var observationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var verifyContext = new IIoTDbContext(dbContextOptions);
            var existing = await FindExistingAsync(
                verifyContext,
                recordId,
                idempotencyKey,
                observationTimeout.Token);
            return existing is not null
                   && MatchesIdempotentEntry(existing, entry, idempotencyKey);
        }
        catch (Exception ex)
        {
            logger.LogError(
                PersistenceFailed,
                "Audit trail idempotency verification failed; ErrorType={ErrorType}.",
                ex.GetType().Name);
            return false;
        }
    }

    private static async Task<AuditTrailRecord?> FindExistingAsync(
        IIoTDbContext dbContext,
        Guid recordId,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is not null)
        {
            return await dbContext.AuditTrails
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    record => record.Id == recordId
                              || record.IdempotencyKey == idempotencyKey,
                    cancellationToken);
        }

        return await dbContext.AuditTrails
            .AsNoTracking()
            .SingleOrDefaultAsync(record => record.Id == recordId, cancellationToken);
    }

    private static bool MatchesIdempotentEntry(
        AuditTrailRecord existing,
        AuditTrailEntry candidate,
        string? idempotencyKey)
        => string.Equals(existing.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)
           && existing.ActorUserId == candidate.ActorUserId
           && string.Equals(existing.ActorEmployeeNo, candidate.ActorEmployeeNo, StringComparison.Ordinal)
           && string.Equals(existing.OperationType, candidate.OperationType, StringComparison.Ordinal)
           && string.Equals(existing.TargetType, candidate.TargetType, StringComparison.Ordinal)
           && string.Equals(existing.TargetIdOrKey, candidate.TargetIdOrKey, StringComparison.Ordinal)
           && existing.ExecutedAtUtc == candidate.ExecutedAtUtc
           && existing.Succeeded == candidate.Succeeded
           && string.Equals(existing.Summary, candidate.Summary, StringComparison.Ordinal)
           && string.Equals(existing.FailureReason, candidate.FailureReason, StringComparison.Ordinal);

    private static string? NormalizeIdempotencyKey(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static DateTime NormalizePostgresTimestamp(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }
}
