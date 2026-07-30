using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
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

    [Fact]
    public async Task RecoveredAudit_ShouldUseIndependentBoundedToken()
    {
        var audit = new CapturingAuditTrailService();

        await CloudWriteCommitRecovery.ConfirmRecoveredAuditAsync(
            audit,
            CreateAuditEntry());

        var token = Assert.Single(audit.CancellationTokens);
        Assert.True(token.CanBeCanceled);
        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public async Task RecoveredAuditNotConfirmed_ShouldFailClosed()
    {
        var audit = new CapturingAuditTrailService
        {
            Confirmed = false
        };

        await Assert.ThrowsAsync<CloudWriteCommitUnknownException>(() =>
            CloudWriteCommitRecovery.ConfirmRecoveredAuditAsync(
                audit,
                CreateAuditEntry()));
    }

    private static AuditTrailEntry CreateAuditEntry()
        => new(
            null,
            "test",
            "Test.Write",
            "Test",
            "target",
            DateTime.UtcNow,
            true,
            "test",
            IdempotencyKey: "test-write:target");

    private sealed class CapturingAuditTrailService : IAuditTrailService
    {
        public bool Confirmed { get; init; } = true;

        public List<CancellationToken> CancellationTokens { get; } = [];

        public Task TryWriteAsync(
            AuditTrailEntry entry,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TryWriteConfirmedAsync(
            AuditTrailEntry entry,
            CancellationToken cancellationToken = default)
        {
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(Confirmed);
        }
    }
}
