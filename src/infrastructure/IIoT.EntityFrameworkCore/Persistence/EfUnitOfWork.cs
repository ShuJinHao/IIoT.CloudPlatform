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
            return;
        }

        _transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
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

        try
        {
            await dbContext.FlushDomainEventsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Transaction committed but domain event dispatch failed.");
            throw;
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
        dbContext.DiscardPendingDomainEvents();
    }
}
