namespace IIoT.Infrastructure.Locking;

/// <summary>
/// Redis 分布式锁配置。
/// </summary>
public sealed class DistributedLockOptions
{
    public const string SectionName = "DistributedLock";

    public int LeaseSeconds { get; set; } = 120;

    public int RenewalCadenceSeconds { get; set; } = 30;

    public TimeSpan ResolveLeaseTtl()
    {
        return TimeSpan.FromSeconds(Math.Max(LeaseSeconds, 5));
    }

    public TimeSpan ResolveRenewalCadence(TimeSpan leaseTtl)
    {
        var cadenceSeconds = RenewalCadenceSeconds <= 0
            ? Math.Max(1, (int)Math.Floor(leaseTtl.TotalSeconds / 3))
            : RenewalCadenceSeconds;

        if (cadenceSeconds >= leaseTtl.TotalSeconds)
        {
            cadenceSeconds = Math.Max(1, (int)Math.Floor(leaseTtl.TotalSeconds / 2));
        }

        return TimeSpan.FromSeconds(cadenceSeconds);
    }
}
