using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace IIoT.Services.CrossCutting.Behaviors;

/// <summary>
/// 请求分类守卫。
/// 用来约束 HTTP 请求必须明确声明自己属于 human、edge、匿名 bootstrap 或 ai-read 四类之一，
/// 同时防止把不同入口的授权特性错误地混挂。
/// </summary>
public sealed class RequestKindGuardBehavior<TRequest, TResponse>(
    IHttpContextAccessor httpContextAccessor) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext is null)
            return await next(cancellationToken);

        var requestType = typeof(TRequest);
        var classifications = GetRequestKinds(requestType);

        if (classifications.Count == 0)
            throw new InvalidOperationException(
                $"HTTP request '{requestType.Name}' must implement exactly one request kind marker.");

        if (classifications.Count > 1)
            throw new InvalidOperationException(
                $"HTTP request '{requestType.Name}' cannot implement multiple request kind markers.");

        var hasAuthorizeRequirement = requestType
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), true)
            .Length > 0;
        var hasAuthorizeAiRead = requestType
            .GetCustomAttributes(typeof(AuthorizeAiReadAttribute), true)
            .Length > 0;
        var hasAdminOnly = requestType
            .GetCustomAttributes(typeof(AdminOnlyAttribute), true)
            .Length > 0;

        if (hasAuthorizeRequirement && classifications[0] != typeof(IHumanRequest<>))
            throw new InvalidOperationException(
                $"AuthorizeRequirementAttribute can only be applied to human requests. Invalid request: '{requestType.Name}'.");

        if (hasAdminOnly && classifications[0] != typeof(IHumanRequest<>))
            throw new InvalidOperationException(
                $"AdminOnlyAttribute can only be applied to human requests. Invalid request: '{requestType.Name}'.");

        if (hasAuthorizeAiRead && classifications[0] != typeof(IAiReadRequest<>))
            throw new InvalidOperationException(
                $"AuthorizeAiReadAttribute can only be applied to ai-read requests. Invalid request: '{requestType.Name}'.");

        if (classifications[0] == typeof(IAiReadRequest<>) && !hasAuthorizeAiRead)
            throw new InvalidOperationException(
                $"AI read request '{requestType.Name}' must declare AuthorizeAiReadAttribute.");

        return await next(cancellationToken);
    }

    private static List<Type> GetRequestKinds(Type requestType)
    {
        return requestType.GetInterfaces()
            .Where(i => i.IsGenericType)
            .Select(i => i.GetGenericTypeDefinition())
            .Where(definition =>
                definition == typeof(IHumanRequest<>) ||
                definition == typeof(IDeviceRequest<>) ||
                definition == typeof(IAnonymousBootstrapRequest<>) ||
                definition == typeof(IAiReadRequest<>))
            .Distinct()
            .ToList();
    }
}
