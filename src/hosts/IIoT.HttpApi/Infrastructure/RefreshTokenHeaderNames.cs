namespace IIoT.HttpApi.Infrastructure;

public static class RefreshTokenHeaderNames
{
    public const string RefreshToken = "X-IIoT-Refresh-Token";
    public const string RefreshTokenExpiresAt = "X-IIoT-Refresh-Token-Expires-At";
    public const string AccessTokenExpiresAt = "X-IIoT-Access-Token-Expires-At";

    public static readonly string[] ExposedHeaders =
    [
        RefreshToken,
        RefreshTokenExpiresAt,
        AccessTokenExpiresAt
    ];

    public static void ApplyTo(
        HttpResponse response,
        string refreshToken,
        DateTimeOffset refreshTokenExpiresAtUtc,
        DateTimeOffset accessTokenExpiresAtUtc)
    {
        response.Headers[RefreshToken] = refreshToken;
        response.Headers[RefreshTokenExpiresAt] = refreshTokenExpiresAtUtc.ToString("O");
        response.Headers[AccessTokenExpiresAt] = accessTokenExpiresAtUtc.ToString("O");
    }
}
