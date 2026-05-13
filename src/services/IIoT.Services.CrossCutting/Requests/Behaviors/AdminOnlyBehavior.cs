using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Exceptions;
using MediatR;

namespace IIoT.Services.CrossCutting.Behaviors;

/// <summary>
/// 人员端管理员专属操作守卫。
/// </summary>
public sealed class AdminOnlyBehavior<TRequest, TResponse>(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requiresAdmin = typeof(TRequest)
            .GetCustomAttributes(typeof(AdminOnlyAttribute), true)
            .Length > 0;

        if (!requiresAdmin)
            return await next(cancellationToken);

        if (!currentUserDeviceAccessService.IsAdministrator)
            throw new ForbiddenException("拒绝访问：只有管理员可以执行该操作");

        return await next(cancellationToken);
    }
}
