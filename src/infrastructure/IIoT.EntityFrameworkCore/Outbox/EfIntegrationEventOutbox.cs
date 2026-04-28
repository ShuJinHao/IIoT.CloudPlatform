using IIoT.Services.Contracts;

namespace IIoT.EntityFrameworkCore.Outbox;

public sealed class EfIntegrationEventOutbox(IIoTDbContext dbContext) : IIntegrationEventOutbox
{
    public async Task EnqueueAsync(
        IIntegrationEvent @event,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        dbContext.OutboxMessages.Add(OutboxMessage.FromIntegrationEvent(@event));
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
