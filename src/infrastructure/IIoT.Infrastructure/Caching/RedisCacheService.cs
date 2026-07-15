using System.Net;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;

namespace IIoT.Infrastructure.Caching;

/// <summary>
/// Redis 分布式缓存实现类 - 基于 FusionCache（L1 内存 + L2 Redis + Backplane 多实例同步）
/// </summary>
public class RedisCacheService(
    IFusionCache fusionCache,
    IConnectionMultiplexer redis,
    ILogger<RedisCacheService> logger,
    IOptions<DomainEventCacheInvalidationOptions> idempotencyOptions)
    : ICacheService, IIdempotentCacheInvalidationService
{
    private static readonly EventId InfrastructureDegradedEvent = new(2401, "ValueCacheInfrastructureDegraded");
    private static readonly TimeSpan CacheWriteFenceCleanupTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan InvalidationClaimReleaseTimeout = TimeSpan.FromSeconds(2);
    private const string SystemKeyPrefix = "__iiot_system:";
    private const string InvalidationReceiptPrefix =
        SystemKeyPrefix + "domain-event-cache-invalidation:v1:";
    private const string CacheWriteFencePrefix =
        SystemKeyPrefix + "value-cache-write-fence:v1:";
    private const string PatternCacheWriteFenceKey =
        CacheWriteFencePrefix + "patterns";
    private const string CompletedReceiptValue = "completed";
    private const string ClaimScript = """
        local current = redis.call('GET', KEYS[1])
        if current == ARGV[1] then
            return 0
        end
        if not current then
            local acquired = redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3], 'NX')
            if acquired then return 1 end
        end
        return -1
        """;
    private const string RenewScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
            return 1
        end
        return 0
        """;
    private const string CompleteScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3])
            return 1
        end
        return 0
        """;
    private const string ReleaseScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;
    private const string BumpCacheWriteFenceScript = """
        local generation = redis.call('INCR', KEYS[1])
        redis.call('PEXPIRE', KEYS[1], ARGV[1])
        return generation
        """;

    private readonly IFusionCache _fusionCache = fusionCache;
    private readonly IConnectionMultiplexer _redis = redis;
    private readonly ILogger<RedisCacheService> _logger = logger;
    private readonly DomainEventCacheInvalidationOptions _idempotencyOptions =
        (idempotencyOptions ?? throw new ArgumentNullException(nameof(idempotencyOptions))).Value
        ?? throw new ArgumentException(
            "Cache invalidation options value cannot be null.",
            nameof(idempotencyOptions));

    public static string GetDomainEventInvalidationReceiptKey(
        Guid operationId,
        string operationScope)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Cache invalidation operation id cannot be empty.", nameof(operationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(operationScope);
        if (operationScope.Length > 80 ||
            operationScope.Any(static character =>
                !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
        {
            throw new ArgumentException(
                "Cache invalidation operation scope must use 1-80 lowercase ASCII letters, digits, or hyphens.",
                nameof(operationScope));
        }

        return $"{InvalidationReceiptPrefix}{operationId:N}:{operationScope}";
    }

    public async Task<T?> GetOrSetAsync<T>(
        string key, Func<CancellationToken, Task<T?>> factory, Func<T?, bool> shouldCache,
        TimeSpan absoluteExpireTime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(shouldCache);
        if (absoluteExpireTime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(absoluteExpireTime));
        cancellationToken.ThrowIfCancellationRequested();

        var duration = absoluteExpireTime;
        var fenceSnapshot = CacheWriteFenceSnapshot.Unavailable;
        var factoryInvocation = new SingleFactoryInvocation<T>(
            async factoryCancellationToken =>
            {
                fenceSnapshot = await CaptureCacheWriteFenceAsync(key, factoryCancellationToken);
                return await factory(factoryCancellationToken);
            },
            shouldCache);
        var entryOptions = _fusionCache.CreateEntryOptions(duration: duration);
        entryOptions.AllowTimedOutFactoryBackgroundCompletion = false;
        entryOptions.AllowBackgroundDistributedCacheOperations = false;

        try
        {
            var fusionOperation = _fusionCache.GetOrSetAsync<T?>(
                    key,
                    async (context, _) =>
                    {
                        var result = await factoryInvocation.InvokeAsync(cancellationToken);
                        await ApplyPreWriteFenceAsync(
                            context,
                            key,
                            fenceSnapshot,
                            factoryInvocation.ShouldCache);
                        return result;
                    },
                    default(ZiggyCreatures.Caching.Fusion.MaybeValue<T?>),
                    entryOptions,
                    tags: null,
                    token: CancellationToken.None)
                .AsTask();
            var guardedOperation = CompleteFusionOperationAsync(
                fusionOperation,
                key,
                factoryInvocation,
                () => fenceSnapshot);

            try
            {
                return await guardedOperation.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _ = ObserveBackgroundFenceCompletionAsync(guardedOperation);
                throw;
            }
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
                var result = await factoryInvocation.InvokeAsync(cancellationToken);
                return await CompleteGetOrSetAsync(
                    key,
                    result,
                    factoryInvocation,
                    fenceSnapshot);
            }

            if (factoryInvocation.HasCompletedSuccessfully)
            {
                LogInfrastructureDegradation("get-or-set", ex);
                return await CompleteGetOrSetAsync(
                    key,
                    factoryInvocation.Result,
                    factoryInvocation,
                    fenceSnapshot);
            }

            if (ex is SyntheticTimeoutException)
            {
                // FusionCache 2.6 uses this same type for factory and provider synthetic timeouts.
                // Once the factory is running, preserve its configured timeout instead of
                // starting another execution or waiting past the timeout boundary.
                factoryInvocation.ThrowCapturedFailure();
                if (factoryInvocation.HasCompletedSuccessfully)
                {
                    return await CompleteGetOrSetAsync(
                        key,
                        factoryInvocation.Result,
                        factoryInvocation,
                        fenceSnapshot);
                }

                throw;
            }

            // A provider failure that races an already-running factory must not mask that
            // factory's eventual result or business exception. Reuse the same execution.
            LogInfrastructureDegradation("get-or-set", ex);
            var completion = await factoryInvocation.Completion.WaitAsync(cancellationToken);
            return await CompleteGetOrSetAsync(
                key,
                completion,
                factoryInvocation,
                fenceSnapshot);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureBusinessKey(key);

        await BumpCacheWriteFenceForOrdinaryInvalidationAsync(
            GetCacheWriteFenceKey(key),
            cancellationToken);

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
        EnsureBusinessPattern(pattern);

        await BumpCacheWriteFenceForOrdinaryInvalidationAsync(
            PatternCacheWriteFenceKey,
            cancellationToken);

        EndPoint[] endpoints;

        try
        {
            endpoints = _redis.GetEndPoints();
            cancellationToken.ThrowIfCancellationRequested();
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
                    if (!IsBusinessCacheKey(key))
                        continue;
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

    public async Task<bool> InvalidateOnceAsync(
        Guid operationId,
        string operationScope,
        IReadOnlyCollection<string> keys,
        IReadOnlyCollection<string> patterns,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(patterns);
        _idempotencyOptions.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedKeys = keys
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var normalizedPatterns = patterns
            .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedKeys.Any(static key => !IsBusinessCacheKey(key)) ||
            normalizedPatterns.Any(PatternMayTargetSystemNamespace))
        {
            throw new InvalidOperationException(
                "Business cache invalidation must not target the __iiot_system: namespace.");
        }

        var database = _redis.GetDatabase();
        var receiptKey = (RedisKey)GetDomainEventInvalidationReceiptKey(operationId, operationScope);
        var claimValue = (RedisValue)$"processing:{Guid.NewGuid():N}";
        var leaseMilliseconds = ToPositiveMilliseconds(_idempotencyOptions.LeaseDuration);
        var completedMilliseconds = ToPositiveMilliseconds(_idempotencyOptions.CompletedRetention);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claimTask = database.ScriptEvaluateAsync(
                ClaimScript,
                [receiptKey],
                [CompletedReceiptValue, claimValue, leaseMilliseconds]);
            long claimResult;
            try
            {
                claimResult = (long)await claimTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ObserveDetachedInvalidationClaim(
                    claimTask,
                    database,
                    receiptKey,
                    claimValue);
                throw;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                if (claimResult == 1)
                    await ReleaseInvalidationClaimAsync(database, receiptKey, claimValue);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (claimResult == 0)
                return false;
            if (claimResult == 1)
                break;

            await Task.Delay(_idempotencyOptions.ClaimRetryDelay, cancellationToken);
        }

        try
        {
            foreach (var key in normalizedKeys)
            {
                await RenewInvalidationClaimAsync(
                    database,
                    receiptKey,
                    claimValue,
                    leaseMilliseconds,
                    cancellationToken);
                await BumpCacheWriteFenceAsync(
                    database,
                    GetCacheWriteFenceKey(key),
                    completedMilliseconds,
                    cancellationToken);
                await _fusionCache.RemoveAsync(key, token: cancellationToken);
            }

            if (normalizedPatterns.Length > 0)
            {
                await RenewInvalidationClaimAsync(
                    database,
                    receiptKey,
                    claimValue,
                    leaseMilliseconds,
                    cancellationToken);
                await BumpCacheWriteFenceAsync(
                    database,
                    PatternCacheWriteFenceKey,
                    completedMilliseconds,
                    cancellationToken);
            }

            foreach (var pattern in normalizedPatterns)
            {
                foreach (var endpoint in _redis.GetEndPoints())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var server = _redis.GetServer(endpoint);
                    if (!server.IsConnected)
                        throw new RedisConnectionException(
                            ConnectionFailureType.UnableToConnect,
                            $"Redis endpoint {endpoint} is disconnected during strict cache invalidation.");

                    await foreach (var redisKey in server.KeysAsync(pattern: pattern)
                                       .WithCancellation(cancellationToken))
                    {
                        if (!IsBusinessCacheKey(redisKey))
                            continue;
                        await RenewInvalidationClaimAsync(
                            database,
                            receiptKey,
                            claimValue,
                            leaseMilliseconds,
                            cancellationToken);
                        await _fusionCache.RemoveAsync(redisKey.ToString(), token: cancellationToken);
                    }
                }
            }

            var completed = (long)await database.ScriptEvaluateAsync(
                    CompleteScript,
                    [receiptKey],
                    [claimValue, CompletedReceiptValue, completedMilliseconds])
                .WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (completed != 1)
            {
                throw new InvalidOperationException(
                    $"Cache invalidation lease was lost before completion: operationId={operationId}.");
            }

            return true;
        }
        catch
        {
            await ReleaseInvalidationClaimAsync(database, receiptKey, claimValue);
            throw;
        }
    }

    internal static bool IsBusinessCacheKey(RedisKey key)
    {
        return !key.ToString().StartsWith(SystemKeyPrefix, StringComparison.Ordinal);
    }

    internal static bool PatternMayTargetSystemNamespace(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var literalPrefix = new System.Text.StringBuilder(pattern.Length);
        var escaped = false;
        foreach (var character in pattern)
        {
            if (escaped)
            {
                literalPrefix.Append(character);
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character is '*' or '?' or '[')
                break;

            literalPrefix.Append(character);
        }

        if (escaped)
            literalPrefix.Append('\\');

        var prefix = literalPrefix.ToString();
        return prefix.Length == 0 ||
               SystemKeyPrefix.StartsWith(prefix, StringComparison.Ordinal) ||
               prefix.StartsWith(SystemKeyPrefix, StringComparison.Ordinal);
    }

    private static void EnsureBusinessKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!IsBusinessCacheKey(key))
        {
            throw new InvalidOperationException(
                "Business cache operations must not remove keys in the __iiot_system: namespace.");
        }
    }

    private static void EnsureBusinessPattern(string pattern)
    {
        if (PatternMayTargetSystemNamespace(pattern))
        {
            throw new InvalidOperationException(
                "Business cache patterns must not be able to match the __iiot_system: namespace.");
        }
    }

    private static async Task RenewInvalidationClaimAsync(
        IDatabase database,
        RedisKey receiptKey,
        RedisValue claimValue,
        long leaseMilliseconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var renewed = (long)await database.ScriptEvaluateAsync(
                RenewScript,
                [receiptKey],
                [claimValue, leaseMilliseconds])
            .WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (renewed != 1)
            throw new InvalidOperationException("Cache invalidation lease was lost while applying side effects.");
    }

    private async Task ReleaseInvalidationClaimAsync(
        IDatabase database,
        RedisKey receiptKey,
        RedisValue claimValue)
    {
        using var cleanup = new CancellationTokenSource(InvalidationClaimReleaseTimeout);
        try
        {
            await database.ScriptEvaluateAsync(
                    ReleaseScript,
                    [receiptKey],
                    [claimValue])
                .WaitAsync(cleanup.Token);
        }
        catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
        {
            LogInfrastructureDegradation(
                "domain-event-invalidation:release",
                "ReleaseCleanupTimeout");
        }
        catch (Exception releaseException)
        {
            // Claim release is a compare-delete cleanup. Its infrastructure failure must
            // remain observable without replacing the caller cancellation/business failure
            // that caused this path.
            LogInfrastructureDegradation(
                "domain-event-invalidation:release",
                releaseException);
        }
    }

    private void ObserveDetachedInvalidationClaim(
        Task<RedisResult> claimTask,
        IDatabase database,
        RedisKey receiptKey,
        RedisValue claimValue)
    {
        var cleanupTask = claimTask.ContinueWith(
                async completedTask =>
                {
                    if (completedTask.IsFaulted)
                    {
                        var exception = completedTask.Exception?.GetBaseException();
                        _ = completedTask.Exception;
                        if (exception is not null)
                        {
                            LogInfrastructureDegradation(
                                "domain-event-invalidation:late-claim-observation",
                                exception);
                        }
                        return;
                    }

                    if (!completedTask.IsCompletedSuccessfully)
                        return;

                    long claimResult;
                    try
                    {
                        claimResult = (long)completedTask.Result;
                    }
                    catch (Exception exception)
                    {
                        LogInfrastructureDegradation(
                            "domain-event-invalidation:late-claim-observation",
                            exception);
                        return;
                    }

                    if (claimResult == 1)
                        await ReleaseInvalidationClaimAsync(database, receiptKey, claimValue);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default)
            .Unwrap();
        ObserveDetachedTask(cleanupTask);
    }

    private static void ObserveDetachedTask(Task task)
    {
        _ = task.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private async Task<CacheWriteFenceSnapshot> CaptureCacheWriteFenceAsync(
        string key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var generations = await _redis.GetDatabase()
                .StringGetAsync([GetCacheWriteFenceKey(key), PatternCacheWriteFenceKey])
                .WaitAsync(cancellationToken);
            return new CacheWriteFenceSnapshot(true, generations[0], generations[1]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsDegradableRedisScanFailure(ex))
        {
            LogInfrastructureDegradation("get-or-set:fence-read", ex);
            return CacheWriteFenceSnapshot.Unavailable;
        }
    }

    private async Task<T?> CompleteGetOrSetAsync<T>(
        string key,
        T? result,
        SingleFactoryInvocation<T> factoryInvocation,
        CacheWriteFenceSnapshot before)
    {
        if (!factoryInvocation.HasCompletedSuccessfully)
            return result;

        using var cleanup = new CancellationTokenSource(CacheWriteFenceCleanupTimeout);
        var after = await CaptureCacheWriteFenceForCleanupAsync(key, cleanup.Token);
        if (before.IsAvailable && after.IsAvailable && before.HasSameGeneration(after))
            return result;

        try
        {
            // Invalidation advances its generation before removing the cache entry. If an
            // older factory returns after that remove, FusionCache may write the old value
            // back; the post-write fence removes that value from L1/L2 and the backplane.
            await _fusionCache.RemoveAsync(key, token: cleanup.Token);
        }
        catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
        {
            LogInfrastructureDegradation("get-or-set:fence-remove", "FenceCleanupTimeout");
        }
        catch (Exception ex) when (IsDegradableValueCacheFailure(ex))
        {
            LogInfrastructureDegradation("get-or-set:fence-remove", ex);
        }

        return result;
    }

    private async Task ApplyPreWriteFenceAsync<T>(
        FusionCacheFactoryExecutionContext<T?>? context,
        string key,
        CacheWriteFenceSnapshot before,
        bool shouldCache)
    {
        if (!shouldCache)
        {
            DisableCacheWrite(context);
            return;
        }

        using var cleanup = new CancellationTokenSource(CacheWriteFenceCleanupTimeout);
        var after = await CaptureCacheWriteFenceForCleanupAsync(key, cleanup.Token);
        if (before.IsAvailable && after.IsAvailable && before.HasSameGeneration(after))
            return;

        DisableCacheWrite(context);
    }

    private static void DisableCacheWrite<T>(FusionCacheFactoryExecutionContext<T?>? context)
    {
        if (context is null)
            return;

        context.Options.SetSkipMemoryCacheWrite(true);
        context.Options.SetSkipDistributedCacheWrite(true, skipBackplaneNotifications: true);
    }

    private async Task<CacheWriteFenceSnapshot> CaptureCacheWriteFenceForCleanupAsync(
        string key,
        CancellationToken cleanupToken)
    {
        try
        {
            return await CaptureCacheWriteFenceAsync(key, cleanupToken);
        }
        catch (OperationCanceledException) when (cleanupToken.IsCancellationRequested)
        {
            LogInfrastructureDegradation("get-or-set:fence-read", "FenceCleanupTimeout");
            return CacheWriteFenceSnapshot.Unavailable;
        }
    }

    private async Task<T?> CompleteFusionOperationAsync<T>(
        Task<T?> fusionOperation,
        string key,
        SingleFactoryInvocation<T> factoryInvocation,
        Func<CacheWriteFenceSnapshot> getFenceSnapshot)
    {
        var result = await fusionOperation;
        return await CompleteGetOrSetAsync(
            key,
            result,
            factoryInvocation,
            getFenceSnapshot());
    }

    private async Task ObserveBackgroundFenceCompletionAsync<T>(Task<T> operation)
    {
        try
        {
            await operation;
        }
        catch (OperationCanceledException)
        {
            // The caller already observed its own cancellation; this observes the provider task.
        }
        catch (Exception ex)
        {
            LogInfrastructureDegradation("get-or-set:background-fence", ex.GetType().Name);
        }
    }

    private static async Task BumpCacheWriteFenceAsync(
        IDatabase database,
        RedisKey fenceKey,
        long retentionMilliseconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await database.ScriptEvaluateAsync(
                BumpCacheWriteFenceScript,
                [fenceKey],
                [retentionMilliseconds])
            .WaitAsync(cancellationToken);
    }

    private Task BumpCacheWriteFenceForOrdinaryInvalidationAsync(
        RedisKey fenceKey,
        CancellationToken cancellationToken)
    {
        _idempotencyOptions.Validate();
        return BumpCacheWriteFenceAsync(
            _redis.GetDatabase(),
            fenceKey,
            ToPositiveMilliseconds(_idempotencyOptions.CompletedRetention),
            cancellationToken);
    }

    private static RedisKey GetCacheWriteFenceKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return $"{CacheWriteFencePrefix}key:{digest}";
    }

    private static long ToPositiveMilliseconds(TimeSpan duration)
    {
        var milliseconds = checked((long)Math.Ceiling(duration.TotalMilliseconds));
        return Math.Max(1, milliseconds);
    }

    private readonly record struct CacheWriteFenceSnapshot(
        bool IsAvailable,
        RedisValue KeyGeneration,
        RedisValue PatternGeneration)
    {
        public static CacheWriteFenceSnapshot Unavailable => new(false, default, default);

        public bool HasSameGeneration(CacheWriteFenceSnapshot other) =>
            KeyGeneration == other.KeyGeneration &&
            PatternGeneration == other.PatternGeneration;
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

    private sealed class SingleFactoryInvocation<T>(
        Func<CancellationToken, Task<T?>> factory,
        Func<T?, bool> shouldCache)
    {
        private const int NotStarted = 0;
        private const int Running = 1;
        private const int Succeeded = 2;
        private const int Faulted = 3;

        private readonly Func<CancellationToken, Task<T?>> _factory = factory;
        private readonly Func<T?, bool> _shouldCache = shouldCache;
        private readonly TaskCompletionSource<T?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private ExceptionDispatchInfo? _capturedFailure;
        private int _state;
        private T? _result;
        private bool _cacheResult;

        public bool HasStarted => Volatile.Read(ref _state) != NotStarted;

        public bool HasCompletedSuccessfully => Volatile.Read(ref _state) == Succeeded;

        public T? Result => _result;

        public bool ShouldCache => _cacheResult;

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
                _cacheResult = _shouldCache(_result);
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
