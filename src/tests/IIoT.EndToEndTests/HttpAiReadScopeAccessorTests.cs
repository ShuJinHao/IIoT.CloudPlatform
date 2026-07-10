using System.Security.Claims;
using IIoT.HttpApi.Infrastructure;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Http;

namespace IIoT.EndToEndTests;

public sealed class HttpAiReadScopeAccessorTests
{
    [Fact]
    public void Scope_ShouldBeGlobalOnlyWhenDelegationClaimsAreAbsent()
    {
        var accessor = CreateAccessor([]);

        Assert.Equal(AiReadScopeKind.Global, accessor.ScopeKind);
        Assert.Null(accessor.DelegatedUserId);
        Assert.Null(accessor.DelegatedDeviceIds);
    }

    [Fact]
    public void Scope_ShouldKeepValidDelegatedUserWithEmptyDeviceScope()
    {
        var delegatedUserId = Guid.NewGuid();
        var accessor = CreateAccessor(
        [
            new Claim(IIoTClaimTypes.DelegatedUserId, delegatedUserId.ToString())
        ]);

        Assert.Equal(AiReadScopeKind.Delegated, accessor.ScopeKind);
        Assert.Equal(delegatedUserId, accessor.DelegatedUserId);
        Assert.Empty(accessor.DelegatedDeviceIds!);
    }

    [Fact]
    public void Scope_ShouldDeduplicateValidDelegatedDeviceClaims()
    {
        var delegatedUserId = Guid.NewGuid();
        var delegatedDeviceId = Guid.NewGuid();
        var accessor = CreateAccessor(
        [
            new Claim(IIoTClaimTypes.DelegatedUserId, delegatedUserId.ToString()),
            new Claim(IIoTClaimTypes.DelegatedDeviceId, delegatedDeviceId.ToString()),
            new Claim(IIoTClaimTypes.DelegatedDeviceId, delegatedDeviceId.ToString())
        ]);

        Assert.Equal(AiReadScopeKind.Delegated, accessor.ScopeKind);
        Assert.Equal([delegatedDeviceId], accessor.DelegatedDeviceIds);
    }

    [Theory]
    [InlineData(IIoTClaimTypes.DelegatedUserId, "not-a-guid")]
    [InlineData(IIoTClaimTypes.DelegatedUserId, "00000000-0000-0000-0000-000000000000")]
    [InlineData(IIoTClaimTypes.DelegatedDeviceId, "not-a-guid")]
    [InlineData(IIoTClaimTypes.DelegatedDeviceId, "00000000-0000-0000-0000-000000000000")]
    public void Scope_ShouldFailClosedForInvalidDelegationClaim(string claimType, string claimValue)
    {
        var claims = new List<Claim>();
        if (claimType == IIoTClaimTypes.DelegatedDeviceId)
        {
            claims.Add(new Claim(IIoTClaimTypes.DelegatedUserId, Guid.NewGuid().ToString()));
        }

        claims.Add(new Claim(claimType, claimValue));
        var accessor = CreateAccessor(claims);

        Assert.Equal(AiReadScopeKind.Invalid, accessor.ScopeKind);
        Assert.Null(accessor.DelegatedUserId);
        Assert.Empty(accessor.DelegatedDeviceIds!);
    }

    [Fact]
    public void Scope_ShouldFailClosedWhenDeviceClaimHasNoDelegatedUser()
    {
        var accessor = CreateAccessor(
        [
            new Claim(IIoTClaimTypes.DelegatedDeviceId, Guid.NewGuid().ToString())
        ]);

        Assert.Equal(AiReadScopeKind.Invalid, accessor.ScopeKind);
        Assert.Empty(accessor.DelegatedDeviceIds!);
    }

    [Fact]
    public void Scope_ShouldFailClosedWhenValidAndInvalidDeviceClaimsAreMixed()
    {
        var accessor = CreateAccessor(
        [
            new Claim(IIoTClaimTypes.DelegatedUserId, Guid.NewGuid().ToString()),
            new Claim(IIoTClaimTypes.DelegatedDeviceId, Guid.NewGuid().ToString()),
            new Claim(IIoTClaimTypes.DelegatedDeviceId, "invalid-device")
        ]);

        Assert.Equal(AiReadScopeKind.Invalid, accessor.ScopeKind);
        Assert.Empty(accessor.DelegatedDeviceIds!);
    }

    private static HttpAiReadScopeAccessor CreateAccessor(IEnumerable<Claim> claims)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
        return new HttpAiReadScopeAccessor(new HttpContextAccessor { HttpContext = httpContext });
    }
}
