using System.Diagnostics;
using IIoT.Infrastructure.Logging;
using Serilog;

namespace IIoT.Gateway.Infrastructure;

internal sealed class GatewayObservabilityMiddleware(
    RequestDelegate next,
    IGatewayRouteCatalog routeCatalog,
    ILogger<GatewayObservabilityMiddleware> logger,
    IDiagnosticContext diagnosticContext)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var route = routeCatalog.Resolve(context.Request.Path);
        var stopwatch = Stopwatch.StartNew();
        var requestId = context.Request.Headers[RequestLogHeaders.RequestId].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            requestId = context.TraceIdentifier;
        }

        diagnosticContext.Set("RouteSurface", route.RouteSurface);
        diagnosticContext.Set("MatchedGatewayRoute", route.MatchedRoute);
        diagnosticContext.Set("GatewayUpstreamCluster", route.UpstreamCluster);
        diagnosticContext.Set("GatewayBlockedAlias", route.IsBlockedAlias);

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[GatewayRouteCatalog.RouteSurfaceHeader] = route.RouteSurface;

            return Task.CompletedTask;
        });

        if (route.IsBlockedAlias)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }
        else
        {
            await next(context);
        }

        stopwatch.Stop();
        var traceId = Activity.Current?.TraceId.ToString() ?? string.Empty;

        logger.LogInformation(
            "Gateway request request_id={request_id} trace_id={trace_id} route_surface={route_surface} is_blocked_alias={is_blocked_alias} matched_route={matched_route} upstream_cluster={upstream_cluster} status_code={status_code} elapsed_ms={elapsed_ms}",
            requestId,
            traceId,
            route.RouteSurface,
            route.IsBlockedAlias,
            route.MatchedRoute,
            route.UpstreamCluster,
            context.Response.StatusCode,
            stopwatch.Elapsed.TotalMilliseconds);
    }
}
