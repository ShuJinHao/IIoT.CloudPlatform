namespace IIoT.Services.Contracts;

public interface IEventPublisher
{
    Task PublishAsync(
        IIntegrationEvent @event,
        Guid messageId,
        CancellationToken cancellationToken = default);
}
