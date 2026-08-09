using IIoT.HttpApi.Infrastructure;
using IIoT.Services.Contracts.Authorization;

namespace IIoT.CloudPlatform.HttpTests;

public sealed class HttpAiReadScopeAccessorTests
{
    [Fact]
    public void Scope_ShouldBeInvalidUntilLiveAuthorizationInitializesRequest()
    {
        var accessor = new HttpAiReadScopeAccessor();

        Assert.False(accessor.IsInitialized);
        Assert.Equal(AiReadScopeKind.Invalid, accessor.ScopeKind);
        Assert.Null(accessor.DelegatedUserId);
        Assert.Null(accessor.DelegatedDeviceIds);
        Assert.Equal("unverified-ai-delegation", accessor.Caller);
    }

    [Fact]
    public void Scope_ShouldKeepOrdinaryUserWithZeroDevicesAsEmptyDelegatedScope()
    {
        var delegatedUserId = Guid.NewGuid();
        var accessor = new HttpAiReadScopeAccessor();

        accessor.Initialize(new AiReadDelegatedAuthorization(
            delegatedUserId,
            IsAdministrator: false,
            AllowedDeviceIds: []));

        Assert.True(accessor.IsInitialized);
        Assert.Equal(AiReadScopeKind.Delegated, accessor.ScopeKind);
        Assert.Equal(delegatedUserId, accessor.DelegatedUserId);
        Assert.Empty(accessor.DelegatedDeviceIds!);
        Assert.Equal(delegatedUserId.ToString("D"), accessor.Caller);
    }

    [Fact]
    public void Scope_ShouldExposeOnlyLiveResolvedDeviceScope()
    {
        var delegatedUserId = Guid.NewGuid();
        var delegatedDeviceId = Guid.NewGuid();
        var accessor = new HttpAiReadScopeAccessor();

        accessor.Initialize(new AiReadDelegatedAuthorization(
            delegatedUserId,
            IsAdministrator: false,
            AllowedDeviceIds: [delegatedDeviceId]));

        Assert.Equal(AiReadScopeKind.Delegated, accessor.ScopeKind);
        Assert.Equal([delegatedDeviceId], accessor.DelegatedDeviceIds);
    }

    [Fact]
    public void Scope_ShouldBeGlobalOnlyForLiveResolvedAdmin()
    {
        var delegatedUserId = Guid.NewGuid();
        var accessor = new HttpAiReadScopeAccessor();

        accessor.Initialize(new AiReadDelegatedAuthorization(
            delegatedUserId,
            IsAdministrator: true,
            AllowedDeviceIds: null));

        Assert.Equal(AiReadScopeKind.Global, accessor.ScopeKind);
        Assert.Equal(delegatedUserId, accessor.DelegatedUserId);
        Assert.Null(accessor.DelegatedDeviceIds);
    }

    [Fact]
    public void Scope_ShouldRejectSecondInitializationWithinSameRequest()
    {
        var accessor = new HttpAiReadScopeAccessor();
        accessor.Initialize(new AiReadDelegatedAuthorization(
            Guid.NewGuid(),
            IsAdministrator: false,
            AllowedDeviceIds: []));

        Assert.Throws<InvalidOperationException>(() => accessor.Initialize(
            new AiReadDelegatedAuthorization(
                Guid.NewGuid(),
                IsAdministrator: false,
                AllowedDeviceIds: [])));
    }
}
