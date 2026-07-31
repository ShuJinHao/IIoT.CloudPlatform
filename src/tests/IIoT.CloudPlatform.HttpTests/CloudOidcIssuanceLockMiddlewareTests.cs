using System.Security.Claims;
using IIoT.HttpApi.Infrastructure.Oidc;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace IIoT.CloudPlatform.HttpTests;

public sealed class CloudOidcIssuanceLockMiddlewareTests
{
    [Fact]
    public async Task TokenExchange_ShouldHoldGlobalIssuanceLockThroughEndpoint()
    {
        var issuanceLock = new RecordingIssuanceLock();
        var context = new DefaultHttpContext();
        context.Request.Path = "/connect/token";
        var reachedEndpoint = false;
        var middleware = new CloudOidcIssuanceLockMiddleware(_ =>
        {
            Assert.True(issuanceLock.IsHeld);
            reachedEndpoint = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, issuanceLock);

        Assert.True(reachedEndpoint);
        Assert.Equal(1, issuanceLock.TokenExchangeAcquisitions);
        Assert.Equal(0, issuanceLock.AuthorizationAcquisitions);
        Assert.False(issuanceLock.IsHeld);
    }

    [Fact]
    public async Task TokenExchangeCapacityExceeded_ShouldReturn429WithoutReachingEndpoint()
    {
        var issuanceLock = new RecordingIssuanceLock
        {
            RejectTokenExchange = true
        };
        var context = new DefaultHttpContext();
        context.Request.Path = "/connect/token";
        context.Response.Body = new MemoryStream();
        var reachedEndpoint = false;
        var middleware = new CloudOidcIssuanceLockMiddleware(_ =>
        {
            reachedEndpoint = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, issuanceLock);

        Assert.False(reachedEndpoint);
        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            context.Response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            context.Response.ContentType);
        Assert.Equal("1", context.Response.Headers.RetryAfter);
        Assert.True(context.Response.Body.Length > 0);
        Assert.Equal(1, issuanceLock.TokenExchangeAcquisitions);
        Assert.False(issuanceLock.IsHeld);
    }

    [Fact]
    public async Task Authorization_ShouldHoldSubjectLockThroughEndpoint()
    {
        var subjectId = Guid.NewGuid();
        var authentication = new StaticAuthenticationService(
            new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, subjectId.ToString())],
                    CloudOidcDefaults.SessionScheme)));
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton<IAuthenticationService>(authentication)
                .BuildServiceProvider()
        };
        context.Request.Path = "/connect/authorize";
        var issuanceLock = new RecordingIssuanceLock();
        var middleware = new CloudOidcIssuanceLockMiddleware(_ =>
        {
            Assert.True(issuanceLock.IsHeld);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, issuanceLock);

        Assert.Equal(1, issuanceLock.AuthorizationAcquisitions);
        Assert.Equal(subjectId, issuanceLock.AuthorizationSubjectId);
        Assert.Equal(0, issuanceLock.TokenExchangeAcquisitions);
        Assert.False(issuanceLock.IsHeld);
    }

    private sealed class RecordingIssuanceLock : IHumanSessionIssuanceLock
    {
        public int AuthorizationAcquisitions { get; private set; }

        public int TokenExchangeAcquisitions { get; private set; }

        public Guid? AuthorizationSubjectId { get; private set; }

        public bool IsHeld { get; private set; }

        public bool RejectTokenExchange { get; init; }

        public ValueTask<IAsyncDisposable> AcquireAuthorizationAsync(
            Guid subjectId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AuthorizationAcquisitions++;
            AuthorizationSubjectId = subjectId;
            return ValueTask.FromResult<IAsyncDisposable>(Acquire());
        }

        public ValueTask<IAsyncDisposable?> TryAcquireTokenExchangeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TokenExchangeAcquisitions++;
            return ValueTask.FromResult<IAsyncDisposable?>(
                RejectTokenExchange ? null : Acquire());
        }

        private IAsyncDisposable Acquire()
        {
            Assert.False(IsHeld);
            IsHeld = true;
            return new Lease(this);
        }

        private sealed class Lease(RecordingIssuanceLock owner)
            : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.IsHeld = false;
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class StaticAuthenticationService(ClaimsPrincipal principal)
        : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(
            HttpContext context,
            string? scheme)
            => Task.FromResult(
                string.Equals(
                    scheme,
                    CloudOidcDefaults.SessionScheme,
                    StringComparison.Ordinal)
                    ? AuthenticateResult.Success(
                        new AuthenticationTicket(
                            principal,
                            CloudOidcDefaults.SessionScheme))
                    : AuthenticateResult.NoResult());

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal signInPrincipal,
            AuthenticationProperties? properties)
            => Task.CompletedTask;

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
            => Task.CompletedTask;
    }
}
