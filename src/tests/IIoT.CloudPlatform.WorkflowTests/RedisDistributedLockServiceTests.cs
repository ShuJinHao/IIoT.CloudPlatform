using IIoT.Infrastructure.Locking;
using IIoT.Services.Contracts;
using Xunit;
using static IIoT.CloudPlatform.WorkflowTests.DistributedLockTestSupport;

namespace IIoT.CloudPlatform.WorkflowTests;

public sealed class RedisDistributedLockServiceTests
{
    [Fact]
    public async Task AcquireAsync_ShouldReportContentionWithoutLeakingResource()
    {
        var pendingAcquire = NewCompletionSource<bool>();
        var primitive = new ControllableRedisLockPrimitive
        {
            Acquire = (_, _, _) => pendingAcquire.Task
        };
        var service = CreateService(primitive);

        var exception = await Assert.ThrowsAsync<DistributedLockConflictException>(() =>
            service.AcquireAsync("iiot:lock:employee:E-SECRET", TimeSpan.Zero)
                .WaitAsync(TimeSpan.FromSeconds(1)));

        Assert.Equal(DistributedLockConflictException.PublicMessage, exception.Message);
        Assert.DoesNotContain("E-SECRET", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, primitive.AcquireCalls);
        pendingAcquire.TrySetResult(false);
    }

    [Fact]
    public async Task AcquireAsync_ShouldMapRedisOperationTimeoutToUnavailable()
    {
        var pendingAcquire = NewCompletionSource<bool>();
        var primitive = new ControllableRedisLockPrimitive
        {
            Acquire = (_, _, _) => pendingAcquire.Task
        };
        var service = CreateService(primitive, operationTimeoutMilliseconds: 25);

        var exception = await Assert.ThrowsAsync<DistributedLockUnavailableException>(() =>
            service.AcquireAsync("iiot:lock:test", TimeSpan.FromSeconds(1)));

        Assert.Equal(DistributedLockUnavailableException.PublicMessage, exception.Message);
        pendingAcquire.TrySetException(new InvalidOperationException("late redis failure must be observed"));
    }

    [Fact]
    public async Task AcquireAsync_ShouldMapUnderlyingRedisTimeoutToUnavailableBeforeShortDeadline()
    {
        var primitive = new ControllableRedisLockPrimitive
        {
            Acquire = (_, _, _) => Task.FromException<bool>(new TimeoutException("redis timeout detail"))
        };
        var service = CreateService(
            primitive,
            leaseSeconds: 30,
            operationTimeoutMilliseconds: 5000);

        await Assert.ThrowsAsync<DistributedLockUnavailableException>(() =>
            service.AcquireAsync("iiot:lock:test", TimeSpan.FromMilliseconds(25)));
    }

    [Fact]
    public async Task AcquireAsync_ShouldHonorShortTotalDeadlineBeforeLongRedisTimeout()
    {
        var pendingAcquire = NewCompletionSource<bool>();
        var lateAcquireReleased = NewCompletionSource<bool>();
        string? acquireOwner = null;
        string? releaseOwner = null;
        var primitive = new ControllableRedisLockPrimitive
        {
            Acquire = (_, owner, _) =>
            {
                acquireOwner = owner;
                return pendingAcquire.Task;
            },
            Release = (_, owner) =>
            {
                releaseOwner = owner;
                lateAcquireReleased.TrySetResult(true);
                return Task.FromResult(true);
            }
        };
        var service = CreateService(
            primitive,
            leaseSeconds: 30,
            operationTimeoutMilliseconds: 5000);

        var acquireTask = service.AcquireAsync(
            "iiot:lock:test",
            TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAsync<DistributedLockConflictException>(() =>
            acquireTask.WaitAsync(TimeSpan.FromSeconds(1)));

        pendingAcquire.TrySetResult(true);
        await lateAcquireReleased.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(acquireOwner, releaseOwner);
        Assert.Equal(1, primitive.ReleaseCalls);
    }

    [Fact]
    public async Task AcquireAsync_ShouldBoundRetryDelayByRemainingTotalBudget()
    {
        var retryDelayStarted = NewCompletionSource<bool>();
        var pendingRetryDelay = NewCompletionSource<bool>();
        var primitive = new ControllableRedisLockPrimitive
        {
            Acquire = (_, _, _) => Task.FromResult(false)
        };
        var service = CreateService(
            primitive,
            leaseSeconds: 30,
            operationTimeoutMilliseconds: 5000,
            delayAsync: (_, _) =>
            {
                retryDelayStarted.TrySetResult(true);
                return pendingRetryDelay.Task;
            });

        var acquireTask = service.AcquireAsync(
            "iiot:lock:test",
            TimeSpan.FromMilliseconds(100));
        await retryDelayStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<DistributedLockConflictException>(() =>
            acquireTask.WaitAsync(TimeSpan.FromSeconds(1)));

        Assert.Equal(1, primitive.AcquireCalls);
        pendingRetryDelay.TrySetResult(true);
    }

    [Fact]
    public async Task AcquireAsync_ShouldPreserveCallerCancellationAndReleaseLateSuccess()
    {
        var pendingAcquire = NewCompletionSource<bool>();
        var lateAcquireReleased = NewCompletionSource<bool>();
        var primitive = new ControllableRedisLockPrimitive
        {
            Acquire = (_, _, _) => pendingAcquire.Task,
            Release = (_, _) =>
            {
                lateAcquireReleased.TrySetResult(true);
                return Task.FromResult(true);
            }
        };
        var service = CreateService(primitive);
        using var callerCancellation = new CancellationTokenSource();
        var acquireTask = service.AcquireAsync(
            "iiot:lock:test",
            TimeSpan.FromSeconds(1),
            callerCancellation.Token);

        callerCancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquireTask);
        Assert.Equal(callerCancellation.Token, exception.CancellationToken);
        pendingAcquire.TrySetResult(true);
        await lateAcquireReleased.Task;
        Assert.Equal(1, primitive.ReleaseCalls);
    }

    [Fact]
    public async Task RedisFailureLog_ShouldContainOnlyStableTypeAndNoSensitiveDetails()
    {
        var logger = new RecordingLogger<RedisDistributedLockService>();
        var primitive = new ControllableRedisLockPrimitive
        {
            Acquire = (_, _, _) => Task.FromException<bool>(
                new InvalidOperationException("redis-secret-message"))
        };
        var service = CreateService(primitive, logger: logger);

        await Assert.ThrowsAsync<DistributedLockUnavailableException>(() =>
            service.AcquireAsync("iiot:lock:employee:E-SECRET", TimeSpan.Zero));

        var log = Assert.Single(logger.Messages);
        Assert.Contains("InvalidOperationException", log, StringComparison.Ordinal);
        Assert.DoesNotContain("redis-secret-message", log, StringComparison.Ordinal);
        Assert.DoesNotContain("E-SECRET", log, StringComparison.Ordinal);
        Assert.DoesNotContain("iiot:lock", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenewalReturningZero_ShouldSignalOwnershipLostAndStop()
    {
        var renewalDelay = new ControlledDelay();
        var primitive = new ControllableRedisLockPrimitive
        {
            Renew = (_, _, _) => Task.FromResult(false)
        };
        var service = CreateService(primitive, delayAsync: renewalDelay.DelayAsync);
        var lease = await service.AcquireAsync("iiot:lock:test", TimeSpan.Zero);
        var ownershipLost = WaitForCancellationAsync(lease.OwnershipLost);

        await renewalDelay.WaitUntilRequestedAsync();
        renewalDelay.Complete();
        await ownershipLost;

        Assert.Equal(1, primitive.RenewCalls);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task RenewalException_ShouldSignalOwnershipLostAndStop()
    {
        var renewalDelay = new ControlledDelay();
        var primitive = new ControllableRedisLockPrimitive
        {
            Renew = (_, _, _) => Task.FromException<bool>(new InvalidOperationException("redis detail"))
        };
        var service = CreateService(primitive, delayAsync: renewalDelay.DelayAsync);
        var lease = await service.AcquireAsync("iiot:lock:test", TimeSpan.Zero);
        var ownershipLost = WaitForCancellationAsync(lease.OwnershipLost);

        await renewalDelay.WaitUntilRequestedAsync();
        renewalDelay.Complete();
        await ownershipLost;

        Assert.Equal(1, primitive.RenewCalls);
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task RenewalTimeout_ShouldSignalOwnershipLostAndObserveLateCompletion()
    {
        var renewalDelay = new ControlledDelay();
        var pendingRenewal = NewCompletionSource<bool>();
        var primitive = new ControllableRedisLockPrimitive
        {
            Renew = (_, _, _) => pendingRenewal.Task
        };
        var service = CreateService(
            primitive,
            operationTimeoutMilliseconds: 25,
            delayAsync: renewalDelay.DelayAsync);
        var lease = await service.AcquireAsync("iiot:lock:test", TimeSpan.Zero);
        var ownershipLost = WaitForCancellationAsync(lease.OwnershipLost);

        await renewalDelay.WaitUntilRequestedAsync();
        renewalDelay.Complete();
        await ownershipLost;

        pendingRenewal.TrySetException(new InvalidOperationException("late renewal failure"));
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task BlockingOwnershipLostCallback_ShouldNotBlockRenewalOrDispose()
    {
        var renewalDelay = new ControlledDelay();
        var callbackStarted = NewCompletionSource<bool>();
        using var releaseCallback = new ManualResetEventSlim();
        var primitive = new ControllableRedisLockPrimitive
        {
            Renew = (_, _, _) => Task.FromResult(false)
        };
        var service = CreateService(primitive, delayAsync: renewalDelay.DelayAsync);
        var lease = await service.AcquireAsync("iiot:lock:test", TimeSpan.Zero);
        using var registration = lease.OwnershipLost.Register(() =>
        {
            callbackStarted.TrySetResult(true);
            if (!releaseCallback.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Ownership-lost callback barrier was not released within the test timeout.");
            }
        });

        await renewalDelay.WaitUntilRequestedAsync();
        renewalDelay.Complete();
        await callbackStarted.Task;
        try
        {
            Assert.True(lease.OwnershipLost.IsCancellationRequested);
            await lease.DisposeAsync();
            Assert.Equal(1, primitive.ReleaseCalls);
        }
        finally
        {
            releaseCallback.Set();
        }
    }

    [Fact]
    public async Task ThrowingOwnershipLostCallback_ShouldBeObservedWithoutBreakingDispose()
    {
        var renewalDelay = new ControlledDelay();
        var callbackInvoked = NewCompletionSource<bool>();
        var primitive = new ControllableRedisLockPrimitive
        {
            Renew = (_, _, _) => Task.FromResult(false)
        };
        var service = CreateService(primitive, delayAsync: renewalDelay.DelayAsync);
        var lease = await service.AcquireAsync("iiot:lock:test", TimeSpan.Zero);
        using var registration = lease.OwnershipLost.Register(() =>
        {
            callbackInvoked.TrySetResult(true);
            throw new InvalidOperationException("callback failure must be observed");
        });

        await renewalDelay.WaitUntilRequestedAsync();
        renewalDelay.Complete();
        await callbackInvoked.Task;

        await lease.DisposeAsync();
        Assert.True(lease.OwnershipLost.IsCancellationRequested);
        Assert.Equal(1, primitive.ReleaseCalls);
    }

    [Fact]
    public async Task DisposeAsync_ShouldBeIdempotentAndBoundRenewalShutdown()
    {
        var neverCompletingDelay = NewCompletionSource<bool>();
        var primitive = new ControllableRedisLockPrimitive();
        var service = CreateService(
            primitive,
            renewalShutdownTimeoutMilliseconds: 25,
            delayAsync: (_, _) => neverCompletingDelay.Task);
        var lease = await service.AcquireAsync("iiot:lock:test", TimeSpan.Zero);

        var firstDispose = lease.DisposeAsync().AsTask();
        var secondDispose = lease.DisposeAsync().AsTask();

        await Assert.ThrowsAsync<DistributedLockUnavailableException>(() => firstDispose);
        await Assert.ThrowsAsync<DistributedLockUnavailableException>(() => secondDispose);
        Assert.Equal(1, primitive.ReleaseCalls);
        neverCompletingDelay.TrySetResult(true);
    }

    [Fact]
    public async Task UnlockTimeout_ShouldBeBoundedAndObserveLateFailure()
    {
        var pendingRelease = NewCompletionSource<bool>();
        var primitive = new ControllableRedisLockPrimitive
        {
            Release = (_, _) => pendingRelease.Task
        };
        var service = CreateService(primitive, operationTimeoutMilliseconds: 25);
        var lease = await service.AcquireAsync("iiot:lock:test", TimeSpan.Zero);

        await Assert.ThrowsAsync<DistributedLockUnavailableException>(
            () => lease.DisposeAsync().AsTask());

        Assert.Equal(1, primitive.ReleaseCalls);
        pendingRelease.TrySetException(new InvalidOperationException("late unlock failure"));
    }

    [Fact]
    public async Task Unlock_ShouldUseOriginalOwnerTokenAndNeverDeleteAnotherOwner()
    {
        var primitive = new ControllableRedisLockPrimitive();
        var service = CreateService(primitive);
        var lease = await service.AcquireAsync("iiot:lock:test", TimeSpan.Zero);
        var originalOwner = Assert.IsType<string>(primitive.CurrentOwner);
        primitive.CurrentOwner = "another-owner";

        await Assert.ThrowsAsync<DistributedLockOwnershipLostException>(
            () => lease.DisposeAsync().AsTask());

        Assert.Equal(originalOwner, primitive.LastReleaseOwner);
        Assert.Equal("another-owner", primitive.CurrentOwner);
        Assert.True(lease.OwnershipLost.IsCancellationRequested);
    }
}
