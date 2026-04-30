using IIoT.EntityFrameworkCore.Outbox;
using Microsoft.Extensions.Options;

namespace IIoT.DataWorker.Outbox;

public sealed class OutboxDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxDispatcherOptions> options,
    ILogger<OutboxDispatcherWorker> logger) : BackgroundService
{
    private readonly OutboxDispatcherOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();

        logger.LogInformation(
            "Outbox dispatcher started polling_interval_seconds={polling_interval_seconds} batch_size={batch_size} max_attempts={max_attempts}",
            _options.PollingIntervalSeconds,
            _options.BatchSize,
            _options.MaxAttempts);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollingIntervalSeconds));

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxMessageDispatcher>();
                var dispatchResult = await dispatcher.DispatchPendingAsync(stoppingToken);

                if (dispatchResult.ScannedCount > 0
                    || dispatchResult.FailedCount > 0
                    || dispatchResult.AbandonedCount > 0)
                {
                    logger.LogInformation(
                        "Outbox dispatch iteration scanned={scanned_count} succeeded={succeeded_count} failed={failed_count} pending_backlog={pending_backlog_count} abandoned={abandoned_count} last_failure_summary={last_failure_summary}",
                        dispatchResult.ScannedCount,
                        dispatchResult.SucceededCount,
                        dispatchResult.FailedCount,
                        dispatchResult.PendingBacklogCount,
                        dispatchResult.AbandonedCount,
                        dispatchResult.LastFailureSummary ?? string.Empty);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatcher iteration failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
