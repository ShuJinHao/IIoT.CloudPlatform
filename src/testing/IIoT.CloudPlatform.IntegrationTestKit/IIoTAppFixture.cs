using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using IIoT.SharedKernel.Configuration;
using Microsoft.Extensions.Logging;

namespace IIoT.CloudPlatform.IntegrationTestKit;

public sealed class IIoTAppFixture : IAsyncDisposable
{
    public const string SeedAdminEmployeeNo = "101650";
    public const string SeedAdminRealName = "\u7CFB\u7EDF\u7BA1\u7406\u5458";
    public const string TestPostgresPassword = "TestPg123!";
    public static readonly string SeedAdminPassword = $"E2eSeed-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}!";
    public static readonly string TestJwtSecret = $"iiot-e2e-{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";

    private static readonly string[] ProxyEnvironmentVariableNames =
    [
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "ALL_PROXY",
        "http_proxy",
        "https_proxy",
        "all_proxy",
        "NO_PROXY",
        "no_proxy"
    ];

    private const string TestNoProxyValue =
        "localhost,127.0.0.1,::1,host.docker.internal,0.0.0.0,*.local,169.254.0.0/16";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan HealthProbeInterval = TimeSpan.FromMilliseconds(500);

    private DistributedApplication? _app;
    private HttpClient? _httpClient;
    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);
    private readonly string _postgresVolumeName = $"postgres-iiot-e2e-{Guid.NewGuid():N}";
    private readonly string _rabbitMqVolumeName = $"rabbitmq-iiot-e2e-{Guid.NewGuid():N}";
    private readonly bool _disableDataWorkerOutboxDispatcher;

    public IIoTAppFixture(bool disableDataWorkerOutboxDispatcher = false)
    {
        _disableDataWorkerOutboxDispatcher = disableDataWorkerOutboxDispatcher;
    }

    public HttpClient HttpClient => _httpClient ?? throw new InvalidOperationException("环境未启动。");

    public bool DataWorkerOutboxDispatcherDisabled => _disableDataWorkerOutboxDispatcher;

    public async Task StartAsync()
    {
        using var startupTimeout = new CancellationTokenSource(StartupTimeout);

        try
        {
            ConfigureAspireProxyEnvironment();
            ConfigureSeedAdminEnvironment();

            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IIoT_AppHost>()
                .WaitAsync(startupTimeout.Token);
            builder.Services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            });

            _app = await builder.BuildAsync().WaitAsync(startupTimeout.Token);
            await _app.StartAsync().WaitAsync(startupTimeout.Token);

            await _app.ResourceNotifications.WaitForResourceHealthyAsync("postgres")
                .WaitAsync(startupTimeout.Token);
            await _app.ResourceNotifications.WaitForResourceHealthyAsync(ConnectionResourceNames.EventBus)
                .WaitAsync(startupTimeout.Token);
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("iiot-httpapi")
                .WaitAsync(startupTimeout.Token);
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("iiot-gateway")
                .WaitAsync(startupTimeout.Token);
            await _app.ResourceNotifications.WaitForResourceAsync(
                "iiot-dataworker",
                KnownResourceStates.Running).WaitAsync(startupTimeout.Token);

            _httpClient = _app.CreateHttpClient("iiot-gateway");
            await WaitForGatewayHealthzAsync(_httpClient, startupTimeout.Token);
        }
        catch (DistributedApplicationException ex)
            when (ex.Message.Contains("docker", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Docker 不可用，无法启动 Aspire 端到端测试环境。",
                ex);
        }
        catch (OperationCanceledException ex) when (startupTimeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Aspire 端到端测试环境在 {StartupTimeout.TotalSeconds:N0} 秒内未就绪，请检查 Docker、Postgres、RabbitMQ、iiot-httpapi、iiot-gateway 和 iiot-dataworker 资源状态。",
                ex);
        }
    }

    public void SetAuthToken(string token)
    {
        HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public void ClearAuthToken()
    {
        HttpClient.DefaultRequestHeaders.Authorization = null;
    }

    public Uri GetEndpoint(string resourceName, string? endpointName = null) =>
        _app?.GetEndpoint(resourceName, endpointName)
        ?? throw new InvalidOperationException("环境未启动。");

    public async Task<string> GetConnectionStringAsync(
        string resourceName,
        CancellationToken cancellationToken = default)
    {
        var connectionString = await (_app?.GetConnectionStringAsync(resourceName, cancellationToken)
            ?? throw new InvalidOperationException("环境未启动。"));

        return connectionString
               ?? throw new InvalidOperationException($"资源 {resourceName} 未提供连接字符串。");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _httpClient?.Dispose();
            if (_app is not null)
                await _app.DisposeAsync();

            await DockerTestResourceCleaner.CleanupNamedVolumesAsync(
                _postgresVolumeName,
                _rabbitMqVolumeName);
        }
        finally
        {
            foreach (var entry in _originalEnvironment)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }

    private void ConfigureAspireProxyEnvironment()
    {
        foreach (var name in ProxyEnvironmentVariableNames)
        {
            SetEnvironmentVariable(name, null);
        }

        SetEnvironmentVariable("NO_PROXY", TestNoProxyValue);
    }

    private void ConfigureSeedAdminEnvironment()
    {
        SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");
        SetEnvironmentVariable("Parameters__pg-password", TestPostgresPassword);
        SetEnvironmentVariable("Parameters__seed-admin-no", SeedAdminEmployeeNo);
        SetEnvironmentVariable("Parameters__seed-admin-password", SeedAdminPassword);
        SetEnvironmentVariable("AppHost__PostgresVolumeName", _postgresVolumeName);
        SetEnvironmentVariable("AppHost__RabbitMqVolumeName", _rabbitMqVolumeName);
        SetEnvironmentVariable(
            "AppHost__Testing__DisableDataWorkerOutboxDispatcher",
            _disableDataWorkerOutboxDispatcher ? "true" : null);
        SetEnvironmentVariable("JwtSettings__Secret", TestJwtSecret);
        SetEnvironmentVariable("JWTSETTINGS__SECRET", TestJwtSecret);
        SetEnvironmentVariable("SEED_ADMIN_NO", SeedAdminEmployeeNo);
        SetEnvironmentVariable("SEED_ADMIN_PASSWORD", SeedAdminPassword);
        SetEnvironmentVariable("SEED_ADMIN_REAL_NAME", SeedAdminRealName);
    }

    private void SetEnvironmentVariable(string name, string? value)
    {
        if (!_originalEnvironment.ContainsKey(name))
        {
            _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
    }

    private static async Task WaitForGatewayHealthzAsync(
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        using var healthProbeTimer = new PeriodicTimer(HealthProbeInterval);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await httpClient.GetAsync("/internal/healthz", cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
            {
                // Gateway can accept connections before downstream health checks are ready.
            }

            if (!await healthProbeTimer.WaitForNextTickAsync(cancellationToken))
            {
                throw new InvalidOperationException("Gateway health probe timer stopped before the gateway became healthy.");
            }
        }
    }
}
