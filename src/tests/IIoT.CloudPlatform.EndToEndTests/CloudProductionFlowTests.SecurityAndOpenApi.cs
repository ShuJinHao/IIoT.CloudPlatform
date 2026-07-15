using System.Text.Json;
using FluentAssertions;

namespace IIoT.CloudPlatform.EndToEndTests;

public sealed partial class CloudProductionFlowTests
{
    [Fact]
    public async Task EdgeCapacity_DeviceBinding_ShouldRejectTokenIssuedForAnotherDevice()
    {
        await AuthenticateAsAdminAsync();

        var tokenDevice = await CreateTestDeviceRegistrationAsync("binding-token");
        var payloadDevice = await CreateTestDeviceRegistrationAsync("binding-payload");
        var accessToken = await IssueEdgeUploadAccessTokenAsync(tokenDevice.DeviceId);

        using var response = await PostEdgeCapacityAsync(
            accessToken,
            payloadDevice.DeviceId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "PLC-BINDING");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("forbidden_device_scope");
    }

    [Fact]
    public async Task OpenApi_ShouldGenerateSeparatedHumanEdgeBootstrapAndAiReadDocuments()
    {
        using var httpApi = new HttpClient
        {
            BaseAddress = _fixture.GetEndpoint("iiot-httpapi", "http")
        };
        var documents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["human"] = "IIoT Human API",
            ["edge"] = "IIoT Edge API",
            ["bootstrap"] = "IIoT Bootstrap API",
            ["ai-read"] = "IIoT AI Read API"
        };

        foreach (var (documentName, expectedTitle) in documents)
        {
            using var response = await httpApi.GetAsync($"/swagger/{documentName}/swagger.json");
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(
                $"the production host must generate the {documentName} OpenAPI document: {body}");

            using var document = JsonDocument.Parse(body);
            document.RootElement.GetProperty("info").GetProperty("title").GetString()
                .Should().Be(expectedTitle);
            var paths = document.RootElement.GetProperty("paths")
                .EnumerateObject()
                .Select(path => path.Name)
                .ToArray();
            paths.Should().NotBeEmpty();

            switch (documentName)
            {
                case "edge":
                    paths.Should().OnlyContain(path => path.StartsWith("/api/v1/edge/", StringComparison.Ordinal));
                    paths.Should().NotContain(path => path.StartsWith("/api/v1/edge/bootstrap", StringComparison.Ordinal));
                    break;
                case "bootstrap":
                    paths.Should().Contain("/api/v1/edge/bootstrap/device-instance");
                    paths.Should().Contain("/api/v1/human/identity/edge-login");
                    break;
                case "ai-read":
                    paths.Should().OnlyContain(path => path.StartsWith("/api/v1/ai/read/", StringComparison.Ordinal));
                    break;
                case "human":
                    paths.Should().NotContain(path => path.StartsWith("/api/v1/edge/", StringComparison.Ordinal));
                    paths.Should().NotContain(path => path.StartsWith("/api/v1/ai/read/", StringComparison.Ordinal));
                    break;
            }
        }
    }
}
