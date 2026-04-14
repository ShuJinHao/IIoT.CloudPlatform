using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace IIoT.Services.Common.Behaviors;

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

        if (hasAuthorizeRequirement && classifications[0] != typeof(IHumanRequest<>))
            throw new InvalidOperationException(
                $"AuthorizeRequirementAttribute can only be applied to human requests. Invalid request: '{requestType.Name}'.");

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
                definition == typeof(IAnonymousBootstrapRequest<>))
            .Distinct()
            .ToList();
    }
}
