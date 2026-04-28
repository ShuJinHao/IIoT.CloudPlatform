using IIoT.Services.Contracts;
using MassTransit;

namespace IIoT.EventBus;

public sealed class MassTransitEventPublisher(IPublishEndpoint publishEndpoint) : IEventPublisher
{
    public Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent
        => publishEndpoint.Publish(@event, cancellationToken);
}
