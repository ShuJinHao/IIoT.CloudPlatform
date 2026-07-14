using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using IIoT.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace IIoT.RedisIntegrationTests;

[Trait("TestKind", "Integration")]
[Trait("Runtime", "Redis")]
[Trait("Risk", "P0")]
[Trait("Capability", "Caching")]
[Trait("Owner", "Cloud.Infrastructure")]
public sealed class RealRedisCacheIntegrationTests
{
    private const string RedisImage =
        "redis@sha256:6ab0b6e7381779332f97b8ca76193e45b0756f38d4c0dcda72dbb3c32061ab99";

    [Fact]
    public async Task RealRedis_DisconnectDegradesWithoutMaskingFactory_ThenRecovers()
    {
        var container = $"cloud-cache-001-{Guid.NewGuid():N}";
        var hostPort = ReserveLoopbackPort();
        var endpoint = $"127.0.0.1:{hostPort}";

        try
        {
            await RunDockerAsync("run", "--detach", "--name", container,
                "--publish", $"127.0.0.1:{hostPort}:6379", RedisImage,
                "--save", string.Empty, "--appendonly", "no");
            await WaitForDockerRedisAsync(container, TimeSpan.FromSeconds(30));
            await AssertPublishedEndpointAsync(container, endpoint);

            await using var runtime = await RedisRuntime.CreateAsync(endpoint);
            var keyPrefix = $"cloud-cache-001:{Guid.NewGuid():N}";

            var healthyFactoryCalls = 0;
            var healthy = await runtime.Service.GetOrSetAsync<string>(
                $"{keyPrefix}:healthy",
                _ => Task.FromResult<string?>($"value-{++healthyFactoryCalls}"));
            Assert.Equal("value-1", healthy);
            Assert.Equal(1, healthyFactoryCalls);

            await RunDockerAsync("pause", container);
            var timeoutWatch = Stopwatch.StartNew();
            var timeoutMiss = await runtime.Service.GetAsync<string>($"{keyPrefix}:timeout-miss");
            timeoutWatch.Stop();
            Assert.Null(timeoutMiss);
            Assert.True(
                timeoutWatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Cache timeout degradation took {timeoutWatch.Elapsed}.");

            await RunDockerAsync("unpause", container);
            await WaitForDockerRedisAsync(container, TimeSpan.FromSeconds(30));
            await WaitForConnectedAsync(runtime.Connection, TimeSpan.FromSeconds(30));
            await using (var timeoutVerification = await RedisRuntime.CreateAsync(endpoint))
            {
                await AssertDistributedRecoveryAsync(
                    runtime.Service,
                    timeoutVerification.Service,
                    $"{keyPrefix}:timeout-recovered",
                    "timeout-path-is-back",
                    TimeSpan.FromSeconds(30));
            }

            await RunDockerAsync("stop", "--timeout", "1", container);
            await WaitForDisconnectedAsync(runtime.Connection, TimeSpan.FromSeconds(15));

            var miss = await runtime.Service.GetAsync<string>($"{keyPrefix}:outage-miss");
            Assert.Null(miss);

            var expectedBusinessFailure = new InvalidOperationException("database business failure");
            var failingFactoryCalls = 0;
            var actualBusinessFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => runtime.Service.GetOrSetAsync<string>(
                    $"{keyPrefix}:outage-business",
                    _ =>
                    {
                        failingFactoryCalls++;
                        return Task.FromException<string?>(expectedBusinessFailure);
                    }));
            Assert.Same(expectedBusinessFailure, actualBusinessFailure);
            Assert.Equal(1, failingFactoryCalls);

            var fallbackFactoryCalls = 0;
            var fallback = await runtime.Service.GetOrSetAsync<string>(
                $"{keyPrefix}:outage-fallback",
                _ => Task.FromResult<string?>($"fallback-{++fallbackFactoryCalls}"));
            Assert.Equal("fallback-1", fallback);
            Assert.Equal(1, fallbackFactoryCalls);

            await runtime.Service.RemoveByPatternAsync($"{keyPrefix}:*");

            await RunDockerAsync("start", container);
            await WaitForDockerRedisAsync(container, TimeSpan.FromSeconds(30));
            await AssertPublishedEndpointAsync(container, endpoint);
            await WaitForConnectedAsync(runtime.Connection, TimeSpan.FromSeconds(30));

            await using var verification = await RedisRuntime.CreateAsync(endpoint);
            await AssertDistributedRecoveryAsync(
                runtime.Service,
                verification.Service,
                $"{keyPrefix}:disconnect-recovered",
                "redis-is-back",
                TimeSpan.FromSeconds(30));
        }
        finally
        {
            await RunDockerAllowFailureAsync("rm", "--force", container);
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
        RedisCacheService writer,
        RedisCacheService reader,
        string keyPrefix,
        string expected,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var attempt = 0;
        while (DateTime.UtcNow < deadline)
        {
            var key = $"{keyPrefix}:{++attempt}";
            await writer.SetAsync(key, expected);
            var actual = await reader.GetAsync<string>(key);
            if (string.Equals(expected, actual, StringComparison.Ordinal))
                return;

            await Task.Delay(100);
        }

        Assert.Fail($"Redis distributed cache did not recover after {attempt} attempts.");
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

    private sealed class RedisRuntime : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        private RedisRuntime(
            ServiceProvider provider,
            IConnectionMultiplexer connection,
            RedisCacheService service)
        {
            _provider = provider;
            Connection = connection;
            Service = service;
        }

        public IConnectionMultiplexer Connection { get; }

        public RedisCacheService Service { get; }

        public static async Task<RedisRuntime> CreateAsync(string endpoint)
        {
            var connectionOptions = ConfigurationOptions.Parse(endpoint);
            connectionOptions.AbortOnConnectFail = false;
            connectionOptions.ConnectRetry = 1;
            connectionOptions.ConnectTimeout = 1_000;
            connectionOptions.AsyncTimeout = 1_000;
            connectionOptions.SyncTimeout = 1_000;
            connectionOptions.KeepAlive = 1;
            connectionOptions.ReconnectRetryPolicy = new ExponentialRetry(500);

            var connection = await ConnectionMultiplexer.ConnectAsync(connectionOptions);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddStackExchangeRedisCache(options =>
            {
                options.ConfigurationOptions = connectionOptions;
            });
            services.AddFusionCache()
                .WithDefaultEntryOptions(new FusionCacheEntryOptions
                {
                    Duration = TimeSpan.FromMinutes(5),
                    IsFailSafeEnabled = false
                })
                .WithSystemTextJsonSerializer()
                .WithDistributedCache(provider => provider.GetRequiredService<IDistributedCache>())
                .WithStackExchangeRedisBackplane(options =>
                {
                    options.Configuration = endpoint;
                });
            var provider = services.BuildServiceProvider();
            var fusion = provider.GetRequiredService<IFusionCache>();
            var service = new RedisCacheService(
                fusion,
                connection,
                NullLogger<RedisCacheService>.Instance);
            return new RedisRuntime(provider, connection, service);
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
