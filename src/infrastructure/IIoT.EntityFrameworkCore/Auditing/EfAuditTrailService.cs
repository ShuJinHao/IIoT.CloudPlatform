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
        var idempotencyKey = NormalizeIdempotencyKey(entry.IdempotencyKey);
        entry = entry with
        {
            IdempotencyKey = idempotencyKey,
            ExecutedAtUtc = NormalizePostgresTimestamp(entry.ExecutedAtUtc)
        };
        try
        {
            await using var dbContext = new IIoTDbContext(dbContextOptions);
            if (idempotencyKey is not null)
            {
                var existing = await dbContext.AuditTrails
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        record => record.IdempotencyKey == idempotencyKey,
                        cancellationToken);
                if (existing is not null)
                {
                    return MatchesIdempotentEntry(existing, entry, idempotencyKey);
                }
            }

            dbContext.AuditTrails.Add(AuditTrailRecord.FromEntry(entry));
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException) when (idempotencyKey is not null)
        {
            // 两个实例可能同时观察到“尚不存在”并竞争插入。唯一索引负责最终仲裁；
            // 写入端收到异常后用全新 DbContext 读取胜出的记录，精确一致才视为幂等成功。
            return await VerifyExistingIdempotentEntryAsync(entry, idempotencyKey, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                PersistenceFailed,
                "Audit trail persistence failed; ErrorType={ErrorType}.",
                ex.GetType().Name);
            return false;
        }
    }

    private async Task<bool> VerifyExistingIdempotentEntryAsync(
        AuditTrailEntry entry,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var verifyContext = new IIoTDbContext(dbContextOptions);
            var existing = await verifyContext.AuditTrails
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    record => record.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            return existing is not null
                   && MatchesIdempotentEntry(existing, entry, idempotencyKey);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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

    private static bool MatchesIdempotentEntry(
        AuditTrailRecord existing,
        AuditTrailEntry candidate,
        string idempotencyKey)
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
