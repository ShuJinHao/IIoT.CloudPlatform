using System.Diagnostics;
using System.Security.Claims;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.AiRead;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.AspNetCore.Http;
using IResult = IIoT.SharedKernel.Result.IResult;

namespace IIoT.Services.CrossCutting.Behaviors;

public sealed class AiReadAuditBehavior<TRequest, TResponse>(
    IHttpContextAccessor httpContextAccessor,
    IAuditTrailService auditTrailService) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext is null || !IsAiReadRequest())
            return await next(cancellationToken);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next(cancellationToken);
            stopwatch.Stop();

            await WriteAuditAsync(response, succeeded: true, stopwatch.Elapsed, null, cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await WriteAuditAsync(default, succeeded: false, stopwatch.Elapsed, ex.Message, cancellationToken);
            throw;
        }
    }

    private async Task WriteAuditAsync(
        TResponse? response,
        bool succeeded,
        TimeSpan elapsed,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var principal = httpContext?.User;
        var metadata = ExtractMetadata(response);
        var requestId = httpContext?.TraceIdentifier ?? string.Empty;
        var endpoint = httpContext?.Request.Path.Value ?? typeof(TRequest).Name;
        var delegatedUserId = principal?.FindFirst(IIoTClaimTypes.DelegatedUserId)?.Value;
        var delegatedDeviceCount = principal?.FindAll(IIoTClaimTypes.DelegatedDeviceId).Count() ?? 0;

        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ResolveActorUserId(principal),
                ResolveCaller(principal),
                "AiRead.Query",
                "AiRead",
                typeof(TRequest).Name,
                DateTime.UtcNow,
                succeeded,
                Truncate(BuildSummary(endpoint, requestId, elapsed, delegatedUserId, delegatedDeviceCount, metadata), 512)
                    ?? string.Empty,
                Truncate(failureReason, 512)),
            cancellationToken);
    }

    private static IAiReadResponseMetadata? ExtractMetadata(TResponse? response)
    {
        return response is IResult result
            ? result.GetValue() as IAiReadResponseMetadata
            : null;
    }

    private static string BuildSummary(
        string endpoint,
        string requestId,
        TimeSpan elapsed,
        string? delegatedUserId,
        int delegatedDeviceCount,
        IAiReadResponseMetadata? metadata)
    {
        var source = metadata?.Source ?? "unknown";
        var scope = metadata?.QueryScope ?? "unavailable";
        var rowCount = metadata?.RowCount ?? 0;
        var truncated = metadata?.Truncated ?? false;

        return
            $"endpoint={endpoint}; source={source}; scope={scope}; rowCount={rowCount}; truncated={truncated}; " +
            $"latencyMs={(long)elapsed.TotalMilliseconds}; requestId={requestId}; " +
            $"delegatedUserId={delegatedUserId ?? "none"}; delegatedDeviceCount={delegatedDeviceCount}";
    }

    private static Guid? ResolveActorUserId(ClaimsPrincipal? principal)
    {
        var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(subject, out var actorUserId) ? actorUserId : null;
    }

    private static string? ResolveCaller(ClaimsPrincipal? principal)
    {
        return principal?.FindFirstValue(ClaimTypes.Name)
               ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    private static bool IsAiReadRequest()
    {
        return typeof(TRequest).GetInterfaces()
            .Where(i => i.IsGenericType)
            .Select(i => i.GetGenericTypeDefinition())
            .Contains(typeof(IAiReadRequest<>));
    }
}
