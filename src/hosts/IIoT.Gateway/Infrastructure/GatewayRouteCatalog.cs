namespace IIoT.Gateway.Infrastructure;

internal static class GatewayRouteCatalog
{
    public const string RouteSurfaceHeader = "X-IIoT-Route-Surface";
    public const string DeprecatedAliasHeader = "X-IIoT-Deprecated-Alias";
    public const string ReplacementRouteHeader = "X-IIoT-Replacement-Route";
    private const string HttpApiCluster = "httpapi";

    public static GatewayRouteMetadata Resolve(PathString path)
    {
        if (path.Equals("/api/v1/bootstrap/device-instance", StringComparison.OrdinalIgnoreCase))
            return new GatewayRouteMetadata("bootstrap", "bootstrap-device-instance", HttpApiCluster, false, null);

        if (path.Equals("/api/v1/bootstrap/edge-login", StringComparison.OrdinalIgnoreCase))
            return new GatewayRouteMetadata("bootstrap", "bootstrap-edge-login", HttpApiCluster, false, null);

        if (path.Equals("/api/v1/edge/bootstrap/device-instance", StringComparison.OrdinalIgnoreCase))
        {
            return new GatewayRouteMetadata(
                "legacy-bootstrap-alias",
                "legacy-edge-bootstrap-device-instance",
                HttpApiCluster,
                true,
                "/api/v1/bootstrap/device-instance");
        }

        if (path.Equals("/api/v1/human/identity/edge-login", StringComparison.OrdinalIgnoreCase))
        {
            return new GatewayRouteMetadata(
                "legacy-edge-login-alias",
                "legacy-human-edge-login",
                HttpApiCluster,
                true,
                "/api/v1/bootstrap/edge-login");
        }

        if (path.StartsWithSegments("/api/v1/human", StringComparison.OrdinalIgnoreCase))
            return new GatewayRouteMetadata("human", "human", HttpApiCluster, false, null);

        if (path.StartsWithSegments("/api/v1/edge", StringComparison.OrdinalIgnoreCase))
            return new GatewayRouteMetadata("edge", "edge", HttpApiCluster, false, null);

        return new GatewayRouteMetadata("unknown", "unmatched", "unmatched", false, null);
    }
}
