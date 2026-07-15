using System.Diagnostics;
using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Infrastructure;
using IIoT.Infrastructure.Caching;
using IIoT.ProductionService.Queries.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Caching;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using IIoT.SharedKernel.Specification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace IIoT.CloudPlatform.IntegrationTests;

public sealed class RealRedisCacheIntegrationTests
{
    private const string RedisImage =
        "redis@sha256:6ab0b6e7381779332f97b8ca76193e45b0756f38d4c0dcda72dbb3c32061ab99";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task RealRedis_DisconnectDegradesWithoutMaskingFactory_ThenRecovers()
    {
        await using var redisContainer = await RedisContainerLease.StartAsync("cloud-cache-001");
        var container = redisContainer.Name;
        var endpoint = redisContainer.Endpoint;
        {
            await using var runtime = await RedisRuntime.CreateAsync(endpoint);
            var keyPrefix = $"cloud-cache-001:{Guid.NewGuid():N}";

            var healthyFactoryCalls = 0;
            var healthy = await runtime.Service.GetOrSetAsync<string>(
                $"{keyPrefix}:healthy",
                _ => Task.FromResult<string?>($"value-{++healthyFactoryCalls}"),
                static value => value is not null,
                CacheDuration);
            Assert.Equal("value-1", healthy);
            Assert.Equal(1, healthyFactoryCalls);

            await RunDockerAsync("pause", container);
            await RunDockerAsync("unpause", container);
            await WaitForDockerRedisAsync(container, TimeSpan.FromSeconds(30));
            await WaitForConnectedAsync(runtime.Connection, TimeSpan.FromSeconds(30));
            await using (var timeoutVerification = await RedisRuntime.CreateAsync(endpoint))
            {
                await AssertDistributedRecoveryAsync(
                    runtime,
                    timeoutVerification,
                    $"{keyPrefix}:timeout-recovered",
                    "timeout-path-is-back",
                    TimeSpan.FromSeconds(30));
            }

            await RunDockerAsync("stop", "--timeout", "1", container);
            await WaitForDisconnectedAsync(runtime.Connection, TimeSpan.FromSeconds(15));

            var expectedBusinessFailure = new InvalidOperationException("database business failure");
            var failingFactoryCalls = 0;
            var actualBusinessFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => runtime.Service.GetOrSetAsync<string>(
                    $"{keyPrefix}:outage-business",
                    _ =>
                    {
                        failingFactoryCalls++;
                        return Task.FromException<string?>(expectedBusinessFailure);
                    },
                    static value => value is not null,
                    CacheDuration));
            Assert.Same(expectedBusinessFailure, actualBusinessFailure);
            Assert.Equal(1, failingFactoryCalls);

            var fallbackFactoryCalls = 0;
            var fallback = await runtime.Service.GetOrSetAsync<string>(
                $"{keyPrefix}:outage-fallback",
                _ => Task.FromResult<string?>($"fallback-{++fallbackFactoryCalls}"),
                static value => value is not null,
                CacheDuration);
            Assert.Equal("fallback-1", fallback);
            Assert.Equal(1, fallbackFactoryCalls);

            await Assert.ThrowsAnyAsync<RedisException>(() =>
                runtime.Service.RemoveByPatternAsync($"{keyPrefix}:*"));

            await RunDockerAsync("start", container);
            await WaitForDockerRedisAsync(container, TimeSpan.FromSeconds(30));
            await AssertPublishedEndpointAsync(container, endpoint);
            await WaitForConnectedAsync(runtime.Connection, TimeSpan.FromSeconds(30));

            await using var verification = await RedisRuntime.CreateAsync(endpoint);
            await AssertDistributedRecoveryAsync(
                runtime,
                verification,
                $"{keyPrefix}:disconnect-recovered",
                "redis-is-back",
                TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task DomainEventInvalidation_RealRedisLeaseRetryAndCompletedReceiptPreventRepeat()
    {
        await using var redisContainer = await RedisContainerLease.StartAsync("cloud-domain-event-cache");
        var endpoint = redisContainer.Endpoint;
        {
            await using var runtime = await RedisRuntime.CreateAsync(
                endpoint,
                new Dictionary<string, string?>
                {
                    [$"{DomainEventCacheInvalidationOptions.SectionName}:LeaseDuration"] = "00:00:00.250",
                    [$"{DomainEventCacheInvalidationOptions.SectionName}:ClaimRetryDelay"] = "00:00:00.025",
                    [$"{DomainEventCacheInvalidationOptions.SectionName}:CompletedRetention"] = "7.00:00:00"
                });
            var operationId = Guid.NewGuid();
            var keyPrefix = $"iiot:domain-event-cache-test:{Guid.NewGuid():N}";
            var targetKey = $"{keyPrefix}:target";
            const string operationScope = "integration-test";
            var receiptKey = RedisCacheService.GetDomainEventInvalidationReceiptKey(operationId, operationScope);
            const string secondOperationScope = "second-handler";
            var secondTargetKey = $"{keyPrefix}:second-handler";
            var secondReceiptKey = RedisCacheService.GetDomainEventInvalidationReceiptKey(
                operationId,
                secondOperationScope);
            var protectedSystemKey = $"__iiot_system:integration-sentinel:{Guid.NewGuid():N}";
            var database = runtime.Connection.GetDatabase();
            var gateway = (IIdempotentCacheInvalidationService)runtime.Service;

            await runtime.Fusion.SetAsync(targetKey, "before-lease-retry");
            await database.StringSetAsync(
                receiptKey,
                $"processing:{Guid.NewGuid():N}",
                TimeSpan.FromMilliseconds(150));

            var leaseWatch = Stopwatch.StartNew();
            var executed = await gateway.InvalidateOnceAsync(
                operationId,
                operationScope,
                [targetKey],
                []);
            leaseWatch.Stop();

            Assert.True(executed);
            Assert.True(leaseWatch.Elapsed >= TimeSpan.FromMilliseconds(100));
            Assert.False((await runtime.Fusion.TryGetAsync<string>(targetKey)).HasValue);
            Assert.Equal("completed", await database.StringGetAsync(receiptKey));
            var completedTtl = await database.KeyTimeToLiveAsync(receiptKey);
            Assert.NotNull(completedTtl);
            Assert.InRange(completedTtl!.Value, TimeSpan.FromDays(6.9), TimeSpan.FromDays(7));

            await runtime.Fusion.SetAsync(targetKey, "reseeded-after-completion");
            var repeated = await gateway.InvalidateOnceAsync(
                operationId,
                operationScope,
                [targetKey],
                []);

            Assert.False(repeated);
            Assert.Equal(
                "reseeded-after-completion",
                (await runtime.Fusion.TryGetAsync<string>(targetKey)).Value);
            Assert.True(await database.KeyExistsAsync(receiptKey));

            await runtime.Fusion.SetAsync(secondTargetKey, "independent-handler-side-effect");
            var secondScopeExecuted = await gateway.InvalidateOnceAsync(
                operationId,
                secondOperationScope,
                [secondTargetKey],
                []);
            Assert.True(secondScopeExecuted);
            Assert.False((await runtime.Fusion.TryGetAsync<string>(secondTargetKey)).HasValue);
            Assert.Equal("completed", await database.StringGetAsync(secondReceiptKey));

            await database.StringSetAsync(protectedSystemKey, "must-survive");
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => runtime.Service.RemoveAsync(protectedSystemKey));
            foreach (var unsafePattern in new[]
                     {
                         "*",
                         "__iiot_system:*",
                         "__iiot?system:*",
                         "__iiot[_x]system:*"
                     })
            {
                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => runtime.Service.RemoveByPatternAsync(unsafePattern));
                await Assert.ThrowsAsync<InvalidOperationException>(() => gateway.InvalidateOnceAsync(
                    Guid.NewGuid(),
                    "system-namespace-guard",
                    [],
                    [unsafePattern]));
            }
            Assert.Equal("must-survive", await database.StringGetAsync(protectedSystemKey));

            await database.KeyDeleteAsync(
                [targetKey, receiptKey, secondTargetKey, secondReceiptKey, protectedSystemKey]);
        }
    }

    [Fact]
    public async Task DomainEventInvalidation_ConcurrentFactoryCannotWriteStaleValueBack()
    {
        await using var redisContainer = await RedisContainerLease.StartAsync("cloud-cache-fence");
        var endpoint = redisContainer.Endpoint;
        {
            await using var runtime = await RedisRuntime.CreateAsync(endpoint);
            await using var verification = await RedisRuntime.CreateAsync(endpoint);
            var gateway = (IIdempotentCacheInvalidationService)runtime.Service;

            await AssertConcurrentFactoryFenceAsync(
                runtime,
                verification,
                gateway,
                $"iiot:cache-fence:exact:{Guid.NewGuid():N}",
                "concurrent-exact",
                usePattern: false);
            await AssertConcurrentFactoryFenceAsync(
                runtime,
                verification,
                gateway,
                $"iiot:cache-fence:pattern:{Guid.NewGuid():N}",
                "concurrent-pattern",
                usePattern: true);
            await AssertCallerCancellationCannotBypassFenceAsync(
                runtime,
                verification,
                gateway,
                $"iiot:cache-fence:cancel:{Guid.NewGuid():N}");
            await AssertSyntheticTimeoutCannotWriteBackAsync(
                runtime,
                verification,
                gateway,
                $"iiot:cache-fence:timeout:{Guid.NewGuid():N}");
            await AssertConcurrentOrdinaryFactoryFenceAsync(
                runtime,
                verification,
                $"iiot:cache-fence:ordinary-exact:{Guid.NewGuid():N}",
                usePattern: false);
            await AssertConcurrentOrdinaryFactoryFenceAsync(
                runtime,
                verification,
                $"iiot:cache-fence:ordinary-pattern:{Guid.NewGuid():N}",
                usePattern: true);
        }
    }

    [Fact]
    public async Task GetAllDevicesHandler_DomainInvalidationCannotBeFollowedByStaleCacheAsideWriteBack()
    {
        await using var redisContainer = await RedisContainerLease.StartAsync("cloud-handler-cache-fence");
        var endpoint = redisContainer.Endpoint;
        await using var handlerRuntime = await RedisRuntime.CreateAsync(endpoint);
        await using var invalidationRuntime = await RedisRuntime.CreateAsync(endpoint);
        var staleDevice = new Device("Stale device", "STALE-DEVICE", Guid.NewGuid());
        var repository = new BlockingDeviceReadRepository(staleDevice);
        var handler = new GetAllDevicesHandler(
            new AdministratorDeviceAccessService(),
            repository,
            handlerRuntime.Service);

        var inFlightQuery = handler.Handle(new GetAllDevicesQuery(), CancellationToken.None);
        await repository.ReadStarted.WaitAsync(TimeSpan.FromSeconds(10));

        var invalidated = await ((IIdempotentCacheInvalidationService)invalidationRuntime.Service)
            .InvalidateOnceAsync(
                Guid.NewGuid(),
                "real-device-handler",
                [IIoT.Services.CrossCutting.Caching.CacheKeys.AllDevices()],
                []);
        Assert.True(invalidated);

        repository.ReleaseRead();
        var staleResult = await inFlightQuery.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(staleResult.IsSuccess);
        Assert.Equal(staleDevice.Id, Assert.Single(staleResult.Value!).Id);

        Assert.False((await handlerRuntime.Fusion.TryGetAsync<List<DeviceSelectDto>>(
            IIoT.Services.CrossCutting.Caching.CacheKeys.AllDevices())).HasValue);
        Assert.False((await invalidationRuntime.Fusion.TryGetAsync<List<DeviceSelectDto>>(
            IIoT.Services.CrossCutting.Caching.CacheKeys.AllDevices())).HasValue);
    }

    [Fact]
    public async Task GetOrSetAsync_RealFusionHonorsExplicitNullAndEmptyAdmissionPolicies()
    {
        await using var redisContainer = await RedisContainerLease.StartAsync("cloud-cache-admission");
        await using var runtime = await RedisRuntime.CreateAsync(redisContainer.Endpoint);
        var keyPrefix = $"iiot:cache-admission:{Guid.NewGuid():N}";

        var nullFactoryCalls = 0;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var value = await runtime.Service.GetOrSetAsync<string>(
                $"{keyPrefix}:null",
                _ =>
                {
                    nullFactoryCalls++;
                    return Task.FromResult<string?>(null);
                },
                static candidate => candidate is not null,
                CacheDuration);
            Assert.Null(value);
        }
        Assert.Equal(2, nullFactoryCalls);
        Assert.False((await runtime.Fusion.TryGetAsync<string>($"{keyPrefix}:null")).HasValue);

        var rejectedEmptyFactoryCalls = 0;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var value = await runtime.Service.GetOrSetAsync<List<string>>(
                $"{keyPrefix}:empty-rejected",
                _ =>
                {
                    rejectedEmptyFactoryCalls++;
                    return Task.FromResult<List<string>?>([]);
                },
                static candidate => candidate is { Count: > 0 },
                CacheDuration);
            Assert.Empty(value!);
        }
        Assert.Equal(2, rejectedEmptyFactoryCalls);
        Assert.False((await runtime.Fusion.TryGetAsync<List<string>>(
            $"{keyPrefix}:empty-rejected")).HasValue);

        var admittedEmptyFactoryCalls = 0;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var value = await runtime.Service.GetOrSetAsync<List<string>>(
                $"{keyPrefix}:empty-admitted",
                _ =>
                {
                    admittedEmptyFactoryCalls++;
                    return Task.FromResult<List<string>?>([]);
                },
                static candidate => candidate is not null,
                CacheDuration);
            Assert.Empty(value!);
        }
        Assert.Equal(1, admittedEmptyFactoryCalls);
        Assert.Empty((await runtime.Fusion.TryGetAsync<List<string>>(
            $"{keyPrefix}:empty-admitted")).Value!);
    }

    private static async Task AssertConcurrentFactoryFenceAsync(
        RedisRuntime runtime,
        RedisRuntime verification,
        IIdempotentCacheInvalidationService gateway,
        string key,
        string operationScope,
        bool usePattern)
    {
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleRead = runtime.Service.GetOrSetAsync<string>(
            key,
            async cancellationToken =>
            {
                factoryStarted.TrySetResult();
                await releaseFactory.Task.WaitAsync(cancellationToken);
                return "stale-before-invalidation";
            },
            static value => value is not null,
            CacheDuration);

        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var executed = await gateway.InvalidateOnceAsync(
            Guid.NewGuid(),
            operationScope,
            usePattern ? [] : [key],
            usePattern ? [$"{key}*"] : []);
        releaseFactory.TrySetResult();

        Assert.True(executed);
        Assert.Equal("stale-before-invalidation", await staleRead);
        Assert.False((await runtime.Fusion.TryGetAsync<string>(key)).HasValue);
        Assert.False((await verification.Fusion.TryGetAsync<string>(key)).HasValue);
    }

    private static async Task AssertCallerCancellationCannotBypassFenceAsync(
        RedisRuntime runtime,
        RedisRuntime verification,
        IIdempotentCacheInvalidationService gateway,
        string key)
    {
        var factory = new BlockedFactory();
        using var callerCancellation = new CancellationTokenSource();
        var staleRead = runtime.Service.GetOrSetAsync<string>(
            key,
            _ => factory.RunAsync("stale-after-caller-cancelled"),
            static value => value is not null,
            CacheDuration,
            cancellationToken: callerCancellation.Token);

        await factory.Started.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await gateway.InvalidateOnceAsync(
            Guid.NewGuid(),
            "cancelled-factory",
            [key],
            []));
        callerCancellation.Cancel();
        var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => staleRead);
        Assert.Equal(callerCancellation.Token, cancellation.CancellationToken);

        factory.Release();
        await factory.Finished.WaitAsync(TimeSpan.FromSeconds(10));
        await AssertCacheRemainsMissingAsync(runtime, verification, key);
    }

    private static async Task AssertConcurrentOrdinaryFactoryFenceAsync(
        RedisRuntime runtime,
        RedisRuntime verification,
        string key,
        bool usePattern)
    {
        var factoryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleRead = runtime.Service.GetOrSetAsync<string>(
            key,
            async cancellationToken =>
            {
                factoryStarted.TrySetResult();
                await releaseFactory.Task.WaitAsync(cancellationToken);
                return "stale-before-ordinary-invalidation";
            },
            static value => value is not null,
            CacheDuration);

        await factoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (usePattern)
            await runtime.Service.RemoveByPatternAsync($"{key}*");
        else
            await runtime.Service.RemoveAsync(key);
        releaseFactory.TrySetResult();

        Assert.Equal("stale-before-ordinary-invalidation", await staleRead);
        await AssertCacheRemainsMissingAsync(runtime, verification, key);
    }

    private static async Task AssertSyntheticTimeoutCannotWriteBackAsync(
        RedisRuntime runtime,
        RedisRuntime verification,
        IIdempotentCacheInvalidationService gateway,
        string key)
    {
        var factory = new BlockedFactory();
        runtime.Fusion.DefaultEntryOptions.FactoryHardTimeout = TimeSpan.FromMilliseconds(50);
        runtime.Fusion.DefaultEntryOptions.AllowTimedOutFactoryBackgroundCompletion = true;
        var staleRead = runtime.Service.GetOrSetAsync<string>(
            key,
            _ => factory.RunAsync("stale-after-synthetic-timeout"),
            static value => value is not null,
            CacheDuration);

        await factory.Started.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<SyntheticTimeoutException>(() => staleRead);
        Assert.True(await gateway.InvalidateOnceAsync(
            Guid.NewGuid(),
            "timed-out-factory",
            [],
            [$"{key}*"]));

        factory.Release();
        await factory.Finished.WaitAsync(TimeSpan.FromSeconds(10));
        await AssertCacheRemainsMissingAsync(runtime, verification, key);
    }

    private static async Task AssertCacheRemainsMissingAsync(
        RedisRuntime runtime,
        RedisRuntime verification,
        string key)
    {
        using var observationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        for (var attempt = 0; attempt < 10; attempt++)
        {
            Assert.False((await runtime.Fusion.TryGetAsync<string>(
                key,
                token: observationTimeout.Token)).HasValue);
            Assert.False((await verification.Fusion.TryGetAsync<string>(
                key,
                token: observationTimeout.Token)).HasValue);
            await Task.Delay(25, observationTimeout.Token);
        }
    }

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task AssertPublishedEndpointAsync(string container, string expected)
    {
        var output = await RunDockerAsync("port", container, "6379/tcp");
        var endpoint = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Single();
        Assert.Equal(expected, endpoint);
    }

    private static async Task WaitForDockerRedisAsync(string container, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastFailure = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var output = await RunDockerAsync("exec", container, "redis-cli", "ping");
                if (string.Equals(output.Trim(), "PONG", StringComparison.Ordinal))
                    return;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Redis container did not become ready: {lastFailure?.GetType().Name}");
    }

    private static async Task WaitForDisconnectedAsync(IConnectionMultiplexer connection, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!connection.IsConnected)
                return;

            await Task.Delay(100);
        }

        throw new TimeoutException("Redis connection did not report disconnection.");
    }

    private static async Task WaitForConnectedAsync(IConnectionMultiplexer connection, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastFailure = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await connection.GetDatabase().PingAsync().WaitAsync(TimeSpan.FromSeconds(2));
                return;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Redis connection did not recover: {lastFailure?.GetType().Name}");
    }

    private static async Task AssertDistributedRecoveryAsync(
        RedisRuntime writer,
        RedisRuntime reader,
        string keyPrefix,
        string expected,
        TimeSpan timeout)
    {
        var key = $"{keyPrefix}:backplane";
        const string stale = "stale-before-backplane-update";
        await writer.Fusion.SetAsync(key, stale);
        Assert.Equal(stale, (await reader.Fusion.TryGetAsync<string>(key)).Value);

        await writer.Fusion.SetAsync(key, expected);
        var deadline = DateTime.UtcNow + timeout;
        var attempt = 0;
        while (DateTime.UtcNow < deadline)
        {
            attempt++;
            var actual = (await reader.Fusion.TryGetAsync<string>(key)).Value;
            if (string.Equals(expected, actual, StringComparison.Ordinal))
                return;

            await Task.Delay(100);
        }

        Assert.Fail($"Redis backplane did not invalidate the reader L1 cache after {attempt} attempts.");
    }

    private static Task<string> RunDockerAsync(params string[] arguments) =>
        RunDockerAsync(CancellationToken.None, arguments);

    private static async Task<string> RunDockerAsync(
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start docker.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await standardOutput;
        var error = await standardError;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"docker {arguments[0]} failed with exit {process.ExitCode}: {error.Trim()}");
        return output;
    }

    private static async Task RunDockerAllowFailureAsync(params string[] arguments)
    {
        try
        {
            await RunDockerAsync(arguments);
        }
        catch
        {
            // Best-effort cleanup only; the test's primary assertion remains authoritative.
        }
    }

    private sealed class BlockedFactory
    {
        private readonly TaskCompletionSource _finished =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Finished => _finished.Task;

        public Task Started => _started.Task;

        public void Release() => _release.TrySetResult();

        public async Task<string?> RunAsync(string value)
        {
            _started.TrySetResult();
            await _release.Task;
            _finished.TrySetResult();
            return value;
        }
    }

    private sealed class AdministratorDeviceAccessService : ICurrentUserDeviceAccessService
    {
        public bool IsAdministrator => true;

        public Task<Result<IReadOnlyList<Guid>?>> GetAccessibleDeviceIdsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> EnsureCanAccessDeviceAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingDeviceReadRepository(Device staleDevice) : IReadRepository<Device>
    {
        private readonly TaskCompletionSource _readStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        public void ReleaseRead() => _releaseRead.TrySetResult();

        public async Task<List<Device>> GetListAsync(
            ISpecification<Device>? specification = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var staleSnapshot = new List<Device> { staleDevice };
            _readStarted.TrySetResult();
            await _releaseRead.Task.WaitAsync(cancellationToken);
            return staleSnapshot;
        }

        public Task<Device?> GetSingleOrDefaultAsync(
            ISpecification<Device>? specification = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(
            ISpecification<Device>? specification = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AnyAsync(
            ISpecification<Device>? specification = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AnyAsync(
            Expression<Func<Device, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(
            Expression<Func<Device, bool>> predicate,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RedisContainerLease : IAsyncDisposable
    {
        private RedisContainerLease(string name, string endpoint)
        {
            Name = name;
            Endpoint = endpoint;
        }

        public string Endpoint { get; }

        public string Name { get; }

        public static async Task<RedisContainerLease> StartAsync(string prefix)
        {
            var name = $"{prefix}-{Guid.NewGuid():N}";
            var endpoint = $"127.0.0.1:{ReserveLoopbackPort()}";
            try
            {
                var hostPort = endpoint[(endpoint.LastIndexOf(':') + 1)..];
                await RunDockerAsync(
                    "run", "--detach", "--name", name,
                    "--publish", $"127.0.0.1:{hostPort}:6379", RedisImage,
                    "--save", string.Empty, "--appendonly", "no");
                await WaitForDockerRedisAsync(name, TimeSpan.FromSeconds(30));
                await AssertPublishedEndpointAsync(name, endpoint);
                return new RedisContainerLease(name, endpoint);
            }
            catch
            {
                await RunDockerAllowFailureAsync("rm", "--force", name);
                throw;
            }
        }

        public async ValueTask DisposeAsync() =>
            await RunDockerAllowFailureAsync("rm", "--force", Name);
    }

    private sealed class RedisRuntime : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        private RedisRuntime(
            ServiceProvider provider,
            IFusionCache fusion,
            IConnectionMultiplexer connection,
            RedisCacheService service)
        {
            _provider = provider;
            Fusion = fusion;
            Connection = connection;
            Service = service;
        }

        public IFusionCache Fusion { get; }

        public IConnectionMultiplexer Connection { get; }

        public RedisCacheService Service { get; }

        public static async Task<RedisRuntime> CreateAsync(
            string endpoint,
            IReadOnlyDictionary<string, string?>? configurationOverrides = null)
        {
            var connectionString = string.Join(",",
                endpoint,
                "abortConnect=false",
                "connectRetry=1",
                "connectTimeout=1000",
                "asyncTimeout=1000",
                "syncTimeout=1000",
                "keepAlive=1");
            var builder = Host.CreateApplicationBuilder();
            var configuration = new Dictionary<string, string?>
            {
                ["ConnectionStrings:redis-cache"] = connectionString,
                ["CacheSafety:FailSafeMinutes"] = "17"
            };
            foreach (var entry in configurationOverrides ?? new Dictionary<string, string?>())
                configuration[entry.Key] = entry.Value;
            builder.Configuration.AddInMemoryCollection(configuration);
            builder.AddInfrastructures();

            var provider = builder.Services.BuildServiceProvider();
            var fusion = provider.GetRequiredService<IFusionCache>();
            Assert.Equal(TimeSpan.FromMinutes(5), fusion.DefaultEntryOptions.Duration);
            Assert.True(fusion.DefaultEntryOptions.IsFailSafeEnabled);
            Assert.Equal(TimeSpan.FromMinutes(17), fusion.DefaultEntryOptions.FailSafeMaxDuration);
            Assert.Equal(TimeSpan.FromSeconds(10), fusion.DefaultEntryOptions.FailSafeThrottleDuration);

            var connection = provider.GetRequiredService<IConnectionMultiplexer>();
            await connection.GetDatabase().PingAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var service = Assert.IsType<RedisCacheService>(provider.GetRequiredService<ICacheService>());
            return new RedisRuntime(provider, fusion, connection, service);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
        }
    }
}
