using IIoT.Services.CrossCutting.Persistence;
using Xunit;

namespace IIoT.CloudPlatform.ApplicationTests;

public sealed class CloudWriteCommitRecoveryTests
{
    [Fact]
    public async Task AttemptObservationFailure_ShouldFailClosed()
    {
        var result = await CloudWriteCommitRecovery.TryObserveAttemptAsync<object>(
            _ => throw new InvalidOperationException(
                "simulated observation failure"),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AttemptObservationTimeout_ShouldFailClosed()
    {
        var result = await CloudWriteCommitRecovery.TryObserveAttemptAsync<object>(
            token => throw new OperationCanceledException(token),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task AttemptObservationCallerCancellation_ShouldPropagateOriginalToken()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CloudWriteCommitRecovery.TryObserveAttemptAsync<object>(
                _ => Task.FromResult(new object()),
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task CommitObservationFailure_ShouldFailClosedWithoutCallerToken()
    {
        var result = await CloudWriteCommitRecovery.TryObserveCommitAsync<object>(
            token => throw new OperationCanceledException(token));

        Assert.Null(result);
    }
}
