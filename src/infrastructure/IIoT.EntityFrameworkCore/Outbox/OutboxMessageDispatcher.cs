using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IIoT.Services.Contracts;

namespace IIoT.EntityFrameworkCore.Outbox;

public sealed class OutboxMessageDispatcher(
    IIoTDbContext dbContext,
    IMediator mediator,
    IEventPublisher eventPublisher,
    IOptions<OutboxDispatcherOptions> options,
    ILogger<OutboxMessageDispatcher> logger) : IOutboxMessageDispatcher
{
    private readonly OutboxDispatcherOptions _options = options.Value;

    public async Task<OutboxDispatchResult> DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        _options.Validate();

        var messages = await dbContext.OutboxMessages
            .Where(x => x.ProcessedAtUtc == null && x.AttemptCount < _options.MaxAttempts)
            .OrderBy(x => x.OccurredAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return new OutboxDispatchResult(0, 0, 0, 0, null);
        }

        var succeededCount = 0;
        var failedCount = 0;
        string? lastFailureSummary = null;

        foreach (var message in messages)
        {
            try
            {
                if (message.MessageKind == OutboxMessageKind.DomainEvent)
                {
                    var domainEvent = message.DeserializeDomainEvent();
                    await mediator.Publish((object)domainEvent, cancellationToken);
                }
                else if (message.MessageKind == OutboxMessageKind.IntegrationEvent)
                {
                    var integrationEvent = message.DeserializeIntegrationEvent();
                    await eventPublisher.PublishAsync(integrationEvent, cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Unsupported outbox message kind '{message.MessageKind}'.");
                }

                message.MarkProcessed();
                succeededCount++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to dispatch outbox message {OutboxMessageId}.", message.Id);
                message.MarkFailed(ex.Message);
                failedCount++;
                lastFailureSummary = $"outbox_id={message.Id}; error={ex.Message}";
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var pendingBacklogCount = await dbContext.OutboxMessages
            .CountAsync(x => x.ProcessedAtUtc == null && x.AttemptCount < _options.MaxAttempts, cancellationToken);

        return new OutboxDispatchResult(
            messages.Count,
            succeededCount,
            failedCount,
            pendingBacklogCount,
            lastFailureSummary);
    }
}
