using System.Reflection;
using System.Text.RegularExpressions;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using MediatR;

namespace IIoT.Services.Common.Behaviors;

/// <summary>
/// 分布式锁管道。
/// 在请求类型上声明 <see cref="DistributedLockAttribute"/> 后，
/// 管道会在执行 handler 前按模板解析锁键并自动申请 Redis 分布式锁。
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
            if (prop is null)
            {
                throw new InvalidOperationException(
                    $"DistributedLock template '{template}' references missing property '{m.Groups[1].Value}' on request '{typeof(TRequest).Name}'.");
            }

            return prop.GetValue(request)?.ToString() ?? string.Empty;
        });
    }
}
