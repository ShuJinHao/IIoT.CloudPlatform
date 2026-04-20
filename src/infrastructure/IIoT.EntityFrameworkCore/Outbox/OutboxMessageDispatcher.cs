using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIoT.EntityFrameworkCore.Outbox;

public sealed class OutboxMessageDispatcher(
    IIoTDbContext dbContext,
    IMediator mediator,
    IOptions<OutboxDispatcherOptions> options,
    ILogger<OutboxMessageDispatcher> logger) : IOutboxMessageDispatcher
{
    private readonly OutboxDispatcherOptions _options = options.Value;

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        _options.Validate();

        var messages = await dbContext.OutboxMessages
            .Where(x => x.ProcessedAtUtc == null)
            .OrderBy(x => x.OccurredAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return 0;
        }

        foreach (var message in messages)
        {
            try
            {
                var domainEvent = message.DeserializeDomainEvent();
                await mediator.Publish((object)domainEvent, cancellationToken);
                message.MarkProcessed();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to dispatch outbox message {OutboxMessageId}.", message.Id);
                message.MarkFailed(ex.Message);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return messages.Count;
    }
}
