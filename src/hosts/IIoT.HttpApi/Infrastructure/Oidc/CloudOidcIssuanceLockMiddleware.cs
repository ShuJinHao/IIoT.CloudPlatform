using System.Security.Claims;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Authentication;

namespace IIoT.HttpApi.Infrastructure.Oidc;

public sealed class CloudOidcIssuanceLockMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IHumanSessionIssuanceLock issuanceLock)
    {
        if (string.Equals(
                context.Request.Path.Value,
                "/connect/token",
                StringComparison.OrdinalIgnoreCase))
        {
            await using var lease =
                await issuanceLock.AcquireTokenExchangeAsync(
                    context.RequestAborted);
            await next(context);
            return;
        }

        if (!string.Equals(
                context.Request.Path.Value,
                "/connect/authorize",
                StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var authentication = await context.AuthenticateAsync(
            CloudOidcDefaults.SessionScheme);
        if (!Guid.TryParse(
                authentication.Principal?.FindFirstValue(
                    ClaimTypes.NameIdentifier),
                out var subjectId))
        {
            await next(context);
            return;
        }

        await using var authorizationLease =
            await issuanceLock.AcquireAuthorizationAsync(
                subjectId,
                context.RequestAborted);
        await next(context);
    }
}
