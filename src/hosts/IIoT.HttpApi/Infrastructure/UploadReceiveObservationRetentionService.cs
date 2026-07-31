using IIoT.Services.Contracts;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IIoT.HttpApi.Infrastructure;

public sealed class UploadReceiveObservationRetentionService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<UploadReceiveObservationRetentionService> logger)
    : BackgroundService
{
    internal static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    internal static void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<UploadReceiveObservationRetentionService>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = await RunCleanupCycleAsync(
                    timeProvider.GetUtcNow(),
                    stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation(
                        "Upload duplicate observation retention removed {DeletedCount} expired receipts.",
                        deleted);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    new EventId(
                        4614,
                        "UploadReceiveObservationRetentionFailure"),
                    ex,
                    "Upload duplicate observation retention failed. ErrorType={ErrorType}.",
                    ex.GetType().Name);
            }

            try
            {
                await Task.Delay(
                    CleanupInterval,
                    timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    internal async Task<int> RunCleanupCycleAsync(
        DateTimeOffset observedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var pruner = scope.ServiceProvider
            .GetRequiredService<IUploadReceiveObservationRetentionPruner>();
        return await pruner.PruneExpiredAsync(
            observedAtUtc,
            cancellationToken);
    }
}
