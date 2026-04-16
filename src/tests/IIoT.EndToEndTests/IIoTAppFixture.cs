using System.Net.Http.Headers;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Logging;

namespace IIoT.EndToEndTests;

public sealed class IIoTAppFixture : IAsyncDisposable
{
    public const string SeedAdminEmployeeNo = "101650";
    public const string SeedAdminPassword = "Ljh123456!";
    public const string SeedAdminRealName = "\u7CFB\u7EDF\u7BA1\u7406\u5458";

    private DistributedApplication? _app;
    private HttpClient? _httpClient;
    private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.Ordinal);

    public HttpClient HttpClient => _httpClient ?? throw new InvalidOperationException("环境未启动。");

    public async Task StartAsync()
    {
        try
        {
            ConfigureSeedAdminEnvironment();

            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.IIoT_AppHost>();
            builder.Services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
            });

            _app = await builder.BuildAsync();
            await _app.StartAsync();

            await _app.ResourceNotifications.WaitForResourceHealthyAsync("postgres");
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("eventbus");
            await _app.ResourceNotifications.WaitForResourceHealthyAsync("iiot-httpapi");
            await _app.ResourceNotifications.WaitForResourceAsync(
                "iiot-dataworker",
                KnownResourceStates.Running);

            _httpClient = _app.CreateHttpClient("iiot-httpapi");
        }
        catch (DistributedApplicationException ex)
            when (ex.Message.Contains("docker", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Docker 不可用，无法启动 Aspire 端到端测试环境。",
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
        _httpClient?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();

        foreach (var entry in _originalEnvironment)
        {
            Environment.SetEnvironmentVariable(entry.Key, entry.Value);
        }
    }

    private void ConfigureSeedAdminEnvironment()
    {
        SetEnvironmentVariable("SEED_ADMIN_NO", SeedAdminEmployeeNo);
        SetEnvironmentVariable("SEED_ADMIN_PASSWORD", SeedAdminPassword);
        SetEnvironmentVariable("SEED_ADMIN_REAL_NAME", SeedAdminRealName);
    }

    private void SetEnvironmentVariable(string name, string value)
    {
        if (!_originalEnvironment.ContainsKey(name))
        {
            _originalEnvironment[name] = Environment.GetEnvironmentVariable(name);
        }

        Environment.SetEnvironmentVariable(name, value);
    }
}
