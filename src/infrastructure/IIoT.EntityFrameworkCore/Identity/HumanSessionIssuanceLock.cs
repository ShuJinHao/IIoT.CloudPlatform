using System.Data;
using IIoT.EntityFrameworkCore.Persistence;
using IIoT.Services.Contracts.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class HumanSessionIssuanceLock(
    IIoTDbContext dbContext,
    HumanSessionTokenExchangeProcessGate tokenExchangeProcessGate)
    : IHumanSessionIssuanceLock
{
    private readonly Func<IIoTDbContext> _createContext = dbContext.CreateFreshContext;
    private readonly bool _usesNpgsql = dbContext.Database.IsNpgsql();

    public ValueTask<IAsyncDisposable> AcquireAuthorizationAsync(
        Guid subjectId,
        CancellationToken cancellationToken = default)
        => AcquireAsync(
            (context, token) =>
                RefreshTokenSubjectTransactionLock.AcquireAsync(
                    context,
                    subjectId,
                    token),
            cancellationToken);

    public async ValueTask<IAsyncDisposable> AcquireTokenExchangeAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_usesNpgsql)
        {
            return NoopLease.Instance;
        }

        var processLease = await tokenExchangeProcessGate.EnterAsync(
            cancellationToken);
        try
        {
            var databaseLease = await AcquireAsync(
                RefreshTokenSubjectTransactionLock
                    .AcquireOidcTokenExchangeAsync,
                cancellationToken);
            return new CompositeLease(databaseLease, processLease);
        }
        catch
        {
            await processLease.DisposeAsync();
            throw;
        }
    }

    private async ValueTask<IAsyncDisposable> AcquireAsync(
        Func<IIoTDbContext, CancellationToken, Task> acquire,
        CancellationToken cancellationToken)
    {
        var context = _createContext();
        if (!context.Database.IsNpgsql())
        {
            await context.DisposeAsync();
            return NoopLease.Instance;
        }

        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            await acquire(context, cancellationToken);
            return new TransactionLease(context, transaction);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            await context.DisposeAsync();
            throw;
        }
    }

    private sealed class TransactionLease(
        IIoTDbContext context,
        IDbContextTransaction transaction) : IAsyncDisposable
    {
        private IIoTDbContext? _context = context;
        private IDbContextTransaction? _transaction = transaction;

        public async ValueTask DisposeAsync()
        {
            var currentTransaction = Interlocked.Exchange(
                ref _transaction,
                null);
            var currentContext = Interlocked.Exchange(ref _context, null);
            if (currentTransaction is not null)
            {
                await currentTransaction.DisposeAsync();
            }

            if (currentContext is not null)
            {
                await currentContext.DisposeAsync();
            }
        }
    }

    private sealed class CompositeLease(
        IAsyncDisposable databaseLease,
        IAsyncDisposable processLease) : IAsyncDisposable
    {
        private IAsyncDisposable? _databaseLease = databaseLease;
        private IAsyncDisposable? _processLease = processLease;

        public async ValueTask DisposeAsync()
        {
            var currentDatabaseLease = Interlocked.Exchange(
                ref _databaseLease,
                null);
            var currentProcessLease = Interlocked.Exchange(
                ref _processLease,
                null);
            try
            {
                if (currentDatabaseLease is not null)
                {
                    await currentDatabaseLease.DisposeAsync();
                }
            }
            finally
            {
                if (currentProcessLease is not null)
                {
                    await currentProcessLease.DisposeAsync();
                }
            }
        }
    }

    private sealed class NoopLease : IAsyncDisposable
    {
        public static NoopLease Instance { get; } = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
