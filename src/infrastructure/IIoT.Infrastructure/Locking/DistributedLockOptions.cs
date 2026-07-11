namespace IIoT.Infrastructure.Locking;

/// <summary>
/// Redis 分布式锁配置。
/// </summary>
public sealed class DistributedLockOptions
{
    public const string SectionName = "DistributedLock";

    public int LeaseSeconds { get; set; } = 120;

    public int RenewalCadenceSeconds { get; set; } = 30;

    public int OperationTimeoutMilliseconds { get; set; } = 5000;

    public int RenewalShutdownTimeoutMilliseconds { get; set; } = 5000;

    public void Validate()
    {
        if (LeaseSeconds < 5)
            throw new InvalidOperationException("DistributedLock:LeaseSeconds must be at least 5.");

        if (RenewalCadenceSeconds <= 0 || RenewalCadenceSeconds >= LeaseSeconds)
            throw new InvalidOperationException("DistributedLock:RenewalCadenceSeconds must be positive and less than LeaseSeconds.");

        if (OperationTimeoutMilliseconds <= 0
            || OperationTimeoutMilliseconds >= TimeSpan.FromSeconds(LeaseSeconds).TotalMilliseconds)
        {
            throw new InvalidOperationException(
                "DistributedLock:OperationTimeoutMilliseconds must be positive and shorter than the lease.");
        }

        if (RenewalShutdownTimeoutMilliseconds <= 0)
        {
            throw new InvalidOperationException(
                "DistributedLock:RenewalShutdownTimeoutMilliseconds must be positive.");
        }

        var leaseMilliseconds = checked(LeaseSeconds * 1000L);
        var renewalSafetyWindow = checked(
            RenewalCadenceSeconds * 1000L + OperationTimeoutMilliseconds * 2L);
        if (renewalSafetyWindow >= leaseMilliseconds)
        {
            throw new InvalidOperationException(
                "DistributedLock renewal cadence plus two operation timeouts must be shorter than the lease.");
        }

        if (RenewalShutdownTimeoutMilliseconds >= leaseMilliseconds)
        {
            throw new InvalidOperationException(
                "DistributedLock:RenewalShutdownTimeoutMilliseconds must be shorter than the lease.");
        }
    }

    public TimeSpan ResolveLeaseTtl()
    {
        return TimeSpan.FromSeconds(LeaseSeconds);
    }

    public TimeSpan ResolveRenewalCadence()
    {
        return TimeSpan.FromSeconds(RenewalCadenceSeconds);
    }

    public TimeSpan ResolveOperationTimeout()
        => TimeSpan.FromMilliseconds(OperationTimeoutMilliseconds);

    public TimeSpan ResolveRenewalShutdownTimeout()
        => TimeSpan.FromMilliseconds(RenewalShutdownTimeoutMilliseconds);
}
