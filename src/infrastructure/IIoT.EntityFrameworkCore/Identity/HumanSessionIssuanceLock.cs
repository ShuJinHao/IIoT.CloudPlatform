using System.Data;
using System.Runtime.ExceptionServices;
using IIoT.EntityFrameworkCore.Persistence;
using IIoT.Services.Contracts.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class HumanSessionIssuanceLock(
    IIoTDbContext dbContext,
    IOpenIddictEntityFrameworkCoreContext openIddictContext,
    HumanSessionIssuanceProcessGate processGate)
    : IHumanSessionIssuanceLock
{
    private readonly bool _usesNpgsql = dbContext.Database.IsNpgsql();

    internal HumanSessionIssuanceLock(
        IIoTDbContext dbContext,
        HumanSessionIssuanceProcessGate processGate)
        : this(
            dbContext,
            new OpenIddictEntityFrameworkCoreContext<IIoTDbContext>(
                dbContext),
            processGate)
    {
    }

    public Task<bool> TryExecuteAuthorizationAsync(
        Guid subjectId,
        Func<Task> operation,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            operation,
            (context, token) =>
                RefreshTokenSubjectTransactionLock.AcquireAsync(
                    context,
                    subjectId,
                    token),
            token => processGate.TryEnterAuthorizationAsync(
                subjectId,
                token),
            cancellationToken);

    public Task<bool> TryExecuteTokenExchangeAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(
            operation,
            (context, token) =>
                RefreshTokenSubjectTransactionLock
                    .AcquireOidcTokenExchangeAsync(context, token),
            processGate.TryEnterTokenExchangeAsync,
            cancellationToken);

    private async Task<bool> ExecuteAsync(
        Func<Task> operation,
        Func<IIoTDbContext, CancellationToken, Task> acquire,
        Func<CancellationToken, ValueTask<IAsyncDisposable?>>
            enterProcessGate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        var storeContext = await openIddictContext.GetDbContextAsync(
            cancellationToken);
        if (!ReferenceEquals(storeContext, dbContext))
        {
            throw new InvalidOperationException(
                "OIDC issuance lock and OpenIddict stores must use the same DbContext.");
        }

        if (!_usesNpgsql)
        {
            await operation();
            return true;
        }

        await using var processLease = await enterProcessGate(
            cancellationToken);
        if (processLease is null)
        {
            return false;
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(
                async callbackToken =>
                {
                    var protectedOperationStarted = false;
                    try
                    {
                        await using var transaction =
                            await dbContext.Database.BeginTransactionAsync(
                                IsolationLevel.ReadCommitted,
                                callbackToken);
                        await acquire(dbContext, callbackToken);
                        protectedOperationStarted = true;
                        await operation();
                        callbackToken.ThrowIfCancellationRequested();
                        await transaction.CommitAsync(callbackToken);
                    }
                    catch (Exception exception)
                        when (protectedOperationStarted
                              && exception is not
                                  ProtectedOperationException)
                    {
                        throw new ProtectedOperationException(exception);
                    }
                },
                cancellationToken);
        }
        catch (ProtectedOperationException exception)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException!).Throw();
        }

        return true;
    }

    private sealed class ProtectedOperationException(Exception innerException)
        : Exception("OIDC issuance failed after the protected operation started.", innerException)
    {
    }
}
