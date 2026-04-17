using IIoT.Services.Common.Contracts;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace IIoT.EntityFrameworkCore.Persistence;

public class EfUnitOfWork(
    IIoTDbContext dbContext,
    ILogger<EfUnitOfWork> logger) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            logger.LogWarning("BeginTransactionAsync was called while a transaction is already active.");
            return;
        }

        if (dbContext.HasPendingDomainEvents)
        {
            throw new InvalidOperationException(
                "Cannot begin a new transaction while previously committed domain events are still pending dispatch. Retry CommitAsync first.");
        }

        _transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            if (!dbContext.HasPendingDomainEvents)
            {
                return;
            }

            await FlushDomainEventsAsync(isRetry: true, cancellationToken);
            return;
        }

        var committed = false;
        try
        {
            await _transaction.CommitAsync(cancellationToken);
            committed = true;
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;

            if (!committed)
            {
                dbContext.DiscardPendingDomainEvents();
            }
        }

        await FlushDomainEventsAsync(isRetry: false, cancellationToken);
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            if (dbContext.HasPendingDomainEvents)
            {
                logger.LogWarning(
                    "Rollback skipped because the transaction has already committed and domain events are still pending dispatch.");
            }

            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
        dbContext.DiscardPendingDomainEvents();
    }

    private async Task FlushDomainEventsAsync(bool isRetry, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.FlushDomainEventsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            if (isRetry)
            {
                logger.LogError(ex, "Retrying pending domain event dispatch failed after the transaction had already committed.");
            }
            else
            {
                logger.LogError(ex, "Transaction committed but domain event dispatch failed.");
            }

            throw;
        }
    }
}
