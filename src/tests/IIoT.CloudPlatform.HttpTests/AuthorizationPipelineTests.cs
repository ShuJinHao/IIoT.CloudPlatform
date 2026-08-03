using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using IIoT.HttpApi;
using IIoT.HttpApi.Controllers;
using IIoT.HttpApi.Controllers.Oidc;
using IIoT.HttpApi.Infrastructure;
using IIoT.HttpApi.Infrastructure.Authentication;
using IIoT.Infrastructure.Authentication;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.DependencyInjection;
using IIoT.Services.Contracts.Identity;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace IIoT.CloudPlatform.HttpTests;

public sealed class AuthorizationPipelineTests
{
    [Fact]
    public void AdminOnlyGuard_ShouldRunBeforePermissionAuthorizationAndDistributedLock()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddConfiguredMediatR(
            configuration,
            DependencyInjection.ConfigureApplicationMediatR);

        var behaviorTypes = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();
        var adminOnlyIndex = behaviorTypes.IndexOf(typeof(AdminOnlyBehavior<,>));
        var authorizationIndex = behaviorTypes.IndexOf(typeof(AuthorizationBehavior<,>));
        var distributedLockIndex = behaviorTypes.IndexOf(typeof(DistributedLockBehavior<,>));

        Assert.True(adminOnlyIndex >= 0);
        Assert.True(authorizationIndex >= 0);
        Assert.True(distributedLockIndex >= 0);
        Assert.True(adminOnlyIndex < authorizationIndex);
        Assert.True(authorizationIndex < distributedLockIndex);
    }

    [Fact]
    public async Task HumanUserPolicy_ShouldAllowAuthenticatedHumanActor()
    {
        await using var serviceProvider = CreateAuthorizationServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
        var principal = CreatePrincipal(IIoTClaimTypes.HumanActor);

        var result = await authorizationService.AuthorizeAsync(
            principal,
            resource: null,
            HttpApiPolicies.RequireHumanUserToken);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(IIoTClaimTypes.EdgeDeviceActor)]
    [InlineData(IIoTClaimTypes.AiServiceActor)]
    [InlineData(IIoTClaimTypes.EdgeReleasePublisherActor)]
    public async Task HumanUserPolicy_ShouldRejectAuthenticatedMachineActors(string actorType)
    {
        await using var serviceProvider = CreateAuthorizationServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
        var principal = CreatePrincipal(actorType);

        var result = await authorizationService.AuthorizeAsync(
            principal,
            resource: null,
            HttpApiPolicies.RequireHumanUserToken);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task HumanUserPolicy_ShouldRejectUnauthenticatedCaller()
    {
        await using var serviceProvider = CreateAuthorizationServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();

        var result = await authorizationService.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null,
            HttpApiPolicies.RequireHumanUserToken);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void HumanIdentitySessionEndpoint_ShouldRequireHumanJwtPolicyAndGetOnly()
    {
        var method = typeof(HumanIdentityController).GetMethod(
            nameof(HumanIdentityController.GetSession),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        var authorize = Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(HttpApiPolicies.RequireHumanUserToken, authorize.Policy);
        Assert.Empty(method.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Equal(
            "session",
            Assert.Single(method.GetCustomAttributes<HttpGetAttribute>()).Template);
    }

    [Fact]
    public void JwtTokenGenerator_ShouldSignCurrentHumanIdentityStatusVersion()
    {
        var generator = new JwtTokenGenerator(Options.Create(new JwtSettings
        {
            Secret = new string('s', JwtSettings.MinimumSecretLength),
            Issuer = "iiot-test",
            Audience = "iiot-test-client",
            ExpiryMinutes = 10
        }));

        var result = generator.GenerateHumanToken(
            Guid.NewGuid(),
            "E-JWT-001",
            [],
            [],
            "status-current");
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.Equal(
            "status-current",
            Assert.Single(token.Claims, claim =>
                claim.Type == IIoTClaimTypes.IdentityStatusVersion).Value);
    }

    [Fact]
    public async Task HumanJwtStatusValidator_ShouldAllowOnlyExactLiveVersion()
    {
        var userId = Guid.NewGuid();
        var profileService = new StubCloudOidcUserProfileService
        {
            Profile = CreateProfile(userId, "status-current")
        };
        var validator = new HumanJwtStatusValidator(profileService);

        var valid = await validator.IsCurrentAsync(
            CreateHumanPrincipal(userId, "status-current"),
            CancellationToken.None);
        var forged = await validator.IsCurrentAsync(
            CreateHumanPrincipal(userId, "status-forged"),
            CancellationToken.None);
        var missing = await validator.IsCurrentAsync(
            CreateHumanPrincipal(userId, statusVersion: null),
            CancellationToken.None);

        Assert.True(valid);
        Assert.False(forged);
        Assert.False(missing);
        Assert.Equal(2, profileService.GetByUserIdCalls);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task HumanJwtStatusValidator_ShouldRejectUnavailableAccountOrEmployee(
        bool accountEnabled,
        bool employeeActive)
    {
        var userId = Guid.NewGuid();
        var profileService = new StubCloudOidcUserProfileService
        {
            Profile = CreateProfile(userId, "status-current") with
            {
                AccountEnabled = accountEnabled,
                EmployeeActive = employeeActive
            }
        };

        var result = await new HumanJwtStatusValidator(profileService).IsCurrentAsync(
            CreateHumanPrincipal(userId, "status-current"),
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HumanJwtStatusValidator_ShouldFailClosedWhenStatusServiceThrows()
    {
        var userId = Guid.NewGuid();
        var profileService = new StubCloudOidcUserProfileService
        {
            ExceptionToThrow = new InvalidOperationException("status store unavailable")
        };

        var result = await new HumanJwtStatusValidator(profileService).IsCurrentAsync(
            CreateHumanPrincipal(userId, "status-current"),
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task HumanJwtStatusValidator_ShouldRejectMissingIdentityAndMalformedSubject()
    {
        var userId = Guid.NewGuid();
        var profileService = new StubCloudOidcUserProfileService();
        var validator = new HumanJwtStatusValidator(profileService);

        var missingIdentity = await validator.IsCurrentAsync(
            CreateHumanPrincipal(userId, "status-current"),
            CancellationToken.None);
        var malformedSubject = await validator.IsCurrentAsync(
            new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "not-a-guid"),
                new Claim(IIoTClaimTypes.ActorType, IIoTClaimTypes.HumanActor),
                new Claim(IIoTClaimTypes.IdentityStatusVersion, "status-current")
            ], "test")),
            CancellationToken.None);

        Assert.False(missingIdentity);
        Assert.False(malformedSubject);
        Assert.Equal(1, profileService.GetByUserIdCalls);
    }

    [Theory]
    [InlineData(IIoTClaimTypes.EdgeDeviceActor)]
    [InlineData(IIoTClaimTypes.AiServiceActor)]
    [InlineData(IIoTClaimTypes.EdgeReleasePublisherActor)]
    public async Task HumanJwtStatusValidator_ShouldLeaveMachineIdentitySemanticsUnchanged(
        string actorType)
    {
        var profileService = new StubCloudOidcUserProfileService
        {
            ExceptionToThrow = new InvalidOperationException("must not be queried")
        };
        var principal = CreatePrincipal(actorType);

        var result = await new HumanJwtStatusValidator(profileService).IsCurrentAsync(
            principal,
            CancellationToken.None);

        Assert.True(result);
        Assert.Equal(0, profileService.GetByUserIdCalls);
    }

    [Fact]
    public void CloudOidcStatusVersion_ShouldRejectOldCookieCodeAndUserInfoPrincipalAfterReactivation()
    {
        var userId = Guid.NewGuid();
        var oldPrincipal = CreateHumanPrincipal(userId, "status-before-deactivation");
        var reactivatedProfile = CreateProfile(userId, "status-after-reactivation");

        Assert.False(CloudOidcController.HasCurrentStatusVersion(
            oldPrincipal,
            reactivatedProfile));
        Assert.True(CloudOidcController.HasCurrentStatusVersion(
            CreateHumanPrincipal(userId, "status-after-reactivation"),
            reactivatedProfile));
    }

    private static ServiceProvider CreateAuthorizationServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var authenticatedUserPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        DependencyInjection.ConfigureHttpAuthorization(
            services.AddAuthorizationBuilder(),
            authenticatedUserPolicy);

        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal CreatePrincipal(string actorType)
    {
        var identity = new ClaimsIdentity(
            [new Claim(IIoTClaimTypes.ActorType, actorType)],
            authenticationType: "test");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateHumanPrincipal(
        Guid userId,
        string? statusVersion)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(IIoTClaimTypes.ActorType, IIoTClaimTypes.HumanActor)
        };
        if (statusVersion is not null)
        {
            claims.Add(new Claim(IIoTClaimTypes.IdentityStatusVersion, statusVersion));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static CloudOidcUserProfile CreateProfile(
        Guid userId,
        string statusVersion)
        => new(
            userId,
            "E-HTTP-001",
            "HTTP User",
            AccountEnabled: true,
            EmployeeActive: true,
            StatusVersion: statusVersion);

    private sealed class StubCloudOidcUserProfileService : ICloudOidcUserProfileService
    {
        public CloudOidcUserProfile? Profile { get; init; }

        public Exception? ExceptionToThrow { get; init; }

        public int GetByUserIdCalls { get; private set; }

        public Task<CloudOidcUserProfile?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            GetByUserIdCalls++;
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(Profile?.UserId == userId ? Profile : null);
        }

        public Task<CloudOidcUserProfile?> GetByEmployeeNoAsync(
            string employeeNo,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
