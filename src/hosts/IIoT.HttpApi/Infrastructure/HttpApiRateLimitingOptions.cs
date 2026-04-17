using System.Threading.RateLimiting;

namespace IIoT.HttpApi.Infrastructure;

public sealed class HttpApiRateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public FixedWindowPolicyOptions Global { get; set; } = new();
    public FixedWindowPolicyOptions Login { get; set; } = new();
    public FixedWindowPolicyOptions Bootstrap { get; set; } = new();
    public TokenBucketPolicyOptions EdgeUpload { get; set; } = new();
}

public sealed class FixedWindowPolicyOptions
{
    public int PermitLimit { get; set; } = 120;
    public int QueueLimit { get; set; } = 0;
    public int WindowSeconds { get; set; } = 60;

    public FixedWindowRateLimiterOptions ToRateLimiterOptions()
    {
        return new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitLimit,
            QueueLimit = QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            Window = TimeSpan.FromSeconds(Math.Max(WindowSeconds, 1)),
            AutoReplenishment = true
        };
    }
}

public sealed class TokenBucketPolicyOptions
{
    public int TokenLimit { get; set; } = 30;
    public int TokensPerPeriod { get; set; } = 30;
    public int QueueLimit { get; set; } = 0;
    public int ReplenishmentPeriodSeconds { get; set; } = 60;

    public TokenBucketRateLimiterOptions ToRateLimiterOptions()
    {
        return new TokenBucketRateLimiterOptions
        {
            TokenLimit = TokenLimit,
            TokensPerPeriod = TokensPerPeriod,
            QueueLimit = QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            ReplenishmentPeriod = TimeSpan.FromSeconds(Math.Max(ReplenishmentPeriodSeconds, 1)),
            AutoReplenishment = true
        };
    }
}
