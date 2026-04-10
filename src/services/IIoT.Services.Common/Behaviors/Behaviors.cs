using System.Reflection;
using System.Text.RegularExpressions;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Exceptions;
using MediatR;

namespace IIoT.Services.Common.Behaviors;

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

        // Admin 直接放行
        if (user.Role == "Admin") return await next(cancellationToken);

        // 解析当前用户 Guid
        if (!Guid.TryParse(user.Id, out var userId))
            throw new ForbiddenException("拒绝访问：用户凭证格式异常");

        // 查当前用户的权限点集合
        var userPermissions = await permissionProvider.GetPermissionsAsync(userId, cancellationToken);

        if (!requiredPermissions.All(p => userPermissions.Contains(p)))
            throw new ForbiddenException("拒绝访问：您的账号当前缺少执行该操作的必备权限点");

        return await next(cancellationToken);
    }
}

/// <summary>
/// 分布式锁管道行为 — 与 AuthorizationBehavior 完全相同的 AOP 模式。
/// 在 Command 类上标注 [DistributedLock("key:{PropA}:{PropB}")] 即可自动加锁。
/// </summary>
public class DistributedLockBehavior<TRequest, TResponse>(
    IDistributedLockService lockService) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var attr = typeof(TRequest).GetCustomAttribute<DistributedLockAttribute>();
        if (attr is null) return await next(cancellationToken);

        var key = ResolveKey(attr.KeyTemplate, request);
        await using var _ = await lockService.AcquireAsync(
            key,
            TimeSpan.FromSeconds(attr.TimeoutSeconds),
            cancellationToken);

        return await next(cancellationToken);
    }

    /// <summary>将模板 "iiot:lock:{DeviceId}:{Date}" 中的占位符替换为 request 对应属性的值</summary>
    private static string ResolveKey(string template, TRequest request)
    {
        return Regex.Replace(template, @"\{(\w+)\}", m =>
        {
            var prop = typeof(TRequest).GetProperty(
                m.Groups[1].Value,
                BindingFlags.Public | BindingFlags.Instance);
            return prop?.GetValue(request)?.ToString() ?? m.Value;
        });
    }
}