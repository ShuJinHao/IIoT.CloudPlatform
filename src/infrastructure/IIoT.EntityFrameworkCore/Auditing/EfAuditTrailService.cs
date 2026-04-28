using IIoT.Services.Contracts.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IIoT.EntityFrameworkCore.Auditing;

internal sealed class EfAuditTrailService(
    IIoTDbContext dbContext,
    ILogger<EfAuditTrailService> logger) : IAuditTrailService
{
    public async Task TryWriteAsync(
        AuditTrailEntry entry,
        CancellationToken cancellationToken = default)
    {
        var record = AuditTrailRecord.FromEntry(entry);
        dbContext.Add(record);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            dbContext.Entry(record).State = EntityState.Detached;
            logger.LogError(
                ex,
                "Failed to write audit trail for {OperationType} on {TargetType}:{TargetIdOrKey}.",
                entry.OperationType,
                entry.TargetType,
                entry.TargetIdOrKey);
        }
    }
}
