namespace IIoT.EntityFrameworkCore.Outbox;

public sealed class OutboxDispatcherOptions
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; set; } = 20;

    public int PollingIntervalSeconds { get; set; } = 5;

    public int MaxAttempts { get; set; } = 5;

    public void Validate()
    {
        if (BatchSize <= 0)
        {
            throw new InvalidOperationException("Outbox.BatchSize must be greater than 0.");
        }

        if (PollingIntervalSeconds <= 0)
        {
            throw new InvalidOperationException("Outbox.PollingIntervalSeconds must be greater than 0.");
        }

        if (MaxAttempts <= 0)
        {
            throw new InvalidOperationException("Outbox.MaxAttempts must be greater than 0.");
        }
    }
}
