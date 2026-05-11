using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace IIoT.EndToEndTests;

public sealed partial class CloudProductionFlowTests
{
    private const string AicopilotOidcCallbackUri = "http://127.0.0.1:5178/api/identity/cloud-oidc/callback";

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

    [Fact]
    public async Task CloudOidc_Authorize_ShouldRejectUnknownRedirectUri()
    {
        _fixture.ClearAuthToken();

        using var client = CreateNoRedirectGatewayClient();
        using var response = await client.GetAsync(CreateAuthorizePath(
            "state-redirect-guard",
            "nonce-redirect-guard",
            CreatePkceChallenge(CreatePkceVerifier()),
            "http://evil.example.com/api/identity/cloud-oidc/callback"));
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeFalse(body);
        response.Headers.Location?.ToString().Should().NotStartWith("http://evil.example.com");
    }

    [Fact]
    public async Task CloudOidc_Token_ShouldRejectWrongPkceVerifierAndCodeReplay()
    {
        _fixture.ClearAuthToken();

        var wrongVerifier = CreatePkceVerifier();
        var wrongVerifierCode = await GetAuthorizationCodeAsync(
            "state-pkce",
            "nonce-pkce",
            CreatePkceChallenge(wrongVerifier));

        using (var wrongVerifierResponse = await ExchangeAuthorizationCodeAsync(
                   wrongVerifierCode,
                   CreatePkceVerifier()))
        {
            var body = await wrongVerifierResponse.Content.ReadAsStringAsync();
            wrongVerifierResponse.IsSuccessStatusCode.Should().BeFalse(body);
        }

        var replayVerifier = CreatePkceVerifier();
        var replayCode = await GetAuthorizationCodeAsync(
            "state-replay",
            "nonce-replay",
            CreatePkceChallenge(replayVerifier));

        using (var tokenResponse = await ExchangeAuthorizationCodeAsync(replayCode, replayVerifier))
        {
            var body = await tokenResponse.Content.ReadAsStringAsync();
            tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK, body);
            body.Should().Contain("id_token");
            body.Should().Contain("access_token");
        }

        using (var replayResponse = await ExchangeAuthorizationCodeAsync(replayCode, replayVerifier))
        {
            var body = await replayResponse.Content.ReadAsStringAsync();
            replayResponse.IsSuccessStatusCode.Should().BeFalse(body);
        }
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

    private async Task<string> GetAuthorizationCodeAsync(
        string state,
        string nonce,
        string codeChallenge)
    {
        var sessionCookie = await LoginAndReadOidcSessionCookieAsync();
        using var client = CreateNoRedirectGatewayClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            CreateAuthorizePath(state, nonce, codeChallenge, AicopilotOidcCallbackUri));
        request.Headers.TryAddWithoutValidation("Cookie", sessionCookie);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Redirect, body);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().StartWith(AicopilotOidcCallbackUri);

        var code = GetQueryValue(response.Headers.Location!, "code");
        code.Should().NotBeNullOrWhiteSpace();
        return code!;
    }

    private async Task<string> LoginAndReadOidcSessionCookieAsync()
    {
        using var loginResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/human/identity/login", new
        {
            EmployeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
            Password = IIoTAppFixture.SeedAdminPassword
        });

        var body = await loginResponse.Content.ReadAsStringAsync();
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, body);
        loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue(
            "Cloud human login should create the OIDC server session cookie.");

        var oidcCookie = cookies!
            .FirstOrDefault(cookie => cookie.StartsWith("__Host-IIoT-OidcSession=", StringComparison.Ordinal));
        oidcCookie.Should().NotBeNullOrWhiteSpace(
            "AICopilot must never receive Cloud cookies directly, but Cloud authorize needs its own OIDC session cookie.");

        return oidcCookie!.Split(';', 2)[0];
    }

    private Task<HttpResponseMessage> ExchangeAuthorizationCodeAsync(string code, string verifier)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "aicopilot",
            ["code"] = code,
            ["redirect_uri"] = AicopilotOidcCallbackUri,
            ["code_verifier"] = verifier
        });

        return _fixture.HttpClient.PostAsync("/connect/token", content);
    }

    private HttpClient CreateNoRedirectGatewayClient()
    {
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = _fixture.HttpClient.BaseAddress
        };
    }

    private static string CreateAuthorizePath(
        string state,
        string nonce,
        string codeChallenge,
        string redirectUri)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = "aicopilot",
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid profile",
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        return "/connect/authorize?" + string.Join(
            "&",
            parameters.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
    }

    private static string CreatePkceVerifier()
    {
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    private static string CreatePkceChallenge(string verifier)
    {
        return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string? GetQueryValue(Uri uri, string key)
    {
        return uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .Where(pair => string.Equals(Uri.UnescapeDataString(pair[0]), key, StringComparison.Ordinal))
            .Select(pair => Uri.UnescapeDataString(pair[1]))
            .FirstOrDefault();
    }
}
