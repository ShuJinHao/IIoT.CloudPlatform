using IIoT.Services.Contracts;
using MassTransit;

namespace IIoT.EventBus;

public sealed class MassTransitEventPublisher(IPublishEndpoint publishEndpoint) : IEventPublisher
{
    public Task PublishAsync(
        IIntegrationEvent @event,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentOutOfRangeException.ThrowIfEqual(messageId, Guid.Empty);
        return publishEndpoint.Publish(
            (object)@event,
            context => context.MessageId = messageId,
            cancellationToken);
    }
}
