namespace IIoT.Gateway.Infrastructure;

internal sealed record GatewayRouteMetadata(
    string RouteSurface,
    string MatchedRoute,
    string UpstreamCluster,
    bool IsBlockedAlias);
