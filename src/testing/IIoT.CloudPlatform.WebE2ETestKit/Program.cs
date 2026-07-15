using System.Text.Json;
using IIoT.CloudPlatform.IntegrationTestKit;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    throw new ArgumentException("Expected one absolute state-file path.");
}

var statePath = Path.GetFullPath(args[0]);
var temporaryRoot = Path.GetFullPath(Path.GetTempPath());
var relativeStatePath = Path.GetRelativePath(temporaryRoot, statePath);
if (Path.IsPathRooted(relativeStatePath) ||
    relativeStatePath == ".." ||
    relativeStatePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
{
    throw new InvalidOperationException("The credential-bearing state file must stay under the OS temporary directory.");
}

Directory.CreateDirectory(Path.GetDirectoryName(statePath)
                          ?? throw new InvalidOperationException("State-file directory is missing."));

await using var fixture = new IIoTAppFixture();
await fixture.StartAsync();

var webEndpoint = fixture.GetEndpoint("iiot-web", "http");
var gatewayEndpoint = fixture.GetEndpoint("iiot-gateway", "http");
using var readinessClient = new HttpClient { BaseAddress = webEndpoint };
using var readinessTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
while (true)
{
    readinessTimeout.Token.ThrowIfCancellationRequested();
    try
    {
        using var response = await readinessClient.GetAsync("/login", readinessTimeout.Token);
        if (response.IsSuccessStatusCode)
        {
            break;
        }
    }
    catch (HttpRequestException) when (!readinessTimeout.IsCancellationRequested)
    {
        // Vite can receive its endpoint before the development server is ready.
    }

    await Task.Delay(250, readinessTimeout.Token);
}

var state = new
{
    schemaVersion = 1,
    webUrl = webEndpoint.AbsoluteUri.TrimEnd('/'),
    gatewayUrl = gatewayEndpoint.AbsoluteUri.TrimEnd('/'),
    employeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
    password = IIoTAppFixture.SeedAdminPassword
};
var temporaryPath = statePath + ".tmp";
await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(state));
if (!OperatingSystem.IsWindows())
{
    File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
}
File.Move(temporaryPath, statePath, overwrite: true);
Console.WriteLine($"CLOUD_WEB_E2E_HOST_READY state={statePath}");

// The Node orchestrator closes stdin after Playwright exits; disposal then tears down Aspire
// and its isolated database/message-bus volumes. Missing Docker or dependencies fail the run.
await Console.In.ReadLineAsync();
