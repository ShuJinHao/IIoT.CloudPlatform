using System.Net;
using System.Runtime.CompilerServices;
using IIoT.Infrastructure.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace IIoT.Infrastructure.Tests;

[Trait("TestKind", "Workflow")]
[Trait("Runtime", "Pure")]
[Trait("Risk", "P0")]
[Trait("Capability", "Caching")]
[Trait("Owner", "Cloud.Infrastructure")]
public sealed class RedisCacheServiceSemanticsTests
{
    [Fact]
    public async Task GetAsync_CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var fusion = new Mock<IFusionCache>(MockBehavior.Strict);
        fusion.Setup(cache => cache.TryGetAsync<string>(
                "key",
                It.IsAny<FusionCacheEntryOptions>(),
                cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        var sut = Create(fusion);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.GetAsync<string>("key", cancellation.Token));
    }

    [Fact]
    public async Task SetAsync_CallerCancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var fusion = new Mock<IFusionCache>(MockBehavior.Strict);
        fusion.Setup(cache => cache.SetAsync(
                "key",
                "value",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        var sut = Create(fusion);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.SetAsync("key", "value", cancellationToken: cancellation.Token));
    }

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
            cancellationToken: cancellation.Token));
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryException_PropagatesSameInstanceAndRunsOnce()
    {
        var expected = new InvalidOperationException("database failed");
        var second = new InvalidOperationException("factory was repeated");
        var factoryCalls = 0;
        var fusion = new Mock<IFusionCache>(MockBehavior.Strict);
        fusion.Setup(cache => cache.GetOrSetAsync<string?>(
                "key",
                It.IsAny<Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>>(),
                It.IsAny<MaybeValue<string?>>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>, MaybeValue<string?>, FusionCacheEntryOptions, IEnumerable<string>?, CancellationToken>(
                (_, factory, _, _, _, token) => new ValueTask<string?>(factory(default!, token)));

        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ => Task.FromException<string?>(factoryCalls++ == 0 ? expected : second)));

        Assert.Same(expected, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFailureBeforeFactory_FallsBackExactlyOnce()
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
            .ThrowsAsync(new FusionCacheDistributedCacheException("redis read failed"));
        var sut = Create(fusion);

        var actual = await sut.GetOrSetAsync<string>("key", _ =>
        {
            factoryCalls++;
            return Task.FromResult<string?>("database value");
        });

        Assert.Equal("database value", actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFailureBeforeFactory_FallbackExceptionPropagatesSameInstance()
    {
        var expected = new InvalidOperationException("database fallback failed");
        var factoryCalls = 0;
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.GetOrSetAsync<string?>(
                "key",
                It.IsAny<Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>>(),
                It.IsAny<MaybeValue<string?>>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FusionCacheDistributedCacheException("redis read failed"));
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                return Task.FromException<string?>(expected);
            }));

        Assert.Same(expected, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryCacheLikeException_IsStillFactoryFailureAndPropagatesSameInstance()
    {
        var expected = new FusionCacheDistributedCacheException("business factory used a cache-like type");
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
                (_, factory, _, _, _, token) => new ValueTask<string?>(factory(default!, token)));
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<FusionCacheDistributedCacheException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                return Task.FromException<string?>(expected);
            }));

        Assert.Same(expected, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryCallerCancellation_PropagatesSameInstanceAndRunsOnce()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new OperationCanceledException(cancellation.Token);
        var factoryCalls = 0;
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.GetOrSetAsync<string?>(
                "key",
                It.IsAny<Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>>(),
                It.IsAny<MaybeValue<string?>>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                cancellation.Token))
            .Returns<string, Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>, MaybeValue<string?>, FusionCacheEntryOptions, IEnumerable<string>?, CancellationToken>(
                (_, factory, _, _, _, token) => new ValueTask<string?>(factory(default!, token)));
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                cancellation.Cancel();
                throw expected;
            },
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
            }));

        Assert.Same(factoryFailure, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFailureAfterFactorySuccess_ReturnsValueAndDoesNotRepeatFactory()
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
                async (_, factory, _, _, _, token) =>
                {
                    await factory(default!, token);
                    throw new FusionCacheBackplaneException("backplane write failed");
                });
        var sut = Create(fusion);

        var actual = await sut.GetOrSetAsync<string>("key", _ =>
        {
            factoryCalls++;
            return Task.FromResult<string?>("database value");
        });

        Assert.Equal("database value", actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_CancellationDuringWriteBack_PropagatesSameInstanceWithoutRepeatingFactory()
    {
        using var cancellation = new CancellationTokenSource();
        var expected = new OperationCanceledException(cancellation.Token);
        var factoryCalls = 0;
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.GetOrSetAsync<string?>(
                "key",
                It.IsAny<Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>>(),
                It.IsAny<MaybeValue<string?>>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                cancellation.Token))
            .Returns<string, Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>, MaybeValue<string?>, FusionCacheEntryOptions, IEnumerable<string>?, CancellationToken>(
                async (_, factory, _, _, _, token) =>
                {
                    await factory(default!, token);
                    cancellation.Cancel();
                    throw expected;
                });
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ => Task.FromResult<string?>($"value-{++factoryCalls}"),
            cancellationToken: cancellation.Token));

        Assert.Same(expected, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_CacheFailureCannotMaskFactoryException()
    {
        var expected = new InvalidOperationException("database failed");
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
            }));

        Assert.Same(expected, actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_ProviderFailureWhileFactoryRuns_AwaitsSameFactoryTask()
    {
        var factoryCompletion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                (_, factory, _, _, _, token) =>
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
        });
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
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.GetOrSetAsync<string?>(
                "key",
                It.IsAny<Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>>(),
                It.IsAny<MaybeValue<string?>>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>, MaybeValue<string?>, FusionCacheEntryOptions, IEnumerable<string>?, CancellationToken>(
                (_, factory, _, _, _, token) =>
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
        });
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
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.GetOrSetAsync<string?>(
                "key",
                It.IsAny<Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>>(),
                It.IsAny<MaybeValue<string?>>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                cancellation.Token))
            .Returns<string, Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>, MaybeValue<string?>, FusionCacheEntryOptions, IEnumerable<string>?, CancellationToken>(
                (_, factory, _, _, _, token) =>
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
        }, cancellationToken: cancellation.Token);

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
        });

        Assert.Equal("database value", actual);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_SyntheticTimeoutWhileFactoryRuns_PropagatesTimeoutWithoutSecondFactory()
    {
        var timeout = new SyntheticTimeoutException("factory hard timeout");
        var factoryCompletion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
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
                (_, factory, _, _, _, token) =>
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
            }));

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
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.GetOrSetAsync<string?>(
                "key",
                It.IsAny<Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>>(),
                It.IsAny<MaybeValue<string?>>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<FusionCacheSerializationException>(
            () => sut.GetOrSetAsync<string>("key", _ =>
            {
                factoryCalls++;
                return Task.FromResult<string?>("value");
            }));

        Assert.Same(expected, actual);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task GetOrSetAsync_UnknownProviderException_PropagatesWithoutInvokingFactory()
    {
        var expected = new InvalidOperationException("unknown provider failure");
        var factoryCalls = 0;
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.GetOrSetAsync<string?>(
                "key",
                It.IsAny<Func<FusionCacheFactoryExecutionContext<string?>, CancellationToken, Task<string?>>>(),
                It.IsAny<MaybeValue<string?>>(),
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetOrSetAsync<string>(
            "key",
            _ =>
            {
                factoryCalls++;
                return Task.FromResult<string?>("value");
            }));

        Assert.Same(expected, actual);
        Assert.Equal(0, factoryCalls);
    }

    [Fact]
    public async Task GetAsync_UnknownException_Propagates()
    {
        var expected = new InvalidOperationException("programming fault");
        var fusion = new Mock<IFusionCache>(MockBehavior.Strict);
        fusion.Setup(cache => cache.TryGetAsync<string>(
                "key",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetAsync<string>("key"));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task GetAsync_RedisConnectionFailure_DegradesToMiss()
    {
        var fusion = new Mock<IFusionCache>(MockBehavior.Strict);
        fusion.Setup(cache => cache.TryGetAsync<string>(
                "key",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis down"));
        var sut = Create(fusion);

        var actual = await sut.GetAsync<string>("key");

        Assert.Null(actual);
    }

    [Fact]
    public async Task GetAsync_SerializationException_Propagates()
    {
        var expected = new FusionCacheSerializationException("bad payload");
        var fusion = new Mock<IFusionCache>(MockBehavior.Strict);
        fusion.Setup(cache => cache.TryGetAsync<string>(
                "key",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<FusionCacheSerializationException>(
            () => sut.GetAsync<string>("key"));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task SetAsync_UnknownException_Propagates()
    {
        var expected = new InvalidOperationException("programming fault");
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.SetAsync(
                "key",
                "value",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);
        var sut = Create(fusion);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SetAsync("key", "value"));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task SetAsync_InfrastructureFailure_Degrades()
    {
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.SetAsync(
                "key",
                "value",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FusionCacheBackplaneException("backplane down"));
        var sut = Create(fusion);

        await sut.SetAsync("key", "value");
    }

    [Fact]
    public async Task SetAsync_Null_DelegatesToRemoveOnly()
    {
        var fusion = CreateFusionMock();
        fusion.Setup(cache => cache.RemoveAsync(
                "key",
                It.IsAny<FusionCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        var sut = Create(fusion);

        await sut.SetAsync<string?>("key", null);

        fusion.Verify(cache => cache.RemoveAsync(
            "key",
            It.IsAny<FusionCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fusion.Verify(cache => cache.SetAsync(
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<FusionCacheEntryOptions>(),
            It.IsAny<IEnumerable<string>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
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
        redis.Setup(connection => connection.GetEndPoints(It.IsAny<bool>())).Throws(expected);
        var sut = Create(CreateFusionMock(), redis);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RemoveByPatternAsync("prefix:*"));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task RemoveByPatternAsync_RedisEndpointFailure_Degrades()
    {
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.Setup(connection => connection.GetEndPoints(It.IsAny<bool>()))
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis down"));
        var sut = Create(CreateFusionMock(), redis);

        await sut.RemoveByPatternAsync("prefix:*");
    }

    [Fact]
    public async Task RemoveByPatternAsync_CancellationAfterEndpointLookup_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        EndPoint endpoint = new DnsEndPoint("redis.internal", 6379);
        var server = new Mock<IServer>(MockBehavior.Strict);
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.Setup(connection => connection.GetEndPoints(It.IsAny<bool>())).Returns([endpoint]);
        redis.Setup(connection => connection.GetServer(endpoint, It.IsAny<object?>()))
            .Callback(() => cancellation.Cancel())
            .Returns(server.Object);
        var sut = Create(CreateFusionMock(), redis);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RemoveByPatternAsync("prefix:*", cancellation.Token));

        server.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RemoveByPatternAsync_DisconnectedServer_LogsStableWarningAndDegrades()
    {
        EndPoint endpoint = new DnsEndPoint("redis.internal", 6379);
        var server = new Mock<IServer>(MockBehavior.Strict);
        server.SetupGet(value => value.IsConnected).Returns(false);
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
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
        server.Setup(value => value.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(ThrowingKeys(expected));
        var sut = Create(CreateFusionMock(), redis);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RemoveByPatternAsync("prefix:*"));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task RemoveByPatternAsync_RedisScanFailure_Degrades()
    {
        var (redis, server) = CreateConnectedServer();
        server.Setup(value => value.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(ThrowingKeys(
                new RedisConnectionException(ConnectionFailureType.SocketFailure, "connection lost")));
        var sut = Create(CreateFusionMock(), redis);

        await sut.RemoveByPatternAsync("prefix:*");
    }

    [Fact]
    public async Task RemoveByPatternAsync_CancellationDuringRemove_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var (redis, server) = CreateConnectedServer();
        server.Setup(value => value.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(Keys("prefix:one"));
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
        server.Setup(value => value.KeysAsync(
                It.IsAny<int>(),
                It.IsAny<RedisValue>(),
                It.IsAny<int>(),
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<CommandFlags>()))
            .Returns(CancelDuringScan(cancellation));
        var fusion = CreateFusionMock();
        var sut = Create(fusion, redis);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.RemoveByPatternAsync("prefix:*", cancellation.Token));

        fusion.VerifyNoOtherCalls();
    }

    private static (Mock<IConnectionMultiplexer> Redis, Mock<IServer> Server) CreateConnectedServer()
    {
        EndPoint endpoint = new DnsEndPoint("redis.internal", 6379);
        var server = new Mock<IServer>(MockBehavior.Strict);
        server.SetupGet(value => value.IsConnected).Returns(true);
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.Setup(connection => connection.GetEndPoints(It.IsAny<bool>())).Returns([endpoint]);
        redis.Setup(connection => connection.GetServer(endpoint, It.IsAny<object?>())).Returns(server.Object);
        return (redis, server);
    }

    private static async IAsyncEnumerable<RedisKey> Keys(
        RedisKey key,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

    private static RedisCacheService Create(
        Mock<IFusionCache> fusion,
        Mock<IConnectionMultiplexer>? redis = null,
        ILogger<RedisCacheService>? logger = null)
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
        redis ??= new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        logger ??= NullLogger<RedisCacheService>.Instance;
        return new RedisCacheService(fusion.Object, redis.Object, logger);
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
