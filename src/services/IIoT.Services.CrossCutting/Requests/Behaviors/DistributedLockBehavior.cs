using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IIoT.Services.CrossCutting.Behaviors;

/// <summary>
/// 分布式锁管道。
/// 在请求类型上声明 <see cref="DistributedLockAttribute"/> 后，
/// 管道会在执行 handler 前按模板解析锁键并自动申请 Redis 分布式锁。
/// </summary>
public class DistributedLockBehavior<TRequest, TResponse>(
    IDistributedLockService lockService,
    ILogger<DistributedLockBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private const string InvalidLockKeyMessage = "Distributed lock key could not be resolved.";

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var attr = typeof(TRequest).GetCustomAttribute<DistributedLockAttribute>();
        if (attr is null) return await next(cancellationToken);

        var key = ResolveKey(attr.KeyTemplate, request);
        var lease = await lockService.AcquireAsync(
            key,
            TimeSpan.FromSeconds(attr.TimeoutSeconds),
            cancellationToken);
        var ownershipLost = lease.OwnershipLost;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            ownershipLost);

        TResponse? response = default;
        Exception? operationException = null;
        try
        {
            response = await next(linkedCancellation.Token);
            if (ownershipLost.IsCancellationRequested)
                operationException = new DistributedLockOwnershipLostException();
        }
        catch (Exception exception)
        {
            operationException = cancellationToken.IsCancellationRequested
                && exception is OperationCanceledException
                ? new OperationCanceledException(cancellationToken)
                : ownershipLost.IsCancellationRequested
                    ? new DistributedLockOwnershipLostException()
                    : exception;
        }

        Exception? disposeException = null;
        try
        {
            await lease.DisposeAsync();
        }
        catch (Exception exception)
        {
            disposeException = exception;
        }

        if (operationException is not null)
        {
            if (disposeException is not null)
            {
                logger.LogError(
                    new EventId(4404, "DistributedLockReleaseAfterHandlerFailure"),
                    "Distributed lock release failed after the handler failed. ErrorType={ErrorType}.",
                    disposeException.GetType().Name);
            }

            ExceptionDispatchInfo.Capture(operationException).Throw();
        }

        if (disposeException is not null)
            ExceptionDispatchInfo.Capture(disposeException).Throw();

        if (ownershipLost.IsCancellationRequested)
            throw new DistributedLockOwnershipLostException();

        return response!;
    }

    private static string ResolveKey(string template, TRequest request)
    {
        return Regex.Replace(template, @"\{(\w+)\}", m =>
        {
            var prop = typeof(TRequest).GetProperty(
                m.Groups[1].Value,
                BindingFlags.Public | BindingFlags.Instance);
            if (prop is null)
                throw new InvalidOperationException(InvalidLockKeyMessage);

            var value = prop.GetValue(request)?.ToString();
            return string.IsNullOrWhiteSpace(value)
                ? throw new InvalidOperationException(InvalidLockKeyMessage)
                : value;
        });
    }
}
