using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace IIoT.HttpApi.Infrastructure;

public sealed class HttpApiForwardedHeadersOptions
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; set; }

    public int ForwardLimit { get; set; } = 1;

    public string[] KnownProxies { get; set; } = [];

    public string[] KnownNetworks { get; set; } = [];

    public void ApplyTo(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.None;
        options.ForwardLimit = null;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        if (!Enabled)
        {
            return;
        }

        Validate();

        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = ForwardLimit;

        foreach (var proxy in KnownProxies)
        {
            options.KnownProxies.Add(ParseProxy(proxy));
        }

        foreach (var network in KnownNetworks)
        {
            options.KnownIPNetworks.Add(ParseNetwork(network));
        }
    }

    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        if (ForwardLimit <= 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:ForwardLimit must be greater than 0 when forwarded headers are enabled.");
        }

        if (KnownProxies.Length == 0 && KnownNetworks.Length == 0)
        {
            throw new InvalidOperationException(
                $"{SectionName} must define at least one trusted proxy or network when enabled.");
        }

        foreach (var proxy in KnownProxies)
        {
            _ = ParseProxy(proxy);
        }

        foreach (var network in KnownNetworks)
        {
            _ = ParseNetwork(network);
        }
    }

    private static IPAddress ParseProxy(string value)
    {
        if (!IPAddress.TryParse(value, out var address))
        {
            throw new InvalidOperationException(
                $"{SectionName}:KnownProxies contains an invalid IP address '{value}'.");
        }

        return address;
    }

    private static System.Net.IPNetwork ParseNetwork(string value)
    {
        var segments = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (segments.Length != 2
            || !IPAddress.TryParse(segments[0], out var prefix)
            || !int.TryParse(segments[1], out var prefixLength))
        {
            throw new InvalidOperationException(
                $"{SectionName}:KnownNetworks contains an invalid CIDR value '{value}'.");
        }

        var maxPrefixLength = prefix.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => 32,
            System.Net.Sockets.AddressFamily.InterNetworkV6 => 128,
            _ => throw new InvalidOperationException(
                $"{SectionName}:KnownNetworks contains an unsupported address family '{value}'.")
        };

        if (prefixLength < 0 || prefixLength > maxPrefixLength)
        {
            throw new InvalidOperationException(
                $"{SectionName}:KnownNetworks contains an out-of-range prefix length '{value}'.");
        }

        return new System.Net.IPNetwork(prefix, prefixLength);
    }
}
