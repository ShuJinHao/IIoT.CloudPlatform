using System.Net;

namespace IIoT.Services.Contracts.Identity;

public sealed class OidcProviderOptions
{
    public const string SectionName = "OidcProvider";
    public const string AllowIntranetHttpOidcEnvironmentVariable = "ALLOW_INTRANET_HTTP_OIDC";
    private const string HostCookiePrefix = "__Host-";

    public string Issuer { get; set; } = string.Empty;
    public bool AllowIntranetHttpOidc { get; set; }
    public string AicopilotClientId { get; set; } = "aicopilot";
    public string[] AicopilotRedirectUris { get; set; } = [];
    public string[] AicopilotPostLogoutRedirectUris { get; set; } = [];
    public int AuthorizationCodeLifetimeMinutes { get; set; } = 5;
    public int AccessTokenLifetimeMinutes { get; set; } = 24 * 60;
    public int IdentityTokenLifetimeMinutes { get; set; } = 24 * 60;
    public int SessionIdleMinutes { get; set; } = 24 * 60;
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
            allowDevelopmentLoopbackHttp,
            AllowIntranetHttpOidc);

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
                allowDevelopmentLoopbackHttp,
                AllowIntranetHttpOidc);
        }

        foreach (var postLogoutRedirectUri in AicopilotPostLogoutRedirectUris)
        {
            var parsedPostLogoutRedirectUri = ParseHttpUri(
                postLogoutRedirectUri,
                $"OidcProvider:AicopilotPostLogoutRedirectUris contains an invalid URI: {postLogoutRedirectUri}");
            EnsureAllowedTransport(
                parsedPostLogoutRedirectUri,
                $"OidcProvider:AicopilotPostLogoutRedirectUris contains an insecure URI: {postLogoutRedirectUri}",
                allowDevelopmentLoopbackHttp,
                AllowIntranetHttpOidc);
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

    public string GetEffectiveSessionCookieName()
    {
        var cookieName = SessionCookieName.Trim();
        return AllowIntranetHttpOidc && cookieName.StartsWith(HostCookiePrefix, StringComparison.Ordinal)
            ? cookieName[HostCookiePrefix.Length..]
            : cookieName;
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
        bool allowDevelopmentLoopbackHttp,
        bool allowIntranetHttpOidc)
    {
        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return;
        }

        if (allowDevelopmentLoopbackHttp && uri.IsLoopback)
        {
            return;
        }

        if (allowIntranetHttpOidc && IsAllowedIntranetHttpHost(uri))
        {
            return;
        }

        throw new InvalidOperationException(
            $"{settingName} must use HTTPS; HTTP is only allowed for Development loopback endpoints or explicit intranet OIDC endpoints.");
    }

    private static bool IsAllowedIntranetHttpHost(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        if (!IPAddress.TryParse(uri.Host, out var address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 &&
            (bytes[0] == 10 ||
             (bytes[0] == 192 && bytes[1] == 168) ||
             (bytes[0] == 172 && bytes[1] is >= 16 and <= 31));
    }
}
