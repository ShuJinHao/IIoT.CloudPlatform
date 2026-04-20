using System.Diagnostics;

namespace IIoT.Gateway.Infrastructure;

internal sealed class GatewayObservabilityMiddleware(
    RequestDelegate next,
    ILogger<GatewayObservabilityMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var route = GatewayRouteCatalog.Resolve(context.Request.Path);
        var stopwatch = Stopwatch.StartNew();

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
        logger.LogInformation(
            "Gateway request route_surface={route_surface} is_deprecated_alias={is_deprecated_alias} matched_route={matched_route} upstream_cluster={upstream_cluster} status_code={status_code} elapsed_ms={elapsed_ms}",
            route.RouteSurface,
            route.IsDeprecatedAlias,
            route.MatchedRoute,
            route.UpstreamCluster,
            context.Response.StatusCode,
            stopwatch.Elapsed.TotalMilliseconds);
    }
}
