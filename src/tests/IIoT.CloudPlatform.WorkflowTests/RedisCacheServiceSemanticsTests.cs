using System.Net;
using System.Runtime.CompilerServices;
using IIoT.Infrastructure.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace IIoT.CloudPlatform.WorkflowTests;

public sealed class RedisCacheServiceSemanticsTests
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task RemoveAsync_CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var fusion = new Mock<IFusionCache>(MockBehavior.Strict);
        fusion.Setup(cache => cache.RemoveAsync(
                "key",
                It.IsAny<FusionCacheEntryOptions>(),
                cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        var sut = Create(fusion);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RemoveAsync("key", cancellation.Token));
    }

    [Fact]
    public async Task RemoveAsync_SystemNamespaceKey_IsRejectedBeforeFusionCache()
    {
        var fusion = CreateFusionMock();
        var sut = Create(fusion);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RemoveAsync("__iiot_system:protected"));

        fusion.Verify(cache => cache.RemoveAsync(
            It.IsAny<string>(),
            It.IsAny<FusionCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveByPatternAsync_SystemMatchingGlobs_AreRejectedBeforeRedisScan()
    {
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        var sut = Create(CreateFusionMock(), redis);
        var unsafePatterns = new[]
        {
            "*",
            "__iiot_system:*",
            "__iiot?system:*",
            "__iiot[_x]system:*"
        };

        foreach (var pattern in unsafePatterns)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.RemoveByPatternAsync(pattern));
            Assert.True(RedisCacheService.PatternMayTargetSystemNamespace(pattern));
        }

        redis.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidateOnceAsync_NormalizedInputsRejectEntireSystemNamespaceAndMatchingGlobs()
    {
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        var sut = Create(CreateFusionMock(), redis);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InvalidateOnceAsync(
            Guid.NewGuid(),
            "direct-system-key",
            ["__iiot_system:not-a-receipt"],
            []));

        foreach (var pattern in new[] { "*", "__iiot_system:*", "__iiot?system:*", "__iiot[_x]system:*" })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => sut.InvalidateOnceAsync(
                Guid.NewGuid(),
                "system-pattern",
                [],
                [pattern]));
        }

        redis.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidationReceiptAndOptions_InvalidInputsFailClosedBeforeRedis()
    {
        Assert.Throws<ArgumentException>(() =>
            RedisCacheService.GetDomainEventInvalidationReceiptKey(Guid.Empty, "valid-scope"));
        Assert.Throws<ArgumentException>(() =>
            RedisCacheService.GetDomainEventInvalidationReceiptKey(Guid.NewGuid(), ""));
        Assert.Throws<ArgumentException>(() =>
            RedisCacheService.GetDomainEventInvalidationReceiptKey(Guid.NewGuid(), new string('a', 81)));
        Assert.Throws<ArgumentException>(() =>
            RedisCacheService.GetDomainEventInvalidationReceiptKey(Guid.NewGuid(), "Invalid_Scope"));
        Assert.EndsWith(
            ":valid-scope",
            RedisCacheService.GetDomainEventInvalidationReceiptKey(Guid.NewGuid(), "valid-scope"));

        Assert.False(RedisCacheService.PatternMayTargetSystemNamespace("business:*"));
        Assert.False(RedisCacheService.PatternMayTargetSystemNamespace("business:\\*"));
        Assert.False(RedisCacheService.PatternMayTargetSystemNamespace("business:\\"));
        Assert.True(RedisCacheService.PatternMayTargetSystemNamespace("__iiot_system:\\*"));
        Assert.Throws<ArgumentException>(() => RedisCacheService.PatternMayTargetSystemNamespace(""));

        static DomainEventCacheInvalidationOptions Options(
            TimeSpan? lease = null,
            TimeSpan? retention = null,
            TimeSpan? retry = null) => new()
            {
                LeaseDuration = lease ?? TimeSpan.FromSeconds(1),
                CompletedRetention = retention ?? TimeSpan.FromDays(7),
                ClaimRetryDelay = retry ?? TimeSpan.FromMilliseconds(1)
            };

        Assert.Throws<InvalidOperationException>(() =>
            Options(lease: TimeSpan.Zero).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            Options(retention: TimeSpan.FromDays(7) - TimeSpan.FromTicks(1)).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            Options(retry: TimeSpan.Zero).Validate());
        Assert.Throws<InvalidOperationException>(() =>
            Options(lease: TimeSpan.FromMilliseconds(1), retry: TimeSpan.FromMilliseconds(1)).Validate());
        Options().Validate();

        Assert.Throws<ArgumentNullException>(() => new RedisCacheService(
            CreateFusionMock().Object,
            new Mock<IConnectionMultiplexer>(MockBehavior.Strict).Object,
            NullLogger<RedisCacheService>.Instance,
            null!));

        var nullValueOptions = new Mock<IOptions<DomainEventCacheInvalidationOptions>>(MockBehavior.Strict);
        nullValueOptions.SetupGet(value => value.Value).Returns((DomainEventCacheInvalidationOptions)null!);
        var nullValueError = Assert.Throws<ArgumentException>(() => new RedisCacheService(
            CreateFusionMock().Object,
            new Mock<IConnectionMultiplexer>(MockBehavior.Strict).Object,
            NullLogger<RedisCacheService>.Instance,
            nullValueOptions.Object));
        Assert.Equal("idempotencyOptions", nullValueError.ParamName);

        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        var sut = Create(CreateFusionMock(), redis);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ => Task.FromResult<string?>("value"),
            static value => value is not null,
            TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.InvalidateOnceAsync(
            Guid.NewGuid(),
            "null-keys",
            null!,
            []));
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.InvalidateOnceAsync(
            Guid.NewGuid(),
            "null-patterns",
            [],
            null!));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.InvalidateOnceAsync(
            Guid.NewGuid(),
            "pre-cancelled",
            [],
            [],
            cancellation.Token));
        redis.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidateOnceAsync_ClaimRetryCompletedReceiptAndLeaseLossAreExplicit()
    {
        static Mock<IConnectionMultiplexer> Redis(Mock<IDatabase> database)
        {
            var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
            redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
                .Returns(database.Object);
            return redis;
        }

        var completedDatabase = CreateScriptDatabase(0L);
        var completedRedis = Redis(completedDatabase);
        var completed = await Create(CreateFusionMock(), completedRedis).InvalidateOnceAsync(
            Guid.NewGuid(),
            "already-completed",
            [],
            []);
        Assert.False(completed);

        var retryDatabase = CreateScriptDatabase(-1L, 1L, 1L, 1L, 1L);
        var retryRedis = Redis(retryDatabase);
        var retryOptions = Options.Create(new DomainEventCacheInvalidationOptions
        {
            LeaseDuration = TimeSpan.FromMilliseconds(2),
            ClaimRetryDelay = TimeSpan.FromTicks(1),
            CompletedRetention = TimeSpan.FromDays(7)
        });
        var retryFusion = CreateFusionMock();
        retryFusion.Setup(cache => cache.RemoveAsync(
                "business:key",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        var retried = await Create(retryFusion, retryRedis, logger: null, idempotencyOptions: retryOptions)
            .InvalidateOnceAsync(Guid.NewGuid(), "claim-retry", ["", "business:key", "business:key"], [""]);
        Assert.True(retried);

        var renewFailureDatabase = CreateScriptDatabase(1L, 0L, 1L);
        var renewFailureRedis = Redis(renewFailureDatabase);
        var renewFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(CreateFusionMock(), renewFailureRedis).InvalidateOnceAsync(
                Guid.NewGuid(),
                "renew-failure",
                ["business:key"],
                []));
        Assert.Contains("lease was lost", renewFailure.Message, StringComparison.Ordinal);

        var completionFailureDatabase = CreateScriptDatabase(1L, 0L, 1L);
        var completionFailureRedis = Redis(completionFailureDatabase);
        var completionFailure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(CreateFusionMock(), completionFailureRedis).InvalidateOnceAsync(
                Guid.NewGuid(),
                "completion-failure",
                [],
                []));
        Assert.Contains("before completion", completionFailure.Message, StringComparison.Ordinal);

        var claimEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateClaimReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingClaim = new TaskCompletionSource<RedisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingRelease = new TaskCompletionSource<RedisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var claimScriptCalls = 0;
        string? observedClaimScript = null;
        string? observedReleaseScript = null;
        RedisKey[]? observedClaimKeys = null;
        RedisKey[]? observedReleaseKeys = null;
        RedisValue[]? observedClaimValues = null;
        RedisValue[]? observedReleaseValues = null;
        var claimDatabase = new Mock<IDatabase>(MockBehavior.Strict);
        claimDatabase.Setup(value => value.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Returns((string script, RedisKey[] keys, RedisValue[] values, CommandFlags _) =>
            {
                var call = Interlocked.Increment(ref claimScriptCalls);
                if (call == 1)
                {
                    observedClaimScript = script;
                    observedClaimKeys = keys;
                    observedClaimValues = values;
                    claimEntered.TrySetResult();
                    return pendingClaim.Task;
                }

                observedReleaseScript = script;
                observedReleaseKeys = keys;
                observedReleaseValues = values;
                releaseEntered.TrySetResult();
                return CompleteReleaseAsync();
            });
        async Task<RedisResult> CompleteReleaseAsync()
        {
            var result = await pendingRelease.Task;
            lateClaimReleased.TrySetResult();
            return result;
        }
        using var claimCancellation = new CancellationTokenSource();
        var claimOperation = Create(CreateFusionMock(), Redis(claimDatabase)).InvalidateOnceAsync(
            Guid.NewGuid(),
            "claim-in-flight-cancellation",
            [],
            [],
            claimCancellation.Token);
        await claimEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        claimCancellation.Cancel();
        Exception? claimCancellationResult;
        try
        {
            claimCancellationResult = await Record.ExceptionAsync(
                () => claimOperation.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            pendingClaim.TrySetResult(RedisResult.Create((RedisValue)1L));
        }
        Assert.Equal(
            claimCancellation.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(claimCancellationResult).CancellationToken);
        await releaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.NotNull(observedClaimScript);
        Assert.NotNull(observedReleaseScript);
        Assert.NotEqual(observedClaimScript, observedReleaseScript);
        Assert.Contains("redis.call('DEL'", observedReleaseScript, StringComparison.Ordinal);
        Assert.Equal(Assert.Single(observedClaimKeys!), Assert.Single(observedReleaseKeys!));
        Assert.Equal(3, observedClaimValues!.Length);
        Assert.Equal(observedClaimValues[1], Assert.Single(observedReleaseValues!));
        pendingRelease.TrySetResult(RedisResult.Create((RedisValue)1L));
        await lateClaimReleased.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(2, claimScriptCalls);

        var completionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingCompletion = new TaskCompletionSource<RedisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completionScriptCalls = 0;
        var cancellationDatabase = new Mock<IDatabase>(MockBehavior.Strict);
        cancellationDatabase.Setup(value => value.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Returns(() =>
            {
                var call = Interlocked.Increment(ref completionScriptCalls);
                if (call == 1)
                    return Task.FromResult(RedisResult.Create((RedisValue)1L));
                if (call == 2)
                {
                    completionEntered.TrySetResult();
                    return pendingCompletion.Task;
                }

                return Task.FromResult(RedisResult.Create((RedisValue)1L));
            });
        using var completionCancellation = new CancellationTokenSource();
        var completionOperation = Create(CreateFusionMock(), Redis(cancellationDatabase)).InvalidateOnceAsync(
            Guid.NewGuid(),
            "complete-in-flight-cancellation",
            [],
            [],
            completionCancellation.Token);
        await completionEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        completionCancellation.Cancel();
        Exception? completionCancellationResult;
        try
        {
            completionCancellationResult = await Record.ExceptionAsync(
                () => completionOperation.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            pendingCompletion.TrySetResult(RedisResult.Create((RedisValue)1L));
        }
        Assert.Equal(
            completionCancellation.Token,
            Assert.IsAssignableFrom<OperationCanceledException>(completionCancellationResult).CancellationToken);
    }

    [Fact]
    public async Task InvalidateOnceAsync_DisconnectedPatternScanReleasesClaimAndPropagates()
    {
        var database = CreateScriptDatabase(1L, 1L, 1L, 1L);
        var endpoint = new DnsEndPoint("redis.invalid", 6379);
        var server = new Mock<IServer>(MockBehavior.Strict);
        server.SetupGet(value => value.IsConnected).Returns(false);
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        redis.Setup(connection => connection.GetEndPoints(It.IsAny<bool>()))
            .Returns([endpoint]);
        redis.Setup(connection => connection.GetServer(endpoint, It.IsAny<object?>()))
            .Returns(server.Object);

        await Assert.ThrowsAsync<RedisConnectionException>(() =>
            Create(CreateFusionMock(), redis).InvalidateOnceAsync(
                Guid.NewGuid(),
                "disconnected-scan",
                [],
                ["business:*"]));
        database.Verify(value => value.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()), Times.Exactly(4));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidateOnceAsync_ReleaseFailureDoesNotMaskPrimaryFailureOrCallerCancellation(
        bool cancelCaller)
    {
        using var cancellation = new CancellationTokenSource();
        var releaseFailure = new RedisConnectionException(
            ConnectionFailureType.UnableToConnect,
            "receipt compare-delete failed");
        Exception primaryFailure = cancelCaller
            ? new OperationCanceledException("caller cancelled", cancellation.Token)
            : new InvalidOperationException("cache side effect failed");
        var releaseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingRelease = new TaskCompletionSource<RedisResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var scriptCalls = 0;
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database.Setup(value => value.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Returns(() =>
            {
                var call = Interlocked.Increment(ref scriptCalls);
                if (call < 4)
                    return Task.FromResult(RedisResult.Create((RedisValue)1L));

                releaseEntered.TrySetResult();
                return cancelCaller
                    ? pendingRelease.Task
                    : Task.FromException<RedisResult>(releaseFailure);
            });
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.RemoveAsync(
                "business:key",
                It.IsAny<FusionCacheEntryOptions>(),
                cancellation.Token))
            .Returns(() =>
            {
                if (cancelCaller)
                    cancellation.Cancel();
                return ValueTask.FromException(primaryFailure);
            });
        var logger = new RecordingLogger<RedisCacheService>();
        var sut = Create(fusion, redis, logger);

        var operation = sut.InvalidateOnceAsync(
            Guid.NewGuid(),
            "release-failure",
            ["business:key"],
            [],
            cancellation.Token);
        if (cancelCaller)
            await releaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Exception? actual;
        try
        {
            actual = await Record.ExceptionAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            pendingRelease.TrySetResult(RedisResult.Create((RedisValue)1L));
        }

        Assert.Same(primaryFailure, actual);
        if (cancelCaller)
            Assert.Equal(cancellation.Token, Assert.IsType<OperationCanceledException>(actual).CancellationToken);
        var warning = Assert.Single(logger.Entries);
        Assert.Equal(2401, warning.EventId.Id);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal(
            "Value cache infrastructure degraded during domain-event-invalidation:release; " +
            (cancelCaller
                ? "ErrorType=ReleaseCleanupTimeout"
                : "ErrorType=RedisConnectionException"),
            warning.Message);
        Assert.Null(warning.Exception);
    }

    [Fact]
    public async Task RemoveByPatternAsync_PreCanceled_DoesNotTouchRedisAndPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        var sut = Create(new Mock<IFusionCache>(MockBehavior.Strict), redis);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RemoveByPatternAsync("prefix:*", cancellation.Token));

        redis.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetOrSetAsync_CallerCancellation_DoesNotInvokeFactory()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factoryCalls = 0;
        var fusion = new Mock<IFusionCache>(MockBehavior.Strict);
        fusion.Setup(cache => cache.GetOrSetAsync<string?>(
                "key",
                It.IsAny<Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>>(),
                It.IsAny<MaybeValue<string?>>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        var sut = Create(fusion);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                return Task.FromResult<string?>("value");
            },
            static value => value is not null,
            CacheDuration,
            cancellationToken: cancellation.Token));
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryException_PropagatesSameInstanceAndRunsOnce()
    {
        var expected = new InvalidOperationException("database failed");
        var second = new InvalidOperationException("factory was repeated");
        var factoryCalls = 0;
        var fusion = CreateFactoryPassThroughFusionMock();

        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ => Task.FromException<string?>(factoryCalls++ == 0 ? expected : second),
            static value => value is not null,
            CacheDuration));

        Assert.Same(expected, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_ShouldCachePolicyFalse_ReturnsValueAndEvaluatesPolicyOnce()
    {
        var factoryCalls = 0;
        var policyCalls = 0;
        var fusion = CreateFactoryPassThroughFusionMock();
        var sut = Create(fusion);

        var actual = await sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                return Task.FromResult<string?>("not-admitted");
            },
            value =>
            {
                policyCalls++;
                Assert.Equal("not-admitted", value);
                return false;
            },
            CacheDuration);

        Assert.Equal("not-admitted", actual);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(1, policyCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_ShouldCachePolicyException_PropagatesSameInstanceAndRunsOnce()
    {
        var expected = new InvalidOperationException("cache admission policy failed");
        var factoryCalls = 0;
        var policyCalls = 0;
        var fusion = CreateFactoryPassThroughFusionMock();
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                return Task.FromResult<string?>("value");
            },
            _ =>
            {
                policyCalls++;
                throw expected;
            },
            CacheDuration));

        Assert.Same(expected, actual);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(1, policyCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFailureBeforeFactory_FallsBackExactlyOnce()
    {
        var factoryCalls = 0;
        var fusion = CreateGetOrSetFusionMock((_, _) =>
            ValueTask.FromException<string?>(
                new FusionCacheDistributedCacheException("redis read failed")));
        var sut = Create(fusion);

        var actual = await sut.GetOrSetAsync<string>("key", _ =>
        {
            factoryCalls++;
            return Task.FromResult<string?>("database value");
        }, static value => value is not null, CacheDuration);

        Assert.Equal("database value", actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFailureBeforeFactory_FallbackExceptionPropagatesSameInstance()
    {
        var expected = new InvalidOperationException("database fallback failed");
        var factoryCalls = 0;
        var fusion = CreateGetOrSetFusionMock((_, _) =>
            ValueTask.FromException<string?>(
                new FusionCacheDistributedCacheException("redis read failed")));
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                return Task.FromException<string?>(expected);
            },
            static value => value is not null,
            CacheDuration));

        Assert.Same(expected, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryCacheLikeException_IsStillFactoryFailureAndPropagatesSameInstance()
    {
        var expected = new FusionCacheDistributedCacheException("business factory used a cache-like type");
        var factoryCalls = 0;
        var fusion = CreateFactoryPassThroughFusionMock();
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<FusionCacheDistributedCacheException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                return Task.FromException<string?>(expected);
            },
            static value => value is not null,
            CacheDuration));

        Assert.Same(expected, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryCallerCancellation_PropagatesSameInstanceAndRunsOnce()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new OperationCanceledException(cancellation.Token);
        var factoryCalls = 0;
        var fusion = CreateFactoryPassThroughFusionMock();
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                cancellation.Cancel();
                throw expected;
            },
            static value => value is not null,
            CacheDuration,
            cancellationToken: cancellation.Token));

        Assert.Same(expected, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_ProviderReplacesFactoryCancellation_PropagatesOriginalFactoryInstanceAndRunsOnce()
    {
        using var factoryCancellation = new CancellationTokenSource();
        using var providerCancellation = new CancellationTokenSource();
        factoryCancellation.Cancel();
        providerCancellation.Cancel();
        var factoryFailure = new OperationCanceledException("factory cancelled", factoryCancellation.Token);
        var providerFailure = new OperationCanceledException("provider replaced cancellation", providerCancellation.Token);
        var factoryCalls = 0;
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.GetOrSetAsync<string?>(
                "key",
                It.IsAny<Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>>(),
                It.IsAny<MaybeValue<string?>>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>, MaybeValue<string?>, FusionCacheEntryOptions, IEnumerable<string>?, CancellationToken>(
                async (_, factory, _, _, _, token) =>
                {
                    try
                    {
                        await factory(default!, token);
                        return null;
                    }
                    catch (OperationCanceledException)
                    {
                        throw providerFailure;
                    }
                });
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                return Task.FromException<string?>(factoryFailure);
            },
            static value => value is not null,
            CacheDuration));

        Assert.Same(factoryFailure, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFailureAfterFactorySuccess_ReturnsValueAndDoesNotRepeatFactory()
    {
        var factoryCalls = 0;
        var fusion = CreateGetOrSetFusionMock(async (factory, token) =>
        {
            await factory(default!, token);
            throw new FusionCacheBackplaneException("backplane write failed");
        });
        var sut = Create(fusion);

        var actual = await sut.GetOrSetAsync<string>("key", _ =>
        {
            factoryCalls++;
            return Task.FromResult<string?>("database value");
        }, static value => value is not null, CacheDuration);

        Assert.Equal("database value", actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_CancellationDuringPostWriteFence_CleanupTimeoutStillRemovesStaleEntryAndPropagatesCallerToken()
    {
        using var cancellation = new CancellationTokenSource();
        var postWriteFenceRead = new TaskCompletionSource<RedisValue[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var staleEntryRemoved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var generationReads = 0;
        var factoryCalls = 0;
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database.Setup(value => value.StringGetAsync(
                It.IsAny<RedisKey[]>(),
                It.IsAny<CommandFlags>()))
            .Returns<RedisKey[], CommandFlags>((_, _) =>
            {
                var read = Interlocked.Increment(ref generationReads);
                if (read == 1)
                    return Task.FromResult<RedisValue[]>(["0", RedisValue.Null]);
                if (read == 2)
                    return Task.FromResult<RedisValue[]>(["1", RedisValue.Null]);

                cancellation.Cancel();
                return postWriteFenceRead.Task;
            });
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        var fusion = CreateFactoryPassThroughFusionMock();
        fusion.Setup(cache => cache.RemoveAsync(
                "key",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => staleEntryRemoved.TrySetResult())
            .Returns(ValueTask.CompletedTask);
        var sut = Create(fusion, redis);

        var operation = sut.GetOrSetAsync<string>(
            "key",
            _ => Task.FromResult<string?>($"value-{++factoryCalls}"),
            static value => value is not null,
            CacheDuration,
            cancellationToken: cancellation.Token);
        var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        Assert.Equal(cancellation.Token, actual.CancellationToken);
        Assert.Equal(1, factoryCalls);
        await staleEntryRemoved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        postWriteFenceRead.TrySetResult(["1", RedisValue.Null]);
        Assert.Equal(3, generationReads);
        fusion.Verify(cache => cache.RemoveAsync(
            "key",
            It.IsAny<FusionCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFailureCannotMaskFactoryException()
    {
        var expected = new InvalidOperationException("database failed");
        var factoryCalls = 0;
        var fusion = CreateGetOrSetFusionMock(async (factory, token) =>
        {
            try
            {
                await factory(default!, token);
            }
            catch
            {
                throw new FusionCacheDistributedCacheException("cache wrapper replaced factory failure");
            }

            return null;
        });
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                return Task.FromException<string?>(expected);
            },
            static value => value is not null,
            CacheDuration));

        Assert.Same(expected, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_ProviderFailureWhileFactoryRuns_AwaitsSameFactoryTask()
    {
        var factoryCompletion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        var fusion = CreateGetOrSetFusionMock((factory, token) =>
        {
            _ = factory(default!, token);
            return ValueTask.FromException<string?>(
                new FusionCacheDistributedCacheException("redis failed while factory was running"));
        });
        var sut = Create(fusion);

        var operation = sut.GetOrSetAsync<string>("key", _ =>
        {
            factoryCalls++;
            return factoryCompletion.Task;
        }, static value => value is not null, CacheDuration);
        Assert.False(operation.IsCompleted);

        factoryCompletion.SetResult("database value");
        var actual = await operation;

        Assert.Equal("database value", actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_ProviderFailureWhileFactoryRuns_PropagatesSameFactoryException()
    {
        var expected = new InvalidOperationException("late database failure");
        var factoryCompletion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        var fusion = CreateGetOrSetFusionMock((factory, token) =>
        {
            _ = factory(default!, token);
            return ValueTask.FromException<string?>(
                new FusionCacheBackplaneException("backplane failed while factory was running"));
        });
        var sut = Create(fusion);

        var operation = sut.GetOrSetAsync<string>("key", _ =>
        {
            factoryCalls++;
            return factoryCompletion.Task;
        }, static value => value is not null, CacheDuration);
        factoryCompletion.SetException(expected);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => operation);

        Assert.Same(expected, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_ProviderFailureWhileFactoryIgnoresToken_CallerCancellationStillPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var factoryCompletion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        var fusion = CreateGetOrSetFusionMock((factory, token) =>
        {
            _ = factory(default!, token);
            return ValueTask.FromException<string?>(
                new FusionCacheDistributedCacheException("redis failed while factory was running"));
        });
        var sut = Create(fusion);
        var operation = sut.GetOrSetAsync<string>("key", _ =>
        {
            factoryCalls++;
            return factoryCompletion.Task;
        }, static value => value is not null, CacheDuration, cancellationToken: cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, factoryCalls);
        factoryCompletion.SetResult("late result");
    }

    [Fact]
    public async Task GetOrSetAsync_RepeatedProviderDelegateInvocation_ReusesSingleFactoryExecution()
    {
        var factoryCalls = 0;
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.GetOrSetAsync<string?>(
                "key",
                It.IsAny<Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>>(),
                It.IsAny<MaybeValue<string?>>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>, MaybeValue<string?>, FusionCacheEntryOptions, IEnumerable<string>?, CancellationToken>(
                (_, factory, _, _, _, token) => new ValueTask<string?>(InvokeProviderDelegateTwice(factory, token)));
        var sut = Create(fusion);

        var actual = await sut.GetOrSetAsync<string>("key", async _ =>
        {
            factoryCalls++;
            await Task.Yield();
            return "database value";
        }, static value => value is not null, CacheDuration);

        Assert.Equal("database value", actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_SyntheticTimeoutWhileFactoryRuns_PropagatesTimeoutWithoutSecondFactory()
    {
        var timeout = new SyntheticTimeoutException("factory hard timeout");
        var factoryCompletion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryCalls = 0;
        var fusion = CreateGetOrSetFusionMock((factory, token) =>
        {
            _ = factory(default!, token);
            return ValueTask.FromException<string?>(timeout);
        });
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<SyntheticTimeoutException>(
            () => sut.GetOrSetAsync<string>("key", _ =>
            {
                factoryCalls++;
                return factoryCompletion.Task;
            },
            static value => value is not null,
            CacheDuration));

        Assert.Same(timeout, actual);
        Assert.Equal(1, factoryCalls);
        Assert.False(factoryCompletion.Task.IsCompleted);
        factoryCompletion.SetResult("observed background completion");
    }

    [Fact]
    public async Task GetOrSetAsync_SerializationFailure_DoesNotInvokeFactory()
    {
        var expected = new FusionCacheSerializationException("bad cache payload");
        var factoryCalls = 0;
        var fusion = CreateGetOrSetFusionMock((_, _) =>
            ValueTask.FromException<string?>(expected));
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<FusionCacheSerializationException>(
            () => sut.GetOrSetAsync<string>("key", _ =>
            {
                factoryCalls++;
                return Task.FromResult<string?>("value");
            },
            static value => value is not null,
            CacheDuration));

        Assert.Same(expected, actual);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_UnknownProviderException_PropagatesWithoutInvokingFactory()
    {
        var expected = new InvalidOperationException("unknown provider failure");
        var factoryCalls = 0;
        var fusion = CreateGetOrSetFusionMock((_, _) =>
            ValueTask.FromException<string?>(expected));
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                return Task.FromResult<string?>("value");
            },
            static value => value is not null,
            CacheDuration));

        Assert.Same(expected, actual);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task RemoveAsync_UnknownException_Propagates()
    {
        var expected = new InvalidOperationException("programming fault");
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.RemoveAsync(
                "key",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RemoveAsync("key"));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task RemoveAsync_BumpsExactFenceBeforeRemovingValue()
    {
        var callOrder = new List<string>();
        RedisKey[]? bumpedKeys = null;
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database.Setup(value => value.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, keys, _, _) =>
            {
                bumpedKeys = keys;
                callOrder.Add("fence");
            })
            .ReturnsAsync(RedisResult.Create((RedisValue)1L));
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.RemoveAsync(
                "business:key",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("remove"))
            .Returns(ValueTask.CompletedTask);

        await Create(fusion, redis).RemoveAsync("business:key");

        Assert.Equal(["fence", "remove"], callOrder);
        var fenceKey = Assert.Single(bumpedKeys!);
        Assert.StartsWith(
            "__iiot_system:value-cache-write-fence:v1:key:",
            fenceKey.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveAsync_FenceConnectionFailureCannotReportSuccessToBlockedFactory()
    {
        var expected = new RedisConnectionException(
            ConnectionFailureType.UnableToConnect,
            "fence unavailable");
        var database = CreateScriptDatabase(expected);
        database.Setup(value => value.StringGetAsync(
                It.IsAny<RedisKey[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(["1", "1"]);
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleWriteBack = false;
        var fusion = CreateGetOrSetFusionMock(async (factory, token) =>
        {
            var result = await factory(default!, token);
            staleWriteBack = true;
            return result;
        });
        fusion.Setup(cache => cache.RemoveAsync(
                "key",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => staleWriteBack = false)
            .Returns(ValueTask.CompletedTask);
        var sut = Create(fusion, redis);
        var staleRead = sut.GetOrSetAsync<string>(
            "key",
            async cancellationToken =>
            {
                factoryStarted.TrySetResult();
                await releaseFactory.Task.WaitAsync(cancellationToken);
                return "stale-before-invalidation";
            },
            static value => value is not null,
            TimeSpan.FromMinutes(5));

        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var failure = await Record.ExceptionAsync(() => sut.RemoveAsync("key"));
        releaseFactory.TrySetResult();

        Assert.Equal("stale-before-invalidation", await staleRead);
        Assert.Same(expected, failure);
        Assert.True(staleWriteBack);
        fusion.Verify(cache => cache.RemoveAsync(
            It.IsAny<string>(),
            It.IsAny<FusionCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_FenceUnknownExceptionPropagatesBeforeRemove()
    {
        var expected = new InvalidOperationException("fence programming fault");
        var database = CreateScriptDatabase(expected);
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        var fusion = CreateFusionMock();

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Create(fusion, redis).RemoveAsync("business:key"));

        Assert.Same(expected, actual);
        fusion.Verify(cache => cache.RemoveAsync(
            It.IsAny<string>(),
            It.IsAny<FusionCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveAsync_InfrastructureFailure_Degrades()
    {
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.RemoveAsync(
                "key",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FusionCacheDistributedCacheException("redis down"));
        var sut = Create(fusion);

        await sut.RemoveAsync("key");
    }

    [Fact]
    public async Task RemoveByPatternAsync_UnknownEndpointFailure_Propagates()
    {
        var expected = new InvalidOperationException("multiplexer misuse");
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        SetupSuccessfulOrdinaryFenceBump(redis);
        redis.Setup(connection => connection.GetEndPoints(It.IsAny<bool>())).Throws(expected);
        var sut = Create(CreateFusionMock(), redis);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RemoveByPatternAsync("prefix:*"));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task RemoveByPatternAsync_ScanResultsNeverDeleteSystemNamespaceKeys()
    {
        var (redis, server) = CreateConnectedServer();
        SetupKeys(server, Keys("__iiot_system:protected", "business:one"));
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.RemoveAsync(
                "business:one",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        var sut = Create(fusion, redis);

        await sut.RemoveByPatternAsync("business:*");

        fusion.Verify(cache => cache.RemoveAsync(
            "business:one",
            It.IsAny<FusionCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fusion.Verify(cache => cache.RemoveAsync(
            "__iiot_system:protected",
            It.IsAny<FusionCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(RedisCacheService.IsBusinessCacheKey("__iiot_system:protected"));
    }

    [Fact]
    public async Task InvalidateOnceAsync_ScanResultsNeverDeleteSystemNamespaceKeys()
    {
        var (redis, server) = CreateConnectedServer(setupOrdinaryFence: false);
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database.Setup(value => value.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)1L));
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        SetupKeys(server, Keys("__iiot_system:protected", "business:one"));
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.RemoveAsync(
                "business:one",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        var sut = Create(fusion, redis);

        var executed = await sut.InvalidateOnceAsync(
            Guid.NewGuid(),
            "scan-filter",
            [],
            ["business:*"]);

        Assert.True(executed);
        fusion.Verify(cache => cache.RemoveAsync(
            "business:one",
            It.IsAny<FusionCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fusion.Verify(cache => cache.RemoveAsync(
            "__iiot_system:protected",
            It.IsAny<FusionCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveByPatternAsync_RedisEndpointFailure_Degrades()
    {
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        SetupSuccessfulOrdinaryFenceBump(redis);
        redis.Setup(connection => connection.GetEndPoints(It.IsAny<bool>()))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis down"));
        var sut = Create(CreateFusionMock(), redis);

        await sut.RemoveByPatternAsync("prefix:*");
    }

    [Fact]
    public async Task RemoveByPatternAsync_CancellationAfterEndpointLookup_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        var database = SetupSuccessfulOrdinaryFenceBump(redis);
        redis.Setup(connection => connection.GetEndPoints(It.IsAny<bool>()))
            .Callback(() => cancellation.Cancel())
            .Returns([]);
        var sut = Create(CreateFusionMock(), redis);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RemoveByPatternAsync("prefix:*", cancellation.Token));

        redis.Verify(connection => connection.GetEndPoints(It.IsAny<bool>()), Times.Once);
        database.Verify(value => value.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RemoveByPatternAsync_DisconnectedServer_LogsStableWarningAndDegrades()
    {
        EndPoint endpoint = new DnsEndPoint("redis.internal", 6379);
        var server = new Mock<IServer>(MockBehavior.Strict);
        server.SetupGet(value => value.IsConnected).Returns(false);
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        SetupSuccessfulOrdinaryFenceBump(redis);
        redis.Setup(connection => connection.GetEndPoints(It.IsAny<bool>())).Returns([endpoint]);
        redis.Setup(connection => connection.GetServer(endpoint, It.IsAny<object?>())).Returns(server.Object);
        var logger = new RecordingLogger<RedisCacheService>();
        var sut = Create(CreateFusionMock(), redis, logger);

        await sut.RemoveByPatternAsync("prefix:*");

        var warning = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal(2401, warning.EventId.Id);
        Assert.Contains("RedisServerDisconnected", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("redis.internal", warning.Message, StringComparison.Ordinal);
        Assert.Null(warning.Exception);
    }

    [Fact]
    public async Task RemoveByPatternAsync_UnknownScanFailure_Propagates()
    {
        var expected = new InvalidOperationException("scan misuse");
        var (redis, server) = CreateConnectedServer();
        SetupKeys(server, ThrowingKeys(expected));
        var sut = Create(CreateFusionMock(), redis);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RemoveByPatternAsync("prefix:*"));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task RemoveByPatternAsync_RedisScanFailure_Degrades()
    {
        var (redis, server) = CreateConnectedServer();
        SetupKeys(
            server,
            ThrowingKeys(new RedisConnectionException(
                ConnectionFailureType.SocketFailure,
                "connection lost")));
        var sut = Create(CreateFusionMock(), redis);

        await sut.RemoveByPatternAsync("prefix:*");
    }

    [Fact]
    public async Task RemoveByPatternAsync_CancellationDuringRemove_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var (redis, server) = CreateConnectedServer();
        SetupKeys(server, Keys("prefix:one"));
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.RemoveAsync(
                "prefix:one",
                It.IsAny<FusionCacheEntryOptions>(),
                cancellation.Token))
            .Callback(() => cancellation.Cancel())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));
        var sut = Create(fusion, redis);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RemoveByPatternAsync("prefix:*", cancellation.Token));
    }

    [Fact]
    public async Task RemoveByPatternAsync_CancellationDuringKeyScan_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var (redis, server) = CreateConnectedServer();
        SetupKeys(server, CancelDuringScan(cancellation));
        var fusion = CreateFusionMock();
        var sut = Create(fusion, redis);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RemoveByPatternAsync("prefix:*", cancellation.Token));

        fusion.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoveByPatternAsync_BumpsGlobalFenceBeforeEndpointScan()
    {
        var callOrder = new List<string>();
        RedisKey[]? bumpedKeys = null;
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database.Setup(value => value.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, keys, _, _) =>
            {
                bumpedKeys = keys;
                callOrder.Add("fence");
            })
            .ReturnsAsync(RedisResult.Create((RedisValue)1L));
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        redis.Setup(connection => connection.GetEndPoints(It.IsAny<bool>()))
            .Callback(() => callOrder.Add("scan"))
            .Returns([]);

        await Create(CreateFusionMock(), redis).RemoveByPatternAsync("business:*");

        Assert.Equal(["fence", "scan"], callOrder);
        Assert.Equal(
            "__iiot_system:value-cache-write-fence:v1:patterns",
            Assert.Single(bumpedKeys!).ToString());
    }

    [Fact]
    public async Task RemoveByPatternAsync_FenceTimeoutPropagatesBeforeScan()
    {
        var expected = new RedisTimeoutException(
            "fence timed out",
            CommandStatus.Sent);
        var database = CreateScriptDatabase(expected);
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);

        var actual = await Assert.ThrowsAsync<RedisTimeoutException>(() =>
            Create(CreateFusionMock(), redis).RemoveByPatternAsync("business:*"));

        Assert.Same(expected, actual);
        redis.Verify(connection => connection.GetEndPoints(It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task RemoveByPatternAsync_FenceUnknownExceptionPropagatesBeforeScan()
    {
        var expected = new InvalidOperationException("pattern fence programming fault");
        var database = CreateScriptDatabase(expected);
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(CreateFusionMock(), redis).RemoveByPatternAsync("business:*"));

        Assert.Same(expected, actual);
        redis.Verify(connection => connection.GetEndPoints(It.IsAny<bool>()), Times.Never);
    }

    private static (Mock<IConnectionMultiplexer> Redis, Mock<IServer> Server) CreateConnectedServer(
        bool setupOrdinaryFence = true)
    {
        EndPoint endpoint = new DnsEndPoint("redis.internal", 6379);
        var server = new Mock<IServer>(MockBehavior.Strict);
        server.SetupGet(value => value.IsConnected).Returns(true);
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        if (setupOrdinaryFence)
            SetupSuccessfulOrdinaryFenceBump(redis);
        redis.Setup(connection => connection.GetEndPoints(It.IsAny<bool>())).Returns([endpoint]);
        redis.Setup(connection => connection.GetServer(endpoint, It.IsAny<object?>())).Returns(server.Object);
        return (redis, server);
    }

    private static void SetupKeys(
        Mock<IServer> server,
        IAsyncEnumerable<RedisKey> keys)
    {
        server.Setup(value => value.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(keys);
    }

    private static async IAsyncEnumerable<RedisKey> Keys(
        RedisKey key,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return key;
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<RedisKey> Keys(params RedisKey[] keys)
    {
        foreach (var key in keys)
            yield return key;
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<RedisKey> ThrowingKeys(
        Exception exception,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        throw exception;
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<RedisKey> CancelDuringScan(
        CancellationTokenSource cancellation,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellation.Cancel();
        cancellationToken.ThrowIfCancellationRequested();
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async Task<string?> InvokeProviderDelegateTwice(
        Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>> factory,
        CancellationToken cancellationToken)
    {
        var first = factory(default!, cancellationToken);
        var second = factory(default!, cancellationToken);
        var results = await Task.WhenAll(first, second);
        Assert.Equal(results[0], results[1]);
        return results[0];
    }

    private static Mock<IFusionCache> CreateFusionMock()
    {
        return new Mock<IFusionCache>(MockBehavior.Strict);
    }

    private static Mock<IDatabase> CreateScriptDatabase(params object[] outcomes)
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        var sequence = database.SetupSequence(value => value.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.IsAny<RedisValue[]>(),
            It.IsAny<CommandFlags>()));
        foreach (var outcome in outcomes)
        {
            if (outcome is Exception exception)
                sequence.ThrowsAsync(exception);
            else
                sequence.ReturnsAsync(RedisResult.Create((RedisValue)Convert.ToInt64(outcome)));
        }

        return database;
    }

    private static Mock<IDatabase> SetupSuccessfulOrdinaryFenceBump(
        Mock<IConnectionMultiplexer> redis)
    {
        var database = new Mock<IDatabase>(MockBehavior.Strict);
        database.Setup(value => value.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]>(),
                It.IsAny<RedisValue[]>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)1L));
        redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(database.Object);
        return database;
    }

    private static Mock<IFusionCache> CreateFactoryPassThroughFusionMock()
    {
        return CreateGetOrSetFusionMock((factory, token) =>
            new ValueTask<string?>(factory(default!, token)));
    }

    private static Mock<IFusionCache> CreateGetOrSetFusionMock(
        Func<
            Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>,
            CancellationToken,
            ValueTask<string?>> operation)
    {
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.GetOrSetAsync<string?>(
                "key",
                It.IsAny<Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>>(),
                It.IsAny<MaybeValue<string?>>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>, MaybeValue<string?>, FusionCacheEntryOptions, IEnumerable<string>?, CancellationToken>(
                (_, factory, _, _, _, token) => operation(factory, token));
        return fusion;
    }

    private static RedisCacheService Create(
        Mock<IFusionCache> fusion,
        Mock<IConnectionMultiplexer>? redis = null,
        ILogger<RedisCacheService>? logger = null) =>
        Create(
            fusion,
            redis,
            logger,
            Options.Create(new DomainEventCacheInvalidationOptions()));

    private static RedisCacheService Create(
        Mock<IFusionCache> fusion,
        Mock<IConnectionMultiplexer>? redis,
        ILogger<RedisCacheService>? logger,
        IOptions<DomainEventCacheInvalidationOptions> idempotencyOptions)
    {
        fusion.SetupGet(cache => cache.DefaultEntryOptionsProvider)
            .Returns(() => null!);
        fusion.SetupGet(cache => cache.DefaultEntryOptions)
            .Returns(new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(5)
            });
        fusion.Setup(cache => cache.CreateEntryOptions(
                It.IsAny<Action<FusionCacheEntryOptions>?>(),
                It.IsAny<TimeSpan?>()))
            .Returns<Action<FusionCacheEntryOptions>?, TimeSpan?>((configure, duration) =>
            {
                var options = new FusionCacheEntryOptions
                {
                    Duration = duration ?? TimeSpan.FromMinutes(5)
                };
                configure?.Invoke(options);
                return options;
            });
        if (redis is null)
        {
            var database = new Mock<IDatabase>(MockBehavior.Strict);
            database.Setup(value => value.StringGetAsync(
                    It.IsAny<RedisKey[]>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisKey[] keys, CommandFlags _) =>
                    keys.Select(static _ => RedisValue.Null).ToArray());
            database.Setup(value => value.ScriptEvaluateAsync(
                    It.IsAny<string>(),
                    It.IsAny<RedisKey[]>(),
                    It.IsAny<RedisValue[]>(),
                    It.IsAny<CommandFlags>()))
                .ReturnsAsync(RedisResult.Create((RedisValue)1L));
            redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
            redis.Setup(connection => connection.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
                .Returns(database.Object);
        }
        logger ??= NullLogger<RedisCacheService>.Instance;
        return new RedisCacheService(fusion.Object, redis.Object, logger, idempotencyOptions);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);
}
