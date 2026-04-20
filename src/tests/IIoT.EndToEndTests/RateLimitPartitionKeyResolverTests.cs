using System.Net;
using System.Security.Claims;
using FluentAssertions;
using IIoT.HttpApi.Infrastructure;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Http;

namespace IIoT.EndToEndTests;

public sealed class RateLimitPartitionKeyResolverTests
{
    [Fact]
    public void ResolveClientPartitionKey_ShouldUseForwardedRemoteIpWhenPresent()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");

        var key = RateLimitPartitionKeyResolver.ResolveClientPartitionKey(context, "anonymous");

        key.Should().Be("203.0.113.10");
    }

    [Fact]
    public void ResolveClientPartitionKey_ShouldFallbackWhenRemoteIpMissing()
    {
        var context = new DefaultHttpContext();

        var key = RateLimitPartitionKeyResolver.ResolveClientPartitionKey(context, "login-anonymous");

        key.Should().Be("login-anonymous");
    }

    [Fact]
    public void ResolveEdgeUploadPartitionKey_ShouldPreferDeviceClaim()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(IIoTClaimTypes.DeviceId, Guid.NewGuid().ToString())
            ], "test"))
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.24");

        var key = RateLimitPartitionKeyResolver.ResolveEdgeUploadPartitionKey(context);

        key.Should().NotBe("198.51.100.24");
        key.Should().NotBe("edge-anonymous");
    }

    [Fact]
    public void HttpApiForwardedHeadersOptions_ShouldRejectEnabledConfigurationWithoutTrustedSources()
    {
        var options = new HttpApiForwardedHeadersOptions
        {
            Enabled = true,
            ForwardLimit = 1,
            KnownProxies = [],
            KnownNetworks = []
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must define at least one trusted proxy or network*");
    }
}
