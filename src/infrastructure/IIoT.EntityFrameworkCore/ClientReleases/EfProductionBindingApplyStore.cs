using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.EntityFrameworkCore.Auditing;
using IIoT.Services.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.ClientReleases;

internal sealed class EfProductionBindingApplyStore(
    DbContextOptions<IIoTDbContext> dbContextOptions)
    : IProductionBindingApplyStore
{
    public async Task<ProductionBindingApplyResult> TryApplyAsync(
        ProductionBindingApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var strategyContext = new IIoTDbContext(dbContextOptions);
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(
                token => ApplyAttemptAsync(request, token),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateException)
        {
            return ProductionBindingApplyResult.Conflict;
        }
        catch
        {
            return await ObserveAsync(request);
        }
    }

    private async Task<ProductionBindingApplyResult> ApplyAttemptAsync(
        ProductionBindingApplyRequest request,
        CancellationToken cancellationToken)
    {
        await using var dbContext = new IIoTDbContext(dbContextOptions);
        var existing = await dbContext.DevicePluginBindings
            .AsNoTracking()
            .SingleOrDefaultAsync(binding =>
                binding.DeviceId == request.DeviceId
                || binding.ClientReleaseComponentId == request.ComponentId,
                cancellationToken);
        if (existing is not null)
        {
            return existing.Id == request.BindingId
                   && existing.DeviceId == request.DeviceId
                   && existing.ClientReleaseComponentId == request.ComponentId
                   && existing.ProcessType == request.ProcessType
                ? ProductionBindingApplyResult.Idempotent
                : ProductionBindingApplyResult.Conflict;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            cancellationToken);
        var component = await dbContext.ClientReleaseComponents
            .SingleOrDefaultAsync(
                item => item.Id == request.ComponentId,
                cancellationToken);
        if (component is null || component.WasEverDeviceBound)
        {
            return ProductionBindingApplyResult.Conflict;
        }

        component.MarkDeviceBound();
        dbContext.DevicePluginBindings.Add(new DevicePluginBinding(
            request.DeviceId,
            request.ComponentId,
            request.ProcessType,
            request.BoundAtUtc,
            request.BindingId));
        dbContext.AuditTrails.Add(AuditTrailRecord.FromEntry(
            request.AuditRecordId,
            request.AuditEntry));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ProductionBindingApplyResult.Applied;
    }

    private async Task<ProductionBindingApplyResult> ObserveAsync(
        ProductionBindingApplyRequest request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await using var dbContext = new IIoTDbContext(dbContextOptions);
            var binding = await dbContext.DevicePluginBindings
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == request.BindingId, timeout.Token);
            var audit = await dbContext.AuditTrails
                .AsNoTracking()
                .AnyAsync(item => item.Id == request.AuditRecordId, timeout.Token);
            return binding is not null
                   && audit
                   && binding.DeviceId == request.DeviceId
                   && binding.ClientReleaseComponentId == request.ComponentId
                   && binding.ProcessType == request.ProcessType
                ? ProductionBindingApplyResult.Idempotent
                : ProductionBindingApplyResult.Unknown;
        }
        catch
        {
            return ProductionBindingApplyResult.Unknown;
        }
    }
}
