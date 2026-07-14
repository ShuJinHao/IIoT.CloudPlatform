using IIoT.Services.Contracts;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static IIoT.CloudPlatform.WorkflowTests.DistributedLockTestSupport;

namespace IIoT.CloudPlatform.WorkflowTests;

public sealed class DistributedLockBehaviorTests
{
    [Fact]
    public async Task Behavior_ShouldTranslateLeaseCancellationWhenHandlerObservesToken()
    {
        var lease = new TestDistributedLockLease();
        var behavior = CreateBehavior(lease);
        var handlerStarted = NewCompletionSource<bool>();
        var execution = behavior.Handle(
            new LeaseAwareCommand(Guid.NewGuid()),
            async cancellationToken =>
            {
                handlerStarted.TrySetResult(true);
                var cancelled = NewCompletionSource<Result<bool>>();
                using var registration = cancellationToken.Register(
                    () => cancelled.TrySetCanceled(cancellationToken));
                return await cancelled.Task;
            },
            CancellationToken.None);

        await handlerStarted.Task;
        lease.LoseOwnership();

        await Assert.ThrowsAsync<DistributedLockOwnershipLostException>(() => execution);
        Assert.Equal(1, lease.DisposeCalls);
    }

    [Fact]
    public async Task Behavior_ShouldRejectSuccessWhenHandlerIgnoresLeaseCancellation()
    {
        var lease = new TestDistributedLockLease();
        var behavior = CreateBehavior(lease);
        var handlerStarted = NewCompletionSource<bool>();
        var allowHandlerReturn = NewCompletionSource<bool>();
        var execution = behavior.Handle(
            new LeaseAwareCommand(Guid.NewGuid()),
            async _ =>
            {
                handlerStarted.TrySetResult(true);
                await allowHandlerReturn.Task;
                return Result.Success(true);
            },
            CancellationToken.None);

        await handlerStarted.Task;
        lease.LoseOwnership();
        allowHandlerReturn.TrySetResult(true);

        await Assert.ThrowsAsync<DistributedLockOwnershipLostException>(() => execution);
    }

    [Fact]
    public async Task Behavior_ShouldPreserveCallerCancellation()
    {
        var lease = new TestDistributedLockLease();
        var behavior = CreateBehavior(lease);
        using var callerCancellation = new CancellationTokenSource();
        var handlerStarted = NewCompletionSource<bool>();
        var execution = behavior.Handle(
            new LeaseAwareCommand(Guid.NewGuid()),
            async cancellationToken =>
            {
                handlerStarted.TrySetResult(true);
                var cancelled = NewCompletionSource<Result<bool>>();
                using var registration = cancellationToken.Register(
                    () => cancelled.TrySetCanceled(cancellationToken));
                return await cancelled.Task;
            },
            callerCancellation.Token);

        await handlerStarted.Task;
        callerCancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal(callerCancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task Behavior_ShouldPreserveCallerCancellationWhenDisposeAlsoFails()
    {
        var lease = new TestDistributedLockLease
        {
            DisposeException = new DistributedLockUnavailableException()
        };
        var behavior = CreateBehavior(lease);
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        var handlerCalls = 0;

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => behavior.Handle(
            new LeaseAwareCommand(Guid.NewGuid()),
            cancellationToken =>
            {
                handlerCalls++;
                return Task.FromCanceled<Result<bool>>(cancellationToken);
            },
            callerCancellation.Token));

        Assert.IsType<OperationCanceledException>(exception);
        Assert.Equal(callerCancellation.Token, exception.CancellationToken);
        Assert.Equal(1, handlerCalls);
        Assert.Equal(1, lease.DisposeCalls);
    }

    [Fact]
    public async Task Behavior_ShouldPreserveHandlerFailureWhenDisposeAlsoFails()
    {
        var handlerFailure = new InvalidOperationException("handler failure");
        var lease = new TestDistributedLockLease
        {
            DisposeException = new DistributedLockUnavailableException()
        };
        var behavior = CreateBehavior(lease);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(
            new LeaseAwareCommand(Guid.NewGuid()),
            _ => Task.FromException<Result<bool>>(handlerFailure),
            CancellationToken.None));

        Assert.Same(handlerFailure, exception);
    }

    [Fact]
    public async Task Behavior_ShouldSurfaceDisposeFailureAfterSuccessfulHandler()
    {
        var lease = new TestDistributedLockLease
        {
            DisposeException = new DistributedLockUnavailableException()
        };
        var behavior = CreateBehavior(lease);

        await Assert.ThrowsAsync<DistributedLockUnavailableException>(() => behavior.Handle(
            new LeaseAwareCommand(Guid.NewGuid()),
            _ => Task.FromResult(Result.Success(true)),
            CancellationToken.None));
    }

    private static DistributedLockBehavior<LeaseAwareCommand, Result<bool>> CreateBehavior(
        TestDistributedLockLease lease)
    {
        return new DistributedLockBehavior<LeaseAwareCommand, Result<bool>>(
            new TestDistributedLockService(lease),
            NullLogger<DistributedLockBehavior<LeaseAwareCommand, Result<bool>>>.Instance);
    }

    [DistributedLock("iiot:lock:test:{Id}", TimeoutSeconds = 1)]
    private sealed record LeaseAwareCommand(Guid Id) : IRequest<Result<bool>>;

    private sealed class TestDistributedLockService(TestDistributedLockLease lease)
        : IDistributedLockService
    {
        public Task<IDistributedLockLease> AcquireAsync(
            string resource,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IDistributedLockLease>(lease);
    }

    private sealed class TestDistributedLockLease : IDistributedLockLease
    {
        private readonly CancellationTokenSource ownershipLost = new();

        public CancellationToken OwnershipLost => ownershipLost.Token;

        public Exception? DisposeException { get; init; }

        public int DisposeCalls { get; private set; }

        public void LoseOwnership() => ownershipLost.Cancel();

        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return DisposeException is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(DisposeException);
        }
    }
}
