namespace IIoT.Services.Contracts;

public interface IEventPublisher
{
    Task PublishAsync(
        IIntegrationEvent @event,
        CancellationToken cancellationToken = default);

    Task PublishAsync<TEvent>(
        TEvent @event,
        CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent;
}
