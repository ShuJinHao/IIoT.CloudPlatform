using IIoT.Infrastructure.Locking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IIoT.ServiceLayer.Tests;

internal static class DistributedLockTestSupport
{
    public static TaskCompletionSource<T> NewCompletionSource<T>()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static RedisDistributedLockService CreateService(
        ControllableRedisLockPrimitive primitive,
        int leaseSeconds = 5,
        int renewalCadenceSeconds = 1,
        int operationTimeoutMilliseconds = 100,
        int renewalShutdownTimeoutMilliseconds = 100,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        ILogger<RedisDistributedLockService>? logger = null)
    {
        return new RedisDistributedLockService(
            primitive,
            Options.Create(new DistributedLockOptions
            {
                LeaseSeconds = leaseSeconds,
                RenewalCadenceSeconds = renewalCadenceSeconds,
                OperationTimeoutMilliseconds = operationTimeoutMilliseconds,
                RenewalShutdownTimeoutMilliseconds = renewalShutdownTimeoutMilliseconds
            }),
            logger ?? NullLogger<RedisDistributedLockService>.Instance,
            delayAsync);
    }

    public static Task WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.CompletedTask;

        var completion = NewCompletionSource<bool>();
        cancellationToken.Register(() => completion.TrySetResult(true));
        return completion.Task;
    }
}

internal sealed class ControllableRedisLockPrimitive : IRedisDistributedLockPrimitive
{
    public Func<string, string, TimeSpan, Task<bool>>? Acquire { get; init; }

    public Func<string, string, TimeSpan, Task<bool>>? Renew { get; init; }

    public Func<string, string, Task<bool>>? Release { get; init; }

    public int AcquireCalls { get; private set; }

    public int RenewCalls { get; private set; }

    public int ReleaseCalls { get; private set; }

    public string? CurrentOwner { get; set; }

    public string? LastReleaseOwner { get; private set; }

    public Task<bool> TryAcquireAsync(string resource, string lockValue, TimeSpan leaseTtl)
    {
        AcquireCalls++;
        if (Acquire is not null)
            return Acquire(resource, lockValue, leaseTtl);

        if (CurrentOwner is not null)
            return Task.FromResult(false);

        CurrentOwner = lockValue;
        return Task.FromResult(true);
    }

    public Task<bool> TryRenewAsync(string resource, string lockValue, TimeSpan leaseTtl)
    {
        RenewCalls++;
        return Renew is not null
            ? Renew(resource, lockValue, leaseTtl)
            : Task.FromResult(string.Equals(CurrentOwner, lockValue, StringComparison.Ordinal));
    }

    public Task<bool> TryReleaseAsync(string resource, string lockValue)
    {
        ReleaseCalls++;
        LastReleaseOwner = lockValue;
        if (Release is not null)
            return Release(resource, lockValue);

        if (!string.Equals(CurrentOwner, lockValue, StringComparison.Ordinal))
            return Task.FromResult(false);

        CurrentOwner = null;
        return Task.FromResult(true);
    }
}

internal sealed class ControlledDelay
{
    private readonly TaskCompletionSource<bool> requested =
        DistributedLockTestSupport.NewCompletionSource<bool>();
    private readonly TaskCompletionSource<bool> completion =
        DistributedLockTestSupport.NewCompletionSource<bool>();

    public Task DelayAsync(TimeSpan _, CancellationToken cancellationToken)
    {
        requested.TrySetResult(true);
        return completion.Task.WaitAsync(cancellationToken);
    }

    public Task WaitUntilRequestedAsync() => requested.Task;

    public void Complete() => completion.TrySetResult(true);
}

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));
    }
}
