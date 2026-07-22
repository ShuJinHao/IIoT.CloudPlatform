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
        try
        {
            await using var dbContext = new IIoTDbContext(dbContextOptions);
            dbContext.AuditTrails.Add(AuditTrailRecord.FromEntry(entry));
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
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
            return false;
        }
    }
}
