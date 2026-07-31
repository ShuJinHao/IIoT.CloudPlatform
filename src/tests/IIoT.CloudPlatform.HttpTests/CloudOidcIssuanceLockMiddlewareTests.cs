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
    public async Task TokenExchange_ShouldBufferResponseUntilProtectedOperationCommits()
    {
        var issuanceLock = new RecordingIssuanceLock();
        var context = new DefaultHttpContext();
        context.Request.Path = "/connect/token";
        var clientBody = new CommitCheckingStream(
            () => issuanceLock.IsCommitted);
        context.Response.Body = clientBody;
        var middleware = new CloudOidcIssuanceLockMiddleware(
            async endpointContext =>
            {
                await endpointContext.Response.WriteAsync("token-response");
                Assert.Equal(0, clientBody.Length);
            });

        await middleware.InvokeAsync(context, issuanceLock);

        Assert.True(issuanceLock.IsCommitted);
        Assert.Equal("token-response", clientBody.GetBody());
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

    [Fact]
    public async Task AuthorizationCapacityExceeded_ShouldReturn429WithoutReachingEndpoint()
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
        context.Response.Body = new MemoryStream();
        var issuanceLock = new RecordingIssuanceLock
        {
            RejectAuthorization = true
        };
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
        Assert.Equal(1, issuanceLock.AuthorizationAcquisitions);
        Assert.Equal(subjectId, issuanceLock.AuthorizationSubjectId);
        Assert.False(issuanceLock.IsHeld);
    }

    private sealed class RecordingIssuanceLock : IHumanSessionIssuanceLock
    {
        public int AuthorizationAcquisitions { get; private set; }

        public int TokenExchangeAcquisitions { get; private set; }

        public Guid? AuthorizationSubjectId { get; private set; }

        public bool IsHeld { get; private set; }

        public bool IsCommitted { get; private set; }

        public bool RejectAuthorization { get; init; }

        public bool RejectTokenExchange { get; init; }

        public Task<bool> TryExecuteAuthorizationAsync(
            Guid subjectId,
            Func<Task> operation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AuthorizationAcquisitions++;
            AuthorizationSubjectId = subjectId;
            return ExecuteAsync(RejectAuthorization, operation);
        }

        public Task<bool> TryExecuteTokenExchangeAsync(
            Func<Task> operation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TokenExchangeAcquisitions++;
            return ExecuteAsync(RejectTokenExchange, operation);
        }

        private async Task<bool> ExecuteAsync(
            bool reject,
            Func<Task> operation)
        {
            if (reject)
            {
                return false;
            }

            Assert.False(IsHeld);
            IsHeld = true;
            IsCommitted = false;
            try
            {
                await operation();
                IsCommitted = true;
                return true;
            }
            finally
            {
                IsHeld = false;
            }
        }
    }

    private sealed class CommitCheckingStream(Func<bool> isCommitted)
        : MemoryStream
    {
        public override void Write(
            byte[] buffer,
            int offset,
            int count)
        {
            Assert.True(isCommitted());
            base.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Assert.True(isCommitted());
            await base.WriteAsync(buffer, cancellationToken);
        }

        public string GetBody()
        {
            Position = 0;
            using var reader = new StreamReader(
                this,
                leaveOpen: true);
            return reader.ReadToEnd();
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
