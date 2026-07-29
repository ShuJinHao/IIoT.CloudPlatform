using IIoT.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace IIoT.EntityFrameworkCore.Persistence;

public class EfUnitOfWork(
    IIoTDbContext dbContext,
    ILogger<EfUnitOfWork> logger) : IUnitOfWork
{
    private IDbContextTransaction? _transaction;

    public async Task<TResult> ExecuteResilientAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_transaction is not null)
        {
            throw new InvalidOperationException(
                "A resilient unit of work cannot start while a transaction is already active.");
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async strategyCancellationToken =>
        {
            try
            {
                return await operation(strategyCancellationToken);
            }
            catch
            {
                if (_transaction is not null)
                {
                    await RollbackAsync(CancellationToken.None);
                }

                // A retry must reload aggregates from the database instead of
                // reusing mutations left in the scoped DbContext by the failed attempt.
                dbContext.ChangeTracker.Clear();
                dbContext.DiscardPendingDomainEvents();
                throw;
            }
        }, cancellationToken);
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            logger.LogWarning("BeginTransactionAsync was called while a transaction is already active.");
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

        try
        {
            await _transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
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
