using System.Diagnostics;
using IIoT.Infrastructure.Logging;
using Serilog;

namespace IIoT.Gateway.Infrastructure;

internal sealed class GatewayObservabilityMiddleware(
    RequestDelegate next,
    ILogger<GatewayObservabilityMiddleware> logger,
    IDiagnosticContext diagnosticContext)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var route = GatewayRouteCatalog.Resolve(context.Request.Path);
        var stopwatch = Stopwatch.StartNew();
        var requestId = context.Request.Headers[RequestLogHeaders.RequestId].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            requestId = context.TraceIdentifier;
        }

        diagnosticContext.Set("RouteSurface", route.RouteSurface);
        diagnosticContext.Set("MatchedGatewayRoute", route.MatchedRoute);
        diagnosticContext.Set("GatewayUpstreamCluster", route.UpstreamCluster);
        diagnosticContext.Set("GatewayDeprecatedAlias", route.IsDeprecatedAlias);

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[GatewayRouteCatalog.RouteSurfaceHeader] = route.RouteSurface;

            if (route.IsDeprecatedAlias)
            {
                context.Response.Headers[GatewayRouteCatalog.DeprecatedAliasHeader] = "true";

                if (!string.IsNullOrWhiteSpace(route.ReplacementRoute))
                    context.Response.Headers[GatewayRouteCatalog.ReplacementRouteHeader] = route.ReplacementRoute;
            }

            return Task.CompletedTask;
        });

        await next(context);

        stopwatch.Stop();
        var traceId = Activity.Current?.TraceId.ToString() ?? string.Empty;

        logger.LogInformation(
            "Gateway request request_id={request_id} trace_id={trace_id} route_surface={route_surface} is_deprecated_alias={is_deprecated_alias} matched_route={matched_route} upstream_cluster={upstream_cluster} status_code={status_code} elapsed_ms={elapsed_ms}",
            requestId,
            traceId,
            route.RouteSurface,
            route.IsDeprecatedAlias,
            route.MatchedRoute,
            route.UpstreamCluster,
            context.Response.StatusCode,
            stopwatch.Elapsed.TotalMilliseconds);
    }
}
