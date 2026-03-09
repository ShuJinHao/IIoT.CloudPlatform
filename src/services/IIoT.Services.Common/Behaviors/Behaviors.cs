using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Exceptions;
using MediatR;

namespace IIoT.Services.Common.Behaviors;

/// <summary>
/// 全局用例授权拦截器 (MediatR Pipeline Behavior)
/// </summary>
/// <typeparam name="TRequest">进入管道的 Command 或 Query</typeparam>
/// <typeparam name="TResponse">用例返回的结果类型</typeparam>
/// <remarks>
/// 架构说明：
/// 1. 本拦截器会在所有标记了 [AuthorizeRequirement] 的用例执行前自动触发。
/// 2. 强依赖倒置：通过注入 IPermissionProvider，本拦截器对底层的 Redis 缓存或 PGSQL 数据库毫无察觉，
///    完美实现了“鉴权逻辑”与“权限数据存储”的物理隔离。
/// </remarks>
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
        // 1. 反射提取当前 Command/Query 头上标记的所有权限需求点
        var requiredPermissions = typeof(TRequest)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), true)
            .Cast<AuthorizeRequirementAttribute>()
            .Select(a => a.Permission)
            .ToList();

        // 如果该用例没有标记任何权限要求，直接放行，不消耗任何性能
        if (requiredPermissions.Count == 0)
            return await next(cancellationToken);

        // 2. 基础身份校验：必须是已登录的合法用户
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.Role))
            throw new ForbiddenException("拒绝访问：用户未登录或身份令牌无效");

        // 3. 🌟 上帝模式 (性能压榨点)：
        // 超级管理员直接无条件放行！这使得最高频的 Admin 操作连查 Redis 的这 1 毫秒都省了。
        if (user.Role == "Admin")
            return await next(cancellationToken);

        // 4. 动态数据源加载：
        // 调用我们刚刚定义好的契约接口。此时底层会自动处理 Redis 缓存命中和 DB 兜底。
        // 业务层根本不需要操心它是怎么查出来的，只需要拿结果即可。
        var userPermissions = await permissionProvider.GetPermissionsAsync(user.Role, cancellationToken);

        // 5. 严格交集鉴权：
        // 核心要求：用例所需要的所有权限（All），当前用户的权限列表中必须全部包含。
        // 如果底层返回空集合，这里的 Contains 必然为 false，完美形成闭环拦截。
        if (!requiredPermissions.All(p => userPermissions.Contains(p)))
            throw new ForbiddenException("拒绝访问：您的账号当前缺少执行该操作的必备权限点");

        // 6. 校验通过，放行到实际的业务 Handler 去执行核心逻辑
        return await next(cancellationToken);
    }
}