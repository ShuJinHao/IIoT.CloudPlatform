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

        if (MaxRetryDelaySeconds <= 0)
        {
            throw new InvalidOperationException("Infrastructure:Postgres:MaxRetryDelaySeconds must be greater than 0.");
        }
    }
}
