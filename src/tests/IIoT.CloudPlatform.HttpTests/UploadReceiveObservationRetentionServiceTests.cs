using IIoT.HttpApi.Infrastructure;
using IIoT.Services.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IIoT.CloudPlatform.HttpTests;

public sealed class UploadReceiveObservationRetentionServiceTests
{
    [Fact]
    public async Task CleanupCycle_ShouldPruneWithoutAnyFutureUploadRequest()
    {
        var observedAtUtc =
            new DateTimeOffset(2026, 7, 31, 7, 0, 0, TimeSpan.Zero);
        var pruner = new RecordingRetentionPruner(513);
        var services = new ServiceCollection();
        services.AddSingleton<IUploadReceiveObservationRetentionPruner>(
            pruner);
        await using var provider = services.BuildServiceProvider();
        var service = new UploadReceiveObservationRetentionService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<UploadReceiveObservationRetentionService>.Instance);

        var deleted = await service.RunCleanupCycleAsync(
            observedAtUtc,
            CancellationToken.None);

        Assert.Equal(513, deleted);
        Assert.Equal(1, pruner.Calls);
        Assert.Equal(observedAtUtc, pruner.ObservedAtUtc);
    }

    private sealed class RecordingRetentionPruner(int deleted)
        : IUploadReceiveObservationRetentionPruner
    {
        public int Calls { get; private set; }

        public DateTimeOffset? ObservedAtUtc { get; private set; }

        public Task<int> PruneExpiredAsync(
            DateTimeOffset observedAtUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            ObservedAtUtc = observedAtUtc;
            return Task.FromResult(deleted);
        }
    }
}
