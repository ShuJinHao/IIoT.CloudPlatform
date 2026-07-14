using System.Net;
using System.Runtime.ExceptionServices;
using IIoT.Services.Contracts;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace IIoT.Infrastructure.Caching;

/// <summary>
/// Redis 分布式缓存实现类 - 基于 FusionCache（L1 内存 + L2 Redis + Backplane 多实例同步）
/// </summary>
public class RedisCacheService(
    IFusionCache fusionCache,
    IConnectionMultiplexer redis,
    ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly EventId InfrastructureDegradedEvent = new(2401, "ValueCacheInfrastructureDegraded");

    private readonly IFusionCache _fusionCache = fusionCache;
    private readonly IConnectionMultiplexer _redis = redis;
    private readonly ILogger<RedisCacheService> _logger = logger;

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await _fusionCache.TryGetAsync<T>(key, token: cancellationToken);
            return result.HasValue ? result.Value : default;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsDegradableValueCacheFailure(ex))
        {
            LogInfrastructureDegradation("read", ex);
            return default;
        }
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T?>> factory,
        TimeSpan? absoluteExpireTime = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var duration = absoluteExpireTime ?? TimeSpan.FromMinutes(5);
        var factoryInvocation = new SingleFactoryInvocation<T>(factory);

        try
        {
            return await _fusionCache.GetOrSetAsync<T?>(
                key,
                factoryInvocation.InvokeAsync,
                default(ZiggyCreatures.Caching.Fusion.MaybeValue<T?>),
                duration,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            factoryInvocation.ThrowCapturedFailure();
            throw;
        }
        catch (Exception ex)
        {
            factoryInvocation.ThrowCapturedFailure();

            if (!IsDegradableValueCacheFailure(ex))
            {
                throw;
            }

            if (!factoryInvocation.HasStarted)
            {
                LogInfrastructureDegradation("get-or-set", ex);
                return await factoryInvocation.InvokeAsync(cancellationToken);
            }

            if (factoryInvocation.HasCompletedSuccessfully)
            {
                LogInfrastructureDegradation("get-or-set", ex);
                return factoryInvocation.Result;
            }

            if (ex is SyntheticTimeoutException)
            {
                // FusionCache 2.6 uses this same type for factory and provider synthetic timeouts.
                // Once the factory is running, preserve its configured timeout instead of
                // starting another execution or waiting past the timeout boundary.
                factoryInvocation.ThrowCapturedFailure();
                if (factoryInvocation.HasCompletedSuccessfully)
                {
                    return factoryInvocation.Result;
                }

                throw;
            }

            // A provider failure that races an already-running factory must not mask that
            // factory's eventual result or business exception. Reuse the same execution.
            LogInfrastructureDegradation("get-or-set", ex);
            return await factoryInvocation.Completion.WaitAsync(cancellationToken);
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? absoluteExpireTime = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (value == null)
        {
            await RemoveAsync(key, cancellationToken);
            return;
        }

        var duration = absoluteExpireTime ?? TimeSpan.FromMinutes(5);

        try
        {
            await _fusionCache.SetAsync(key, value, duration, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsDegradableValueCacheFailure(ex))
        {
            LogInfrastructureDegradation("write", ex);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _fusionCache.RemoveAsync(key, token: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsDegradableValueCacheFailure(ex))
        {
            LogInfrastructureDegradation("remove", ex);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        EndPoint[] endpoints;

        try
        {
            endpoints = _redis.GetEndPoints();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsDegradableRedisScanFailure(ex))
        {
            LogInfrastructureDegradation("remove-by-pattern:endpoints", ex);
            return;
        }

        // 遍历所有 Redis 服务端节点（Cluster/Sentinel 兼容）
        foreach (var endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var server = _redis.GetServer(endpoint);
                cancellationToken.ThrowIfCancellationRequested();
                if (!server.IsConnected)
                {
                    LogInfrastructureDegradation("remove-by-pattern:scan", "RedisServerDisconnected");
                    continue;
                }

                // SCAN 扫描匹配的 Key，通过 FusionCache 删除（同时清 L1 + L2 + 通知 Backplane）
                await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await RemoveAsync(key.ToString(), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsDegradableRedisScanFailure(ex))
            {
                LogInfrastructureDegradation("remove-by-pattern:scan", ex);
            }
        }
    }

    private static bool IsDegradableValueCacheFailure(Exception exception) => exception is
        FusionCacheDistributedCacheException or
        FusionCacheBackplaneException or
        SyntheticTimeoutException or
        RedisConnectionException or
        RedisTimeoutException;

    private static bool IsDegradableRedisScanFailure(Exception exception) => exception is
        RedisConnectionException or
        RedisTimeoutException;

    private void LogInfrastructureDegradation(string operation, Exception exception)
    {
        LogInfrastructureDegradation(operation, exception.GetType().Name);
    }

    private void LogInfrastructureDegradation(string operation, string errorType)
    {
        _logger.LogWarning(
            InfrastructureDegradedEvent,
            "Value cache infrastructure degraded during {Operation}; ErrorType={ErrorType}",
            operation,
            errorType);
    }

    private sealed class SingleFactoryInvocation<T>(Func<CancellationToken, Task<T?>> factory)
    {
        private const int NotStarted = 0;
        private const int Running = 1;
        private const int Succeeded = 2;
        private const int Faulted = 3;

        private readonly Func<CancellationToken, Task<T?>> _factory = factory;
        private readonly TaskCompletionSource<T?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ExceptionDispatchInfo? _capturedFailure;
        private int _state;
        private T? _result;

        public bool HasStarted => Volatile.Read(ref _state) != NotStarted;

        public bool HasCompletedSuccessfully => Volatile.Read(ref _state) == Succeeded;

        public T? Result => _result;

        public Task<T?> Completion => _completion.Task;

        public Task<T?> InvokeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Interlocked.CompareExchange(ref _state, Running, NotStarted) == NotStarted)
            {
                _ = ExecuteAsync(cancellationToken);
            }

            return Completion.WaitAsync(cancellationToken);
        }

        private async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                _result = await _factory(cancellationToken);
                Volatile.Write(ref _state, Succeeded);
                _completion.TrySetResult(_result);
            }
            catch (Exception ex)
            {
                _capturedFailure = ExceptionDispatchInfo.Capture(ex);
                Volatile.Write(ref _state, Faulted);
                _completion.TrySetException(ex);
            }
        }

        public void ThrowCapturedFailure()
        {
            if (Volatile.Read(ref _state) == Faulted)
            {
                _capturedFailure!.Throw();
            }
        }
    }
}
