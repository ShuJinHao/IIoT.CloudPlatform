using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace IIoT.EndToEndTests;

public sealed partial class CloudProductionFlowTests
{
    [Fact]
    public async Task CloudOidc_Discovery_ShouldExposeCodeFlowPkceAndProviderEndpoints()
    {
        _fixture.ClearAuthToken();

        using var response = await _fixture.HttpClient.GetAsync("/.well-known/openid-configuration");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        root.GetProperty("authorization_endpoint").GetString().Should().EndWith("/connect/authorize");
        root.GetProperty("token_endpoint").GetString().Should().EndWith("/connect/token");
        root.GetProperty("userinfo_endpoint").GetString().Should().EndWith("/connect/userinfo");
        root.GetProperty("end_session_endpoint").GetString().Should().EndWith("/connect/logout");
        ReadStringArray(root, "response_types_supported").Should().Contain("code");
        ReadStringArray(root, "grant_types_supported").Should().Contain("authorization_code");
        ReadStringArray(root, "code_challenge_methods_supported").Should().Contain("S256");
        var scopes = ReadStringArray(root, "scopes_supported");
        scopes.Should().Contain("openid");
        scopes.Should().Contain("profile");
    }

    private static string[] ReadStringArray(JsonElement root, string propertyName)
    {
        return root.GetProperty(propertyName)
            .EnumerateArray()
            .Select(element => element.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }
}
