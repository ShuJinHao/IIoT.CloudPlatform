using System.Security.Claims;
using IIoT.HttpApi.Infrastructure.Oidc;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace IIoT.CloudPlatform.HttpTests;

public sealed class CloudOidcSessionStatusTests
{
    [Fact]
    public async Task CloudOidcSessionService_ShouldWriteCurrentStatusVersionIntoCookiePrincipal()
    {
        var userId = Guid.NewGuid();
        var authentication = new CapturingAuthenticationService();
        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(authentication)
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        var service = new CloudOidcSessionService(
            new StaticProfileService(new CloudOidcUserProfile(
                userId,
                "E-OIDC-COOKIE",
                "Cookie User",
                AccountEnabled: true,
                EmployeeActive: true,
                StatusVersion: "status-cookie-current")),
            new RecordingAuditTrailService(),
            Options.Create(new OidcProviderOptions { SessionIdleMinutes = 30 }));

        await service.SignInAsync(context, "E-OIDC-COOKIE");

        Assert.Equal(CloudOidcDefaults.SessionScheme, authentication.SignInScheme);
        Assert.NotNull(authentication.SignInPrincipal);
        Assert.Equal(
            "status-cookie-current",
            authentication.SignInPrincipal.FindFirstValue(
                IIoTClaimTypes.IdentityStatusVersion));
    }

    [Fact]
    public async Task CloudOidcSessionService_ShouldFailClosedWhenStatusVersionIsMissing()
    {
        var userId = Guid.NewGuid();
        var authentication = new CapturingAuthenticationService();
        var services = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(authentication)
            .BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        var service = new CloudOidcSessionService(
            new StaticProfileService(new CloudOidcUserProfile(
                userId,
                "E-OIDC-LEGACY",
                "Legacy User",
                AccountEnabled: true,
                EmployeeActive: true)),
            new RecordingAuditTrailService(),
            Options.Create(new OidcProviderOptions { SessionIdleMinutes = 30 }));

        await service.SignInAsync(context, "E-OIDC-LEGACY");

        Assert.Null(authentication.SignInPrincipal);
    }

    private sealed class StaticProfileService(CloudOidcUserProfile profile)
        : ICloudOidcUserProfileService
    {
        public Task<CloudOidcUserProfile?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(profile.UserId == userId ? profile : null);

        public Task<CloudOidcUserProfile?> GetByEmployeeNoAsync(
            string employeeNo,
            CancellationToken cancellationToken = default)
            => Task.FromResult(
                string.Equals(profile.EmployeeNo, employeeNo, StringComparison.Ordinal)
                    ? profile
                    : null);
    }

    private sealed class RecordingAuditTrailService : IAuditTrailService
    {
        public Task TryWriteAsync(
            AuditTrailEntry entry,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TryWriteConfirmedAsync(
            AuditTrailEntry entry,
            CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class CapturingAuthenticationService : IAuthenticationService
    {
        public string? SignInScheme { get; private set; }

        public ClaimsPrincipal? SignInPrincipal { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(
            HttpContext context,
            string? scheme)
            => Task.FromResult(AuthenticateResult.NoResult());

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
            ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            SignInScheme = scheme;
            SignInPrincipal = principal;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
            => Task.CompletedTask;
    }
}
