using System.Reflection;
using System.Text.RegularExpressions;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using MediatR;

namespace IIoT.Services.Common.Behaviors;

/// <summary>
/// 分布式锁管道行为，与 AuthorizationBehavior 保持一致的 AOP 用法。
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
