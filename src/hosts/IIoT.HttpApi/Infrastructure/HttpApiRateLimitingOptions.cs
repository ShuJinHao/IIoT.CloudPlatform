using System.Threading.RateLimiting;

namespace IIoT.HttpApi.Infrastructure;

public sealed class HttpApiRateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public FixedWindowPolicyOptions Global { get; set; } = new();
    public FixedWindowPolicyOptions Login { get; set; } = new();
    public FixedWindowPolicyOptions Bootstrap { get; set; } = new();
    public TokenBucketPolicyOptions EdgeUpload { get; set; } = new();

    public void Validate()
    {
        Global.Validate($"{SectionName}:Global");
        Login.Validate($"{SectionName}:Login");
        Bootstrap.Validate($"{SectionName}:Bootstrap");
        EdgeUpload.Validate($"{SectionName}:EdgeUpload");
    }
}

public sealed class FixedWindowPolicyOptions
{
    public int PermitLimit { get; set; } = 120;
    public int QueueLimit { get; set; } = 0;
    public int WindowSeconds { get; set; } = 60;

    public void Validate(string sectionPath)
    {
        if (PermitLimit <= 0)
        {
            throw new InvalidOperationException($"{sectionPath}:PermitLimit must be greater than 0.");
        }

        if (QueueLimit < 0)
        {
            throw new InvalidOperationException($"{sectionPath}:QueueLimit cannot be negative.");
        }

        if (WindowSeconds <= 0)
        {
            throw new InvalidOperationException($"{sectionPath}:WindowSeconds must be greater than 0.");
        }
    }

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

    public void Validate(string sectionPath)
    {
        if (TokenLimit <= 0)
        {
            throw new InvalidOperationException($"{sectionPath}:TokenLimit must be greater than 0.");
        }

        if (TokensPerPeriod <= 0)
        {
            throw new InvalidOperationException($"{sectionPath}:TokensPerPeriod must be greater than 0.");
        }

        if (QueueLimit < 0)
        {
            throw new InvalidOperationException($"{sectionPath}:QueueLimit cannot be negative.");
        }

        if (ReplenishmentPeriodSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"{sectionPath}:ReplenishmentPeriodSeconds must be greater than 0.");
        }
    }

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
