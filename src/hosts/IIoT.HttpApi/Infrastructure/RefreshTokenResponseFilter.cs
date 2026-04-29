using Microsoft.AspNetCore.Mvc.Filters;

namespace IIoT.HttpApi.Infrastructure;

public sealed record RefreshTokenResponseHeaders(
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    DateTimeOffset AccessTokenExpiresAtUtc);

public sealed class RefreshTokenResponseFilter : IAsyncResultFilter
{
    private const string ItemKey = "__IIoT_REFRESH_TOKEN_RESPONSE_HEADERS";

    public static void SetHeaders(
        HttpContext httpContext,
        string refreshToken,
        DateTimeOffset refreshTokenExpiresAtUtc,
        DateTimeOffset accessTokenExpiresAtUtc)
    {
        httpContext.Items[ItemKey] = new RefreshTokenResponseHeaders(
            refreshToken,
            refreshTokenExpiresAtUtc,
            accessTokenExpiresAtUtc);
    }

    public async Task OnResultExecutionAsync(
        ResultExecutingContext context,
        ResultExecutionDelegate next)
    {
        if (context.HttpContext.Items.TryGetValue(ItemKey, out var value)
            && value is RefreshTokenResponseHeaders headers)
        {
            RefreshTokenHeaderNames.ApplyTo(
                context.HttpContext.Response,
                headers.RefreshToken,
                headers.RefreshTokenExpiresAtUtc,
                headers.AccessTokenExpiresAtUtc);
        }

        await next();
    }
}
