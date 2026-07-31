namespace IIoT.Services.Contracts.Identity;

public interface IHumanSessionRevocationService
{
    Task RevokeAllAsync(
        Guid subjectId,
        string reason,
        CancellationToken cancellationToken = default);
}

public interface IIndependentHumanSessionRevocationService
{
    Task RevokeAllAsync(
        Guid subjectId,
        string reason,
        CancellationToken cancellationToken = default);
}

public interface IHumanSessionIssuanceLock
{
    ValueTask<IAsyncDisposable> AcquireAuthorizationAsync(
        Guid subjectId,
        CancellationToken cancellationToken = default);

    ValueTask<IAsyncDisposable?> TryAcquireTokenExchangeAsync(
        CancellationToken cancellationToken = default);
}
