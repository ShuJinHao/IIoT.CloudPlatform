using System.Threading.RateLimiting;

namespace IIoT.HttpApi.Infrastructure;

public sealed class HttpApiRateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public FixedWindowPolicyOptions GeneralApi { get; set; } = new();
    public FixedWindowPolicyOptions PasswordLogin { get; set; } = new();
    public FixedWindowPolicyOptions Refresh { get; set; } = new();
    public FixedWindowPolicyOptions EdgeOperatorLogin { get; set; } = new();
    public FixedWindowPolicyOptions Bootstrap { get; set; } = new();
    public TokenBucketPolicyOptions CapacityUpload { get; set; } = new();
    public TokenBucketPolicyOptions DeviceLogUpload { get; set; } = new();
    public TokenBucketPolicyOptions PassStationUpload { get; set; } = new();

    public void Validate()
    {
        GeneralApi.Validate($"{SectionName}:GeneralApi");
        PasswordLogin.Validate($"{SectionName}:PasswordLogin");
        Refresh.Validate($"{SectionName}:Refresh");
        EdgeOperatorLogin.Validate($"{SectionName}:EdgeOperatorLogin");
        Bootstrap.Validate($"{SectionName}:Bootstrap");
        CapacityUpload.Validate($"{SectionName}:CapacityUpload");
        DeviceLogUpload.Validate($"{SectionName}:DeviceLogUpload");
        PassStationUpload.Validate($"{SectionName}:PassStationUpload");
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
