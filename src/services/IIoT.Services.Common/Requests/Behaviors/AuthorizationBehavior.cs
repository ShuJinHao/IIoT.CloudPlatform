using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.Authorization;
using IIoT.Services.Common.Exceptions;
using MediatR;

namespace IIoT.Services.Common.Behaviors;

/// <summary>
/// 人员端权限点校验管道。
/// 读取请求上的 <see cref="AuthorizeRequirementAttribute"/>，
/// 再结合当前用户和权限提供器完成 RBAC 权限校验。
/// </summary>
public class AuthorizationBehavior<TRequest, TResponse>(
    ICurrentUser user,
    IPermissionProvider permissionProvider) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requiredPermissions = typeof(TRequest)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), true)
            .Cast<AuthorizeRequirementAttribute>()
            .Select(a => a.Permission)
            .ToList();

        if (requiredPermissions.Count == 0) return await next(cancellationToken);

        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.Id))
            throw new ForbiddenException("拒绝访问：用户未登录或身份令牌无效");

        if (string.Equals(user.Role, SystemRoles.Admin, StringComparison.Ordinal))
            return await next(cancellationToken);

        if (!Guid.TryParse(user.Id, out var userId))
            throw new ForbiddenException("拒绝访问：用户凭证格式异常");

        var userPermissions = await permissionProvider.GetPermissionsAsync(userId, cancellationToken);

        if (!requiredPermissions.All(p => userPermissions.Contains(p)))
            throw new ForbiddenException("拒绝访问：您的账号当前缺少执行该操作的必备权限点");

        return await next(cancellationToken);
    }
}
