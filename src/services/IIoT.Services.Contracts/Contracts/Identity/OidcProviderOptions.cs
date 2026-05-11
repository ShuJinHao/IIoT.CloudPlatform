namespace IIoT.Services.Contracts.Identity;

public sealed class OidcProviderOptions
{
    public const string SectionName = "OidcProvider";

    public string Issuer { get; set; } = string.Empty;
    public string AicopilotClientId { get; set; } = "aicopilot";
    public string[] AicopilotRedirectUris { get; set; } = [];
    public string[] AicopilotPostLogoutRedirectUris { get; set; } = [];
    public int AuthorizationCodeLifetimeMinutes { get; set; } = 5;
    public int AccessTokenLifetimeMinutes { get; set; } = 10;
    public int IdentityTokenLifetimeMinutes { get; set; } = 10;
    public int SessionIdleMinutes { get; set; } = 30;
    public string SessionCookieName { get; set; } = "__Host-IIoT-OidcSession";
    public string? SigningCertificatePath { get; set; }
    public string? SigningCertificatePassword { get; set; }
    public string? EncryptionCertificatePath { get; set; }
    public string? EncryptionCertificatePassword { get; set; }

    public void Validate()
    {
        Validate("Production");
    }

    public void Validate(string environmentName)
    {
        var allowDevelopmentLoopbackHttp = string.Equals(
            environmentName,
            "Development",
            StringComparison.OrdinalIgnoreCase);

        var issuer = ParseHttpUri(Issuer, "OidcProvider:Issuer");
        EnsureAllowedTransport(
            issuer,
            "OidcProvider:Issuer",
            allowDevelopmentLoopbackHttp);

        if (string.IsNullOrWhiteSpace(AicopilotClientId))
        {
            throw new InvalidOperationException("OidcProvider:AicopilotClientId is required.");
        }

        if (AicopilotRedirectUris.Length == 0)
        {
            throw new InvalidOperationException("OidcProvider:AicopilotRedirectUris must contain at least one URI.");
        }

        foreach (var redirectUri in AicopilotRedirectUris)
        {
            var parsedRedirectUri = ParseHttpUri(
                redirectUri,
                $"OidcProvider:AicopilotRedirectUris contains an invalid URI: {redirectUri}");
            EnsureAllowedTransport(
                parsedRedirectUri,
                $"OidcProvider:AicopilotRedirectUris contains an insecure URI: {redirectUri}",
                allowDevelopmentLoopbackHttp);
        }

        foreach (var postLogoutRedirectUri in AicopilotPostLogoutRedirectUris)
        {
            var parsedPostLogoutRedirectUri = ParseHttpUri(
                postLogoutRedirectUri,
                $"OidcProvider:AicopilotPostLogoutRedirectUris contains an invalid URI: {postLogoutRedirectUri}");
            EnsureAllowedTransport(
                parsedPostLogoutRedirectUri,
                $"OidcProvider:AicopilotPostLogoutRedirectUris contains an insecure URI: {postLogoutRedirectUri}",
                allowDevelopmentLoopbackHttp);
        }

        if (AuthorizationCodeLifetimeMinutes <= 0)
        {
            throw new InvalidOperationException("OidcProvider:AuthorizationCodeLifetimeMinutes must be greater than 0.");
        }

        if (AccessTokenLifetimeMinutes <= 0)
        {
            throw new InvalidOperationException("OidcProvider:AccessTokenLifetimeMinutes must be greater than 0.");
        }

        if (IdentityTokenLifetimeMinutes <= 0)
        {
            throw new InvalidOperationException("OidcProvider:IdentityTokenLifetimeMinutes must be greater than 0.");
        }

        if (SessionIdleMinutes <= 0)
        {
            throw new InvalidOperationException("OidcProvider:SessionIdleMinutes must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(SessionCookieName))
        {
            throw new InvalidOperationException("OidcProvider:SessionCookieName is required.");
        }
    }

    private static Uri ParseHttpUri(string value, string settingName)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException($"{settingName} must be an absolute http/https URI.");
        }

        return uri;
    }

    private static void EnsureAllowedTransport(
        Uri uri,
        string settingName,
        bool allowDevelopmentLoopbackHttp)
    {
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return;
        }

        if (allowDevelopmentLoopbackHttp && uri.IsLoopback)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{settingName} must use HTTPS; HTTP is only allowed for Development loopback endpoints.");
    }
}
