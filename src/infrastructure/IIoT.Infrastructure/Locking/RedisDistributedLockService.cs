using System.Diagnostics;
using IIoT.Services.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace IIoT.Infrastructure.Locking;

/// <summary>
/// 基于 Redis SET NX EX 和带 owner token 的 Lua compare-renew/compare-delete 实现分布式锁。
/// </summary>
public sealed class RedisDistributedLockService : IDistributedLockService
{
    private static readonly EventId RedisOperationFailed = new(4401, nameof(RedisOperationFailed));
    private static readonly EventId OwnershipLostEvent = new(4402, nameof(OwnershipLostEvent));
    private static readonly EventId RenewalShutdownFailed = new(4403, nameof(RenewalShutdownFailed));
    private static readonly EventId AcquireWaitFailed = new(4405, nameof(AcquireWaitFailed));

    private readonly IRedisDistributedLockPrimitive primitive;
    private readonly DistributedLockOptions options;
    private readonly ILogger<RedisDistributedLockService> logger;
    private readonly Func<TimeSpan, CancellationToken, Task> delayAsync;

    public RedisDistributedLockService(
        IConnectionMultiplexer redis,
        IOptions<DistributedLockOptions> options,
        ILogger<RedisDistributedLockService> logger)
        : this(
            new RedisDistributedLockPrimitive(redis.GetDatabase()),
            options,
            logger,
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken))
    {
    }

    internal RedisDistributedLockService(
        IRedisDistributedLockPrimitive primitive,
        IOptions<DistributedLockOptions> options,
        ILogger<RedisDistributedLockService> logger,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        this.primitive = primitive;
        this.options = options.Value;
        this.options.Validate();
        this.logger = logger;
        this.delayAsync = delayAsync
            ?? ((delay, cancellationToken) => Task.Delay(delay, cancellationToken));
    }

    public async Task<IDistributedLockLease> AcquireAsync(
        string resource,
        TimeSpan? acquireTimeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resource))
            throw new ArgumentException("Distributed lock resource is required.", nameof(resource));

        var timeout = acquireTimeout ?? TimeSpan.FromSeconds(10);
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(acquireTimeout));

        var leaseTtl = options.ResolveLeaseTtl();
        var lockValue = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        var firstAttempt = true;

        while (firstAttempt || stopwatch.Elapsed < timeout)
        {
            firstAttempt = false;
            cancellationToken.ThrowIfCancellationRequested();

            var remainingAcquireBudget = timeout - stopwatch.Elapsed;
            if (remainingAcquireBudget < TimeSpan.Zero)
                remainingAcquireBudget = TimeSpan.Zero;

            var configuredOperationTimeout = options.ResolveOperationTimeout();
            var attemptUsesAcquireDeadline = remainingAcquireBudget <= configuredOperationTimeout;
            var attemptTimeout = attemptUsesAcquireDeadline
                ? remainingAcquireBudget
                : configuredOperationTimeout;

            var acquired = await ExecuteRedisOperationAsync(
                "acquire",
                () => primitive.TryAcquireAsync(resource, lockValue, leaseTtl),
                cancellationToken,
                attemptTimeout,
                attemptUsesAcquireDeadline,
                detachedTask => ObserveDetachedAcquireTask(
                    detachedTask,
                    resource,
                    lockValue));
            if (acquired)
            {
                return new RedisDistributedLockLease(
                    this,
                    resource,
                    lockValue,
                    leaseTtl);
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                break;

            await WaitForAcquireRetryAsync(remaining, cancellationToken);
        }

        throw new DistributedLockConflictException();
    }

    private async Task<T> ExecuteRedisOperationAsync<T>(
        string operation,
        Func<Task<T>> operationFactory,
        CancellationToken cancellationToken,
        TimeSpan? operationTimeout = null,
        bool timeoutMeansAcquireDeadline = false,
        Action<Task<T>>? observeDetached = null)
    {
        Task<T> operationTask;
        try
        {
            operationTask = operationFactory();
        }
        catch (Exception exception)
        {
            LogRedisOperationFailure(operation, exception);
            throw new DistributedLockUnavailableException();
        }

        var observedOperationTask = CaptureRedisOperationAsync(operationTask);

        try
        {
            return await observedOperationTask.WaitAsync(
                operationTimeout ?? options.ResolveOperationTimeout(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ObserveDetached(observedOperationTask, observeDetached);
            throw;
        }
        catch (TimeoutException exception)
        {
            ObserveDetached(observedOperationTask, observeDetached);
            if (timeoutMeansAcquireDeadline)
                throw new DistributedLockConflictException();

            LogRedisOperationFailure(operation, exception);
            throw new DistributedLockUnavailableException();
        }
        catch (RedisOperationException exception)
        {
            LogRedisOperationFailure(operation, exception.InnerException ?? exception);
            throw new DistributedLockUnavailableException();
        }
        catch (Exception exception)
        {
            LogRedisOperationFailure(operation, exception);
            throw new DistributedLockUnavailableException();
        }
    }

    private static async Task<T> CaptureRedisOperationAsync<T>(Task<T> operationTask)
    {
        try
        {
            return await operationTask;
        }
        catch (Exception exception)
        {
            throw new RedisOperationException(exception);
        }
    }

    private async Task WaitForAcquireRetryAsync(
        TimeSpan remainingAcquireBudget,
        CancellationToken cancellationToken)
    {
        var retryDelay = remainingAcquireBudget < TimeSpan.FromMilliseconds(100)
            ? remainingAcquireBudget
            : TimeSpan.FromMilliseconds(100);
        Task delayTask;
        try
        {
            delayTask = delayAsync(retryDelay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogAcquireWaitFailure(exception);
            throw new DistributedLockUnavailableException();
        }

        try
        {
            await delayTask.WaitAsync(remainingAcquireBudget, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ObserveDetachedTask(delayTask);
            throw;
        }
        catch (TimeoutException exception)
        {
            if (delayTask.IsFaulted)
            {
                var delayException = delayTask.Exception?.GetBaseException() ?? exception;
                _ = delayTask.Exception;
                LogAcquireWaitFailure(delayException);
                throw new DistributedLockUnavailableException();
            }

            ObserveDetachedTask(delayTask);
            throw new DistributedLockConflictException();
        }
        catch (Exception exception)
        {
            LogAcquireWaitFailure(exception);
            throw new DistributedLockUnavailableException();
        }
    }

    private void LogAcquireWaitFailure(Exception exception)
    {
        logger.LogError(
            AcquireWaitFailed,
            "Distributed lock acquisition wait failed. ErrorType={ErrorType}.",
            exception.GetType().Name);
    }

    private static void ObserveDetached<T>(
        Task<T> operationTask,
        Action<Task<T>>? observer)
    {
        if (observer is null)
        {
            ObserveDetachedTask(operationTask);
            return;
        }

        observer(operationTask);
    }

    private void ObserveDetachedAcquireTask(
        Task<bool> acquireTask,
        string resource,
        string lockValue)
    {
        var cleanupTask = acquireTask.ContinueWith(
                async completedTask =>
                {
                    if (completedTask.IsFaulted)
                    {
                        _ = completedTask.Exception;
                        return;
                    }

                    if (!completedTask.IsCompletedSuccessfully || !completedTask.Result)
                        return;

                    await ReleaseDetachedAcquireAsync(resource, lockValue);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default)
            .Unwrap();
        ObserveDetachedTask(cleanupTask);
    }

    private async Task ReleaseDetachedAcquireAsync(string resource, string lockValue)
    {
        Task<bool> releaseTask;
        try
        {
            releaseTask = primitive.TryReleaseAsync(resource, lockValue);
        }
        catch (Exception exception)
        {
            LogRedisOperationFailure("late-acquire-cleanup", exception);
            return;
        }

        try
        {
            await releaseTask.WaitAsync(options.ResolveOperationTimeout());
        }
        catch (TimeoutException exception)
        {
            ObserveDetachedTask(releaseTask);
            LogRedisOperationFailure("late-acquire-cleanup", exception);
        }
        catch (Exception exception)
        {
            LogRedisOperationFailure("late-acquire-cleanup", exception);
        }
    }

    private void LogRedisOperationFailure(string operation, Exception exception)
    {
        logger.LogError(
            RedisOperationFailed,
            "Distributed lock Redis operation failed. Operation={Operation} ErrorType={ErrorType}.",
            operation,
            exception.GetType().Name);
    }

    private static void ObserveDetachedTask(Task task)
    {
        _ = task.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private sealed class RedisDistributedLockLease : IDistributedLockLease
    {
        private readonly RedisDistributedLockService owner;
        private readonly string resource;
        private readonly string lockValue;
        private readonly TimeSpan leaseTtl;
        private readonly CancellationTokenSource renewalStop = new();
        private readonly CancellationTokenSource ownershipLost = new();
        private readonly CancellationToken ownershipLostToken;
        private readonly Task renewalTask;
        private readonly object disposeGate = new();
        private Task? disposeTask;
        private Task? ownershipNotificationTask;
        private int ownershipLostSignaled;

        public RedisDistributedLockLease(
            RedisDistributedLockService owner,
            string resource,
            string lockValue,
            TimeSpan leaseTtl)
        {
            this.owner = owner;
            this.resource = resource;
            this.lockValue = lockValue;
            this.leaseTtl = leaseTtl;
            ownershipLostToken = ownershipLost.Token;
            renewalTask = RunRenewalLoopAsync();
        }

        public CancellationToken OwnershipLost => ownershipLostToken;

        public ValueTask DisposeAsync()
        {
            lock (disposeGate)
            {
                disposeTask ??= DisposeCoreAsync();
                return new ValueTask(disposeTask);
            }
        }

        private async Task RunRenewalLoopAsync()
        {
            while (true)
            {
                try
                {
                    await owner.delayAsync(
                        owner.options.ResolveRenewalCadence(),
                        renewalStop.Token);
                    if (renewalStop.IsCancellationRequested)
                        return;

                    var renewed = await owner.ExecuteRedisOperationAsync(
                        "renew",
                        () => owner.primitive.TryRenewAsync(resource, lockValue, leaseTtl),
                        renewalStop.Token);
                    if (!renewed)
                    {
                        SignalOwnershipLost("owner-token-mismatch");
                        return;
                    }
                }
                catch (OperationCanceledException) when (renewalStop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    SignalOwnershipLost(exception.GetType().Name);
                    return;
                }
            }
        }

        private async Task DisposeCoreAsync()
        {
            renewalStop.Cancel();
            Exception? shutdownFailure = null;

            try
            {
                await renewalTask.WaitAsync(owner.options.ResolveRenewalShutdownTimeout());
            }
            catch (TimeoutException exception)
            {
                RedisDistributedLockService.ObserveDetachedTask(renewalTask);
                shutdownFailure = new DistributedLockUnavailableException();
                SignalOwnershipLost("renewal-shutdown-timeout");
                owner.logger.LogError(
                    RenewalShutdownFailed,
                    "Distributed lock renewal shutdown failed. ErrorType={ErrorType}.",
                    exception.GetType().Name);
            }
            catch (Exception exception)
            {
                shutdownFailure = new DistributedLockUnavailableException();
                SignalOwnershipLost("renewal-shutdown-failed");
                owner.logger.LogError(
                    RenewalShutdownFailed,
                    "Distributed lock renewal shutdown failed. ErrorType={ErrorType}.",
                    exception.GetType().Name);
            }

            var released = false;
            try
            {
                released = await owner.ExecuteRedisOperationAsync(
                    "unlock",
                    () => owner.primitive.TryReleaseAsync(resource, lockValue),
                    CancellationToken.None);
                if (!released)
                    SignalOwnershipLost("unlock-owner-token-mismatch");
            }
            catch (DistributedLockUnavailableException exception)
            {
                shutdownFailure ??= exception;
            }
            finally
            {
                renewalStop.Dispose();
                DisposeOwnershipNotificationSourceWhenSafe();
            }

            if (shutdownFailure is not null)
                throw shutdownFailure;

            if (!released)
            {
                throw new DistributedLockOwnershipLostException();
            }
        }

        private void SignalOwnershipLost(string reason)
        {
            if (Interlocked.Exchange(ref ownershipLostSignaled, 1) != 0)
                return;

            ownershipNotificationTask = ownershipLost.CancelAsync();
            _ = ownershipNotificationTask.ContinueWith(
                static (completedTask, state) =>
                {
                    var (eventLogger, eventReason) =
                        ((ILogger<RedisDistributedLockService>, string))state!;
                    eventLogger.LogError(
                        OwnershipLostEvent,
                        "Distributed lock ownership-loss notification failed. Reason={Reason} ErrorType={ErrorType}.",
                        eventReason,
                        completedTask.Exception?.GetType().Name ?? "Unknown");
                    _ = completedTask.Exception;
                },
                (owner.logger, reason),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

            owner.logger.LogError(
                OwnershipLostEvent,
                "Distributed lock ownership was lost. Reason={Reason}.",
                reason);
        }

        private void DisposeOwnershipNotificationSourceWhenSafe()
        {
            var notificationTask = ownershipNotificationTask;
            if (notificationTask is null || notificationTask.IsCompleted)
            {
                ownershipLost.Dispose();
                return;
            }

            _ = notificationTask.ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                ownershipLost,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private sealed class RedisOperationException(Exception innerException)
        : Exception("Distributed lock Redis operation failed.", innerException);
}

internal interface IRedisDistributedLockPrimitive
{
    Task<bool> TryAcquireAsync(string resource, string lockValue, TimeSpan leaseTtl);

    Task<bool> TryRenewAsync(string resource, string lockValue, TimeSpan leaseTtl);

    Task<bool> TryReleaseAsync(string resource, string lockValue);
}

internal sealed class RedisDistributedLockPrimitive(IDatabase database) : IRedisDistributedLockPrimitive
{
    internal const string UnlockScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then " +
        "  return redis.call('del', KEYS[1]) " +
        "else " +
        "  return 0 " +
        "end";

    internal const string RenewScript =
        "if redis.call('get', KEYS[1]) == ARGV[1] then " +
        "  return redis.call('pexpire', KEYS[1], ARGV[2]) " +
        "else " +
        "  return 0 " +
        "end";

    public Task<bool> TryAcquireAsync(string resource, string lockValue, TimeSpan leaseTtl)
        => database.StringSetAsync(resource, lockValue, leaseTtl, When.NotExists);

    public async Task<bool> TryRenewAsync(string resource, string lockValue, TimeSpan leaseTtl)
    {
        var result = await database.ScriptEvaluateAsync(
            RenewScript,
            keys: [(RedisKey)resource],
            values:
            [
                (RedisValue)lockValue,
                (RedisValue)((long)leaseTtl.TotalMilliseconds)
            ]);
        return (long)result != 0;
    }

    public async Task<bool> TryReleaseAsync(string resource, string lockValue)
    {
        var result = await database.ScriptEvaluateAsync(
            UnlockScript,
            keys: [(RedisKey)resource],
            values: [(RedisValue)lockValue]);
        return (long)result != 0;
    }
}
