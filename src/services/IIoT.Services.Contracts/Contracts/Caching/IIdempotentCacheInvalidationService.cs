namespace IIoT.Services.Contracts.Caching;

/// <summary>
/// Executes a cache invalidation once for a stable persisted operation id.
/// Completion is stored in the cache authority itself, outside the caller's database transaction.
/// </summary>
public interface IIdempotentCacheInvalidationService
{
    Task<bool> InvalidateOnceAsync(
        Guid operationId,
        string operationScope,
        IReadOnlyCollection<string> keys,
        IReadOnlyCollection<string> patterns,
        CancellationToken cancellationToken = default);
}
