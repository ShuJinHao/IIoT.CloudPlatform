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
        if (!Uri.TryCreate(Issuer, UriKind.Absolute, out var issuer) ||
            (issuer.Scheme != Uri.UriSchemeHttps && issuer.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("OidcProvider:Issuer must be an absolute http/https URI.");
        }

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
            if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException(
                    $"OidcProvider:AicopilotRedirectUris contains an invalid URI: {redirectUri}");
            }
        }

        foreach (var postLogoutRedirectUri in AicopilotPostLogoutRedirectUris)
        {
            if (!Uri.TryCreate(postLogoutRedirectUri, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException(
                    $"OidcProvider:AicopilotPostLogoutRedirectUris contains an invalid URI: {postLogoutRedirectUri}");
            }
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
}
