namespace IIoT.EventBus;

public sealed class EventBusOptions
{
    public const string SectionName = "Infrastructure:EventBus";

    public int ConcurrentMessageLimit { get; set; } = 4;

    public int PrefetchMultiplier { get; set; } = 4;

    public int MinimumPrefetchCount { get; set; } = 4;

    public int RetryLimit { get; set; } = 3;

    public int RetryInitialSeconds { get; set; } = 1;

    public int RetryIncrementSeconds { get; set; } = 2;

    public int HostStartTimeoutSeconds { get; set; } = 30;

    public int HostStopTimeoutSeconds { get; set; } = 30;

    public string EndpointPrefix { get; set; } = string.Empty;

    public EventBusConsumerOptions Consumers { get; set; } = new();

    public void Validate()
    {
        if (ConcurrentMessageLimit <= 0)
        {
            throw new InvalidOperationException("Infrastructure:EventBus:ConcurrentMessageLimit must be greater than 0.");
        }

        if (PrefetchMultiplier <= 0)
        {
            throw new InvalidOperationException("Infrastructure:EventBus:PrefetchMultiplier must be greater than 0.");
        }

        if (MinimumPrefetchCount <= 0)
        {
            throw new InvalidOperationException("Infrastructure:EventBus:MinimumPrefetchCount must be greater than 0.");
        }

        if (RetryLimit < 0)
        {
            throw new InvalidOperationException("Infrastructure:EventBus:RetryLimit cannot be negative.");
        }

        if (RetryInitialSeconds < 0)
        {
            throw new InvalidOperationException("Infrastructure:EventBus:RetryInitialSeconds cannot be negative.");
        }

        if (RetryIncrementSeconds < 0)
        {
            throw new InvalidOperationException("Infrastructure:EventBus:RetryIncrementSeconds cannot be negative.");
        }

        if (HostStartTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Infrastructure:EventBus:HostStartTimeoutSeconds must be greater than 0.");
        }

        if (HostStopTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("Infrastructure:EventBus:HostStopTimeoutSeconds must be greater than 0.");
        }

        Consumers.Validate();
    }

    public int ResolveConcurrentMessageLimit(int? configuredLimit = null)
    {
        return configuredLimit.GetValueOrDefault() > 0
            ? configuredLimit!.Value
            : ConcurrentMessageLimit;
    }

    public string ResolveEndpointName(string semanticName)
    {
        if (string.IsNullOrWhiteSpace(EndpointPrefix))
        {
            return semanticName;
        }

        return $"{EndpointPrefix.Trim()}-{semanticName}";
    }
}

public sealed class EventBusConsumerOptions
{
    public int PassStationConcurrentMessageLimit { get; set; } = 4;

    public int DeviceLogConcurrentMessageLimit { get; set; } = 3;

    public int HourlyCapacityConcurrentMessageLimit { get; set; } = 1;

    public void Validate()
    {
        if (PassStationConcurrentMessageLimit <= 0)
        {
            throw new InvalidOperationException("Infrastructure:EventBus:Consumers:PassStationConcurrentMessageLimit must be greater than 0.");
        }

        if (DeviceLogConcurrentMessageLimit <= 0)
        {
            throw new InvalidOperationException("Infrastructure:EventBus:Consumers:DeviceLogConcurrentMessageLimit must be greater than 0.");
        }

        if (HourlyCapacityConcurrentMessageLimit <= 0)
        {
            throw new InvalidOperationException("Infrastructure:EventBus:Consumers:HourlyCapacityConcurrentMessageLimit must be greater than 0.");
        }
    }
}
