using System.Net.Http.Headers;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Logging;

namespace IIoT.EndToEndTests;

public sealed class IIoTAppFixture : IAsyncDisposable
{
    private DistributedApplication? _app;
    private HttpClient? _httpClient;

    public HttpClient HttpClient => _httpClient ?? throw new InvalidOperationException("环境未启动。");

    public async Task StartAsync()
    {
        try
        {
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
    }
}
