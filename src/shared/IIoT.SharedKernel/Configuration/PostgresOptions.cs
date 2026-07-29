namespace IIoT.SharedKernel.Configuration;

public sealed class PostgresOptions
{
    public const string SectionName = "Infrastructure:Postgres";

    public bool EnableRetry { get; set; } = true;

    public int CommandTimeoutSeconds { get; set; } = 30;

    public int MaxRetryCount { get; set; } = 3;

    public int MaxRetryDelaySeconds { get; set; } = 10;

    public void Validate(string? environmentName = null)
    {
        if (string.Equals(
                environmentName,
                "Production",
                StringComparison.OrdinalIgnoreCase)
            && !EnableRetry)
        {
            throw new InvalidOperationException(
                "Infrastructure:Postgres:EnableRetry must be true in Production.");
        }

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
