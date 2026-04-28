using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Exceptions;
using MediatR;

namespace IIoT.Services.CrossCutting.Behaviors;

/// <summary>
/// Ensures authenticated edge requests stay bound to the device id carried by the JWT.
/// Anonymous bootstrap traffic does not implement <see cref="IDeviceRequest{TResponse}"/> and is skipped.
/// </summary>
public sealed class DeviceBindingBehavior<TRequest, TResponse>(
    ICurrentUser currentUser) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!IsDeviceRequest())
        {
            return await next(cancellationToken);
        }

        if (!currentUser.IsAuthenticated)
        {
            throw new ForbiddenException("Access denied: edge request is not authenticated.");
        }

        if (!currentUser.DeviceId.HasValue)
        {
            throw new ForbiddenException("Access denied: device token is missing a device binding.");
        }

        var requestDeviceId = ResolveRequestDeviceId(request);
        if (!requestDeviceId.HasValue)
        {
            throw new InvalidOperationException(
                $"Device request '{typeof(TRequest).Name}' must expose a public DeviceId property.");
        }

        if (requestDeviceId.Value != currentUser.DeviceId.Value)
        {
            throw new ForbiddenException("Access denied: the current token cannot operate on this device.");
        }

        return await next(cancellationToken);
    }

    private static bool IsDeviceRequest()
    {
        return typeof(TRequest).GetInterfaces()
            .Where(i => i.IsGenericType)
            .Select(i => i.GetGenericTypeDefinition())
            .Contains(typeof(IDeviceRequest<>));
    }

    private static Guid? ResolveRequestDeviceId(TRequest request)
    {
        var property = typeof(TRequest).GetProperty("DeviceId");
        if (property is null)
        {
            return null;
        }

        var value = property.GetValue(request);
        if (property.PropertyType == typeof(Guid))
        {
            return value is Guid deviceId ? deviceId : null;
        }

        if (property.PropertyType == typeof(Guid?))
        {
            return value is null
                ? null
                : value is Guid nullableDeviceId
                    ? nullableDeviceId
                    : null;
        }

        return null;
    }
}
