using IIoT.Services.Contracts.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.EntityFrameworkCore.Auditing;

internal sealed class EfOidcIssuanceAuditTrailService
    : IOidcIssuanceAuditTrailService
{
    private readonly Guid recordId = Guid.NewGuid();
    private readonly IIoTDbContext dbContext;
    private readonly IServiceScopeFactory? serviceScopeFactory;
    private readonly Func<IIoTDbContext>? observationContextFactory;
    private bool staged;

    public EfOidcIssuanceAuditTrailService(
        IIoTDbContext dbContext,
        IServiceScopeFactory serviceScopeFactory)
    {
        this.dbContext = dbContext;
        this.serviceScopeFactory = serviceScopeFactory;
    }

    internal EfOidcIssuanceAuditTrailService(
        IIoTDbContext dbContext)
    {
        this.dbContext = dbContext;
    }

    internal EfOidcIssuanceAuditTrailService(
        IIoTDbContext dbContext,
        Func<IIoTDbContext> observationContextFactory)
    {
        this.dbContext = dbContext;
        this.observationContextFactory =
            observationContextFactory;
    }

    public async Task StageSuccessAsync(
        AuditTrailEntry entry,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (staged)
        {
            throw new InvalidOperationException(
                "Only one OIDC issuance success audit may be staged per request.");
        }

        if (!entry.Succeeded)
        {
            throw new ArgumentException(
                "OIDC issuance audit must describe a successful issuance.",
                nameof(entry));
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "OIDC issuance audit requires the lock-owning database transaction.");
        }

        var executedAtUtc = entry.ExecutedAtUtc.Kind == DateTimeKind.Utc
            ? entry.ExecutedAtUtc
            : entry.ExecutedAtUtc.ToUniversalTime();
        entry = entry with
        {
            ExecutedAtUtc = new DateTime(
                executedAtUtc.Ticks - executedAtUtc.Ticks % 10,
                DateTimeKind.Utc)
        };
        dbContext.AuditTrails.Add(
            AuditTrailRecord.FromEntry(recordId, entry));
        await dbContext.SaveChangesAsync(cancellationToken);
        staged = true;
    }

    public async Task<bool> IsStagedSuccessCommittedAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!staged)
        {
            return false;
        }

        if (serviceScopeFactory is not null)
        {
            await using var observationScope =
                serviceScopeFactory.CreateAsyncScope();
            var observationContext =
                observationScope.ServiceProvider
                    .GetRequiredService<IIoTDbContext>();
            return await IsCommittedAsync(
                observationContext,
                cancellationToken);
        }

        if (observationContextFactory is null)
        {
            throw new InvalidOperationException(
                "A fresh OIDC issuance observation context is unavailable.");
        }

        await using var testObservationContext =
            observationContextFactory();
        return await IsCommittedAsync(
            testObservationContext,
            cancellationToken);
    }

    private Task<bool> IsCommittedAsync(
        IIoTDbContext observationContext,
        CancellationToken cancellationToken)
        => observationContext.AuditTrails
            .AsNoTracking()
            .AnyAsync(
                record => record.Id == recordId,
                cancellationToken);
}
