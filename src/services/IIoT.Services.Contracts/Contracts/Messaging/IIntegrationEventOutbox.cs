namespace IIoT.Services.Contracts;

public interface IIntegrationEventOutbox
{
    Task EnqueueAsync(
        IIntegrationEvent @event,
        CancellationToken cancellationToken = default);
}
