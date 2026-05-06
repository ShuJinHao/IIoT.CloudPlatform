using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Exceptions;
using IIoT.Services.Contracts.Identity;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace IIoT.Services.CrossCutting.Behaviors;

/// <summary>
/// AI 只读接口专用授权管道。
/// 只接受 actor_type=ai-service-account 的 service account token，并校验独立 AiRead.* 权限点。
/// </summary>
public sealed class AiReadAuthorizationBehavior<TRequest, TResponse>(
    IHttpContextAccessor httpContextAccessor) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requiredPermissions = typeof(TRequest)
            .GetCustomAttributes(typeof(AuthorizeAiReadAttribute), true)
            .Cast<AuthorizeAiReadAttribute>()
            .Select(attribute => attribute.Permission)
            .ToList();

        if (requiredPermissions.Count == 0)
            return await next(cancellationToken);

        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            throw new ForbiddenException("拒绝访问：AiRead 请求未认证或身份令牌无效");

        var actorType = principal.FindFirst(IIoTClaimTypes.ActorType)?.Value;
        if (!string.Equals(actorType, IIoTClaimTypes.AiServiceActor, StringComparison.Ordinal))
            throw new ForbiddenException("拒绝访问：AiRead 接口只接受 AI service account 身份");

        var grantedPermissions = principal
            .FindAll(IIoTClaimTypes.Permission)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        if (!requiredPermissions.All(grantedPermissions.Contains))
            throw new ForbiddenException("拒绝访问：AI service account 缺少必需的 AiRead 权限点");

        return await next(cancellationToken);
    }
}
