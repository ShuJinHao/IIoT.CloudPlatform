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

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollingIntervalSeconds));

        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IOutboxMessageDispatcher>();
                var dispatched = await dispatcher.DispatchPendingAsync(stoppingToken);

                if (dispatched > 0)
                {
                    logger.LogInformation("Dispatched {Count} outbox message(s).", dispatched);
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
