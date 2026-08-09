using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Exceptions;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace IIoT.Services.CrossCutting.Behaviors;

/// <summary>
/// AI 只读接口专用授权管道。
/// 只接受当前 Cloud 用户的短期委托，并在每次请求中实时读取账号、员工、角色、权限和设备范围。
/// </summary>
public sealed class AiReadAuthorizationBehavior<TRequest, TResponse>(
    IHttpContextAccessor httpContextAccessor,
    IAiReadDelegatedAuthorizationService delegatedAuthorizationService,
    IAiReadAuthorizationContext authorizationContext) : IPipelineBehavior<TRequest, TResponse>
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

        if (!HasSingleClaimValue(
                principal,
                IIoTClaimTypes.ActorType,
                IIoTClaimTypes.AiDelegatedUserActor) ||
            !HasAudience(principal, AiReadDelegationDefaults.Audience) ||
            !HasScope(principal, AiReadDelegationDefaults.Scope) ||
            principal.HasClaim(claim =>
                claim.Type == IIoTClaimTypes.Permission ||
                claim.Type == IIoTClaimTypes.DelegatedDeviceId))
        {
            throw new ForbiddenException("拒绝访问：AiRead 当前用户委托声明缺失、畸形或包含禁止的授权快照");
        }

        var subjects = principal.FindAll("sub").Select(claim => claim.Value).ToArray();
        var delegatedUsers = principal
            .FindAll(IIoTClaimTypes.DelegatedUserId)
            .Select(claim => claim.Value)
            .ToArray();
        var statusVersions = principal
            .FindAll(IIoTClaimTypes.IdentityStatusVersion)
            .Select(claim => claim.Value)
            .ToArray();
        if (subjects.Length != 1 ||
            delegatedUsers.Length != 1 ||
            statusVersions.Length != 1 ||
            !Guid.TryParse(subjects[0], out var subjectUserId) ||
            subjectUserId == Guid.Empty ||
            !Guid.TryParse(delegatedUsers[0], out var delegatedUserId) ||
            delegatedUserId != subjectUserId ||
            string.IsNullOrWhiteSpace(statusVersions[0]))
        {
            throw new ForbiddenException("拒绝访问：AiRead 委托主体或状态版本无效");
        }

        AiReadDelegatedAuthorization? authorization;
        try
        {
            authorization = await delegatedAuthorizationService.AuthorizeAsync(
                delegatedUserId,
                statusVersions[0],
                requiredPermissions,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            throw new ForbiddenException("拒绝访问：AiRead 实时授权状态不可验证");
        }

        if (authorization is null)
            throw new ForbiddenException("拒绝访问：Cloud 用户状态、权限或设备范围不再有效");

        authorizationContext.Initialize(authorization);

        return await next(cancellationToken);
    }

    private static bool HasSingleClaimValue(
        System.Security.Claims.ClaimsPrincipal principal,
        string claimType,
        string expectedValue)
    {
        var values = principal.FindAll(claimType).Select(claim => claim.Value).ToArray();
        return values.Length == 1 &&
               string.Equals(values[0], expectedValue, StringComparison.Ordinal);
    }

    private static bool HasAudience(
        System.Security.Claims.ClaimsPrincipal principal,
        string requiredAudience)
    {
        var audiences = principal.FindAll("aud")
            .Select(claim => claim.Value)
            .ToArray();
        return audiences.Length == 1 &&
               string.Equals(audiences[0], requiredAudience, StringComparison.Ordinal);
    }

    private static bool HasScope(
        System.Security.Claims.ClaimsPrincipal principal,
        string requiredScope)
    {
        var scopeClaims = principal.FindAll("scope")
            .Select(claim => claim.Value)
            .ToArray();
        if (scopeClaims.Length != 1 || string.IsNullOrWhiteSpace(scopeClaims[0]))
            return false;

        var scopes = scopeClaims[0].Split(' ', StringSplitOptions.None);
        return scopes.All(scope => !string.IsNullOrWhiteSpace(scope)) &&
               scopes.Distinct(StringComparer.Ordinal).Count() == scopes.Length &&
               scopes.Contains(requiredScope, StringComparer.Ordinal);
    }
}
