namespace IIoT.SharedKernel.Configuration;

public sealed class PostgresOptions
{
    public const string SectionName = "Infrastructure:Postgres";

    public bool EnableRetry { get; set; }

    public int CommandTimeoutSeconds { get; set; } = 30;

    public int MaxRetryCount { get; set; } = 3;

    public int MaxRetryDelaySeconds { get; set; } = 10;

    public void Validate()
    {
        if (CommandTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Infrastructure:Postgres:CommandTimeoutSeconds must be greater than 0.");
        }

        if (MaxRetryCount < 0)
        {
            throw new InvalidOperationException("Infrastructure:Postgres:MaxRetryCount cannot be negative.");
        }

        if (EnableRetry && MaxRetryCount <= 0)
        {
            throw new InvalidOperationException(
                "Infrastructure:Postgres:MaxRetryCount must be greater than 0 when retry is enabled.");
        }

        if (MaxRetryDelaySeconds <= 0)
        {
            throw new InvalidOperationException("Infrastructure:Postgres:MaxRetryDelaySeconds must be greater than 0.");
        }
    }

    public void Validate(string? environmentName)
    {
        Validate();

        if (!EnableRetry &&
            !string.Equals(environmentName, "Testing", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Infrastructure:Postgres:EnableRetry may be false only when the environment is exactly Testing.");
        }
    }
}
