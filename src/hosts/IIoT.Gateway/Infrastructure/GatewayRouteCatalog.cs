using Microsoft.Extensions.Configuration;

namespace IIoT.Gateway.Infrastructure;

internal interface IGatewayRouteCatalog
{
    GatewayRouteMetadata Resolve(PathString path);
}

internal sealed class GatewayRouteCatalog : IGatewayRouteCatalog
{
    public const string RouteSurfaceHeader = "X-IIoT-Route-Surface";

    private readonly IReadOnlyList<ConfiguredGatewayRoute> _blockedRoutes;
    private readonly IReadOnlyList<ConfiguredGatewayRoute> _proxyRoutes;

    public GatewayRouteCatalog(IConfiguration configuration)
    {
        _blockedRoutes = LoadBlockedRoutes(configuration).ToArray();
        _proxyRoutes = LoadProxyRoutes(configuration).ToArray();
    }

    public GatewayRouteMetadata Resolve(PathString path)
    {
        var requestPath = path.Value ?? string.Empty;
        var blocked = FindMatch(_blockedRoutes, requestPath);
        if (blocked is not null)
        {
            return new GatewayRouteMetadata(
                blocked.RouteSurface,
                blocked.RouteName,
                "blocked",
                true);
        }

        var proxy = FindMatch(_proxyRoutes, requestPath);
        if (proxy is not null)
        {
            return new GatewayRouteMetadata(
                proxy.RouteSurface,
                proxy.RouteName,
                proxy.UpstreamCluster,
                false);
        }

        return new GatewayRouteMetadata("unknown", "unmatched", "unmatched", false);
    }

    private static IEnumerable<ConfiguredGatewayRoute> LoadBlockedRoutes(IConfiguration configuration)
    {
        foreach (var section in configuration.GetSection("GatewayRoutes:BlockedAliases").GetChildren())
        {
            var configuredPath = section["Path"]?.Trim();
            var configuredPrefix = section["PathPrefix"]?.Trim();
            var routeSurface = section["RouteSurface"]?.Trim();
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                yield return new ConfiguredGatewayRoute(
                    section.Key,
                    configuredPath,
                    routeSurface ?? section.Key,
                    "blocked",
                    IsPrefix: false);
            }

            if (!string.IsNullOrWhiteSpace(configuredPrefix))
            {
                yield return new ConfiguredGatewayRoute(
                    section.Key,
                    configuredPrefix,
                    routeSurface ?? section.Key,
                    "blocked",
                    IsPrefix: true);
            }
        }
    }

    private static IEnumerable<ConfiguredGatewayRoute> LoadProxyRoutes(IConfiguration configuration)
    {
        foreach (var section in configuration.GetSection("ReverseProxy:Routes").GetChildren())
        {
            var configuredPath = section["Match:Path"]?.Trim();
            if (string.IsNullOrWhiteSpace(configuredPath))
                continue;

            var routeSurface = ResolveRouteSurface(section) ?? section.Key;
            var upstreamCluster = section["ClusterId"]?.Trim();
            var routePath = ConvertYarpPath(configuredPath, out var isPrefix);
            yield return new ConfiguredGatewayRoute(
                section.Key,
                routePath,
                routeSurface,
                string.IsNullOrWhiteSpace(upstreamCluster) ? "unmatched" : upstreamCluster,
                isPrefix);
        }
    }

    private static string? ResolveRouteSurface(IConfigurationSection routeSection)
    {
        foreach (var transform in routeSection.GetSection("Transforms").GetChildren())
        {
            if (string.Equals(transform["RequestHeader"], RouteSurfaceHeader, StringComparison.OrdinalIgnoreCase))
                return transform["Set"]?.Trim();
        }

        return null;
    }

    private static string ConvertYarpPath(string configuredPath, out bool isPrefix)
    {
        var wildcardIndex = configuredPath.IndexOf("/{*", StringComparison.Ordinal);
        if (wildcardIndex >= 0)
        {
            isPrefix = true;
            return configuredPath[..wildcardIndex];
        }

        isPrefix = false;
        return configuredPath;
    }

    private static ConfiguredGatewayRoute? FindMatch(
        IReadOnlyList<ConfiguredGatewayRoute> routes,
        string requestPath)
    {
        foreach (var route in routes)
        {
            if (route.IsMatch(requestPath))
                return route;
        }

        return null;
    }

    private sealed record ConfiguredGatewayRoute(
        string RouteName,
        string Path,
        string RouteSurface,
        string UpstreamCluster,
        bool IsPrefix)
    {
        public bool IsMatch(string requestPath)
        {
            if (!IsPrefix)
                return string.Equals(requestPath, Path, StringComparison.OrdinalIgnoreCase);

            return string.Equals(requestPath, Path, StringComparison.OrdinalIgnoreCase)
                   || requestPath.StartsWith(
                       Path.EndsWith('/') ? Path : $"{Path}/",
                       StringComparison.OrdinalIgnoreCase);
        }
    }
}
