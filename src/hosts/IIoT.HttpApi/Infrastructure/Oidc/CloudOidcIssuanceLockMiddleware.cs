using System.Security.Claims;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

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
                await issuanceLock.TryAcquireTokenExchangeAsync(
                    context.RequestAborted);
            if (lease is null)
            {
                await WriteTokenExchangeCapacityRejectedAsync(context);
                return;
            }

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
            await issuanceLock.TryAcquireAuthorizationAsync(
                subjectId,
                context.RequestAborted);
        if (authorizationLease is null)
        {
            await WriteCapacityRejectedAsync(
                context,
                "OIDC authorization 请求繁忙，请稍后重试。");
            return;
        }

        await next(context);
    }

    private static async Task WriteTokenExchangeCapacityRejectedAsync(
        HttpContext context)
        => await WriteCapacityRejectedAsync(
            context,
            "OIDC token 交换繁忙，请稍后重试。");

    private static async Task WriteCapacityRejectedAsync(
        HttpContext context,
        string detail)
    {
        context.Response.StatusCode =
            StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = "1";
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "请求过于频繁",
                Type =
                    "https://developer.mozilla.org/zh-CN/docs/Web/HTTP/Status/429",
                Detail = detail
            },
            options: null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted);
    }
}
