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

        var useSkipLocked = UsesPostgresProvider();
        await using var transaction = useSkipLocked
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var messages = useSkipLocked
            ? await LoadPendingWithSkipLockedAsync(cancellationToken)
            : await LoadPendingAsync(cancellationToken);

        if (messages.Count == 0)
        {
            var emptyBacklog = await CountUnprocessedBacklogAsync(cancellationToken);
            var emptyAbandonedCount = await CountAbandonedAsync(cancellationToken);
            return new OutboxDispatchResult(0, 0, 0, emptyBacklog, emptyAbandonedCount, null);
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
                message.MarkFailed(ex.Message, _options.MaxAttempts);
                failedCount++;
                lastFailureSummary = $"outbox_id={message.Id}; error={ex.Message}";
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var pendingBacklogCount = await CountUnprocessedBacklogAsync(cancellationToken);
        var abandonedCount = await CountAbandonedAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new OutboxDispatchResult(
            messages.Count,
            succeededCount,
            failedCount,
            pendingBacklogCount,
            abandonedCount,
            lastFailureSummary);
    }

    private Task<List<OutboxMessage>> LoadPendingAsync(CancellationToken cancellationToken)
    {
        return dbContext.OutboxMessages
            .Where(x => x.ProcessedAtUtc == null
                        && x.AbandonedAtUtc == null
                        && x.AttemptCount < _options.MaxAttempts)
            .OrderBy(x => x.OccurredAtUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken);
    }

    private Task<List<OutboxMessage>> LoadPendingWithSkipLockedAsync(CancellationToken cancellationToken)
    {
        return dbContext.OutboxMessages
            .FromSqlInterpolated($"""
                                  SELECT *
                                  FROM outbox_messages
                                  WHERE processed_at_utc IS NULL
                                    AND abandoned_at_utc IS NULL
                                    AND attempt_count < {_options.MaxAttempts}
                                  ORDER BY occurred_at_utc
                                  LIMIT {_options.BatchSize}
                                  FOR UPDATE SKIP LOCKED
                                  """)
            .ToListAsync(cancellationToken);
    }

    private Task<int> CountUnprocessedBacklogAsync(CancellationToken cancellationToken)
    {
        return dbContext.OutboxMessages
            .CountAsync(x => x.ProcessedAtUtc == null, cancellationToken);
    }

    private Task<int> CountAbandonedAsync(CancellationToken cancellationToken)
    {
        return dbContext.OutboxMessages
            .CountAsync(x => x.ProcessedAtUtc == null && x.AbandonedAtUtc != null, cancellationToken);
    }

    private bool UsesPostgresProvider()
    {
        return string.Equals(
            dbContext.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.Ordinal);
    }
}
