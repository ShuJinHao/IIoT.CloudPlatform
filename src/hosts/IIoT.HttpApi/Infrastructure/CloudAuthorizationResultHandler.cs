using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;

namespace IIoT.HttpApi.Infrastructure;

public sealed class CloudAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "未认证或凭据无效",
                "访问令牌无效或缺失。",
                CloudProblemCodes.InvalidToken);
            return;
        }

        if (authorizeResult.Forbidden)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status403Forbidden,
                "禁止访问",
                "当前令牌不具备访问该资源的授权范围。",
                CloudProblemCodes.ForbiddenDeviceScope);
            return;
        }

        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int status,
        string title,
        string detail,
        string code)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Instance = context.Request.Path
            }.AddCode(code));
    }
}
