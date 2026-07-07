using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Exceptions;
using MediatR;

namespace IIoT.Services.CrossCutting.Behaviors;

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
    private static readonly HashSet<string> EdgeReleasePublisherAllowedPermissions =
    [
        ClientReleasePermissions.Read,
        ClientReleasePermissions.Publish
    ];

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

        if (requiredPermissions.Count == 0)
        {
            if (string.Equals(user.ActorType, IIoTClaimTypes.EdgeReleasePublisherActor, StringComparison.Ordinal) &&
                IsHumanRequest(typeof(TRequest)))
            {
                throw new ForbiddenException("拒绝访问：发布机器凭据不能执行未声明权限点的人员端请求");
            }

            return await next(cancellationToken);
        }

        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.Id))
            throw new ForbiddenException("拒绝访问：用户未登录或身份令牌无效");

        if (string.Equals(user.ActorType, IIoTClaimTypes.EdgeReleasePublisherActor, StringComparison.Ordinal))
        {
            if (requiredPermissions.Any(permission => !EdgeReleasePublisherAllowedPermissions.Contains(permission)))
                throw new ForbiddenException("拒绝访问：发布机器凭据只能执行客户端发布读取和上传操作");

            if (!requiredPermissions.All(permission => user.Permissions.Contains(permission)))
                throw new ForbiddenException("拒绝访问：发布机器凭据缺少执行该操作的必备权限点");

            return await next(cancellationToken);
        }

        if (user.Roles.Contains(SystemRoles.Admin, StringComparer.Ordinal))
            return await next(cancellationToken);

        if (!Guid.TryParse(user.Id, out var userId))
            throw new ForbiddenException("拒绝访问：用户凭证格式异常");

        var userPermissions = await permissionProvider.GetPermissionsAsync(userId, cancellationToken);

        if (!requiredPermissions.All(p => userPermissions.Contains(p)))
            throw new ForbiddenException("拒绝访问：您的账号当前缺少执行该操作的必备权限点");

        return await next(cancellationToken);
    }

    private static bool IsHumanRequest(Type requestType)
        => requestType.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHumanRequest<>));
}
