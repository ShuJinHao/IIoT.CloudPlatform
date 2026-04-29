using IIoT.Services.Contracts;
using MassTransit;

namespace IIoT.EventBus;

public sealed class MassTransitEventPublisher(IPublishEndpoint publishEndpoint) : IEventPublisher
{
    public Task PublishAsync(
        IIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return publishEndpoint.Publish((object)@event, cancellationToken);
    }

    public Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent
        => publishEndpoint.Publish(@event, cancellationToken);
}
