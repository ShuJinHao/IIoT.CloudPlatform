using MassTransit;

namespace IIoT.EventBus;

public static class RabbitMqReceiveEndpointConfiguratorExtensions
{
    public static void ApplyIIoTEndpointDefaults(
        this IRabbitMqReceiveEndpointConfigurator endpointConfigurator,
        EventBusOptions eventBusOptions,
        int? concurrentMessageLimit = null)
    {
        var normalizedLimit = eventBusOptions.ResolveConcurrentMessageLimit(concurrentMessageLimit);

        endpointConfigurator.ConcurrentMessageLimit = normalizedLimit;
        endpointConfigurator.PrefetchCount = (ushort)Math.Clamp(
            normalizedLimit * eventBusOptions.PrefetchMultiplier,
            eventBusOptions.MinimumPrefetchCount,
            ushort.MaxValue);

        if (eventBusOptions.RetryLimit > 0)
        {
            endpointConfigurator.UseMessageRetry(retry =>
                retry.Incremental(
                    eventBusOptions.RetryLimit,
                    TimeSpan.FromSeconds(eventBusOptions.RetryInitialSeconds),
                    TimeSpan.FromSeconds(eventBusOptions.RetryIncrementSeconds)));
        }
    }
}
