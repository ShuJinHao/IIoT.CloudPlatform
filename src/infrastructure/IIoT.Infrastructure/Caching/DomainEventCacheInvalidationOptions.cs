namespace IIoT.Infrastructure.Caching;

public sealed class DomainEventCacheInvalidationOptions
{
    public const string SectionName = "CacheInvalidationIdempotency";

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan CompletedRetention { get; set; } = TimeSpan.FromDays(30);

    public TimeSpan ClaimRetryDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    public void Validate()
    {
        if (LeaseDuration <= TimeSpan.Zero)
            throw new InvalidOperationException("Cache invalidation lease duration must be positive.");
        if (CompletedRetention < TimeSpan.FromDays(7))
            throw new InvalidOperationException("Cache invalidation completion retention must be at least seven days.");
        if (ClaimRetryDelay <= TimeSpan.Zero || ClaimRetryDelay >= LeaseDuration)
            throw new InvalidOperationException("Cache invalidation claim retry delay must be positive and shorter than the lease.");
    }
}
