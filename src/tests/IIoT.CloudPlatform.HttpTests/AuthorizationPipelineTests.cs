using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.HttpApi;
using IIoT.HttpApi.Controllers;
using IIoT.HttpApi.Controllers.Oidc;
using IIoT.HttpApi.Infrastructure;
using IIoT.HttpApi.Infrastructure.Authentication;
using IIoT.Infrastructure.Authentication;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.DependencyInjection;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;

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
    [InlineData(IIoTClaimTypes.AiDelegatedUserActor)]
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
    public async Task AiReadAndIdentityStatusPolicies_ShouldKeepActorsSeparated()
    {
        await using var serviceProvider = CreateAuthorizationServiceProvider();
        var authorizationService = serviceProvider.GetRequiredService<IAuthorizationService>();
        var delegated = CreateAiPrincipal(IIoTClaimTypes.AiDelegatedUserActor);
        var identityStatus = CreateAiPrincipal(IIoTClaimTypes.AiIdentityStatusActor);

        Assert.True((await authorizationService.AuthorizeAsync(
            delegated,
            resource: null,
            HttpApiPolicies.RequireAiReadDelegation)).Succeeded);
        Assert.False((await authorizationService.AuthorizeAsync(
            identityStatus,
            resource: null,
            HttpApiPolicies.RequireAiReadDelegation)).Succeeded);
        Assert.True((await authorizationService.AuthorizeAsync(
            identityStatus,
            resource: null,
            HttpApiPolicies.RequireAiIdentityStatusToken)).Succeeded);
        Assert.False((await authorizationService.AuthorizeAsync(
            delegated,
            resource: null,
            HttpApiPolicies.RequireAiIdentityStatusToken)).Succeeded);
    }

    [Fact]
    public void IdentityStatusTokenShape_ShouldRequireFixedClaimsAndFiveMinuteMaximum()
    {
        const long issuedAt = 1_800_000_000;
        var validClaims = CreateIdentityStatusClaims(
            issuedAt,
            issuedAt,
            issuedAt + (AiIdentityStatusTokenDefaults.LifetimeMinutes * 60));

        Assert.True(AiIdentityStatusTokenValidator.IsValidRawToken(
            CreateUnsignedToken(validClaims)));

        Assert.False(AiIdentityStatusTokenValidator.IsValidRawToken(
            CreateUnsignedToken(CreateIdentityStatusClaims(
                issuedAt,
                issuedAt,
                issuedAt + (AiIdentityStatusTokenDefaults.LifetimeMinutes * 60) + 1))));
        Assert.False(AiIdentityStatusTokenValidator.IsValidRawToken(
            CreateUnsignedToken(CreateIdentityStatusClaims(
                issuedAt,
                issuedAt + 1,
                issuedAt + (AiIdentityStatusTokenDefaults.LifetimeMinutes * 60)))));

        foreach (var claimType in new[]
                 {
                     JwtRegisteredClaimNames.Iat,
                     JwtRegisteredClaimNames.Nbf,
                     JwtRegisteredClaimNames.Exp
                 })
        {
            Assert.False(AiIdentityStatusTokenValidator.IsValidRawToken(
                CreateUnsignedToken(validClaims.Where(claim =>
                    !string.Equals(claim.Name, claimType, StringComparison.Ordinal)))));
            Assert.False(AiIdentityStatusTokenValidator.IsValidRawToken(
                CreateUnsignedToken(validClaims.Concat(
                    [validClaims.Single(claim => claim.Name == claimType)]))));
        }
    }

    [Fact]
    public void IdentityStatusTokenShape_ShouldRejectWrongSystemIdentityAndDelegatedActor()
    {
        const long issuedAt = 1_800_000_000;
        var validClaims = CreateIdentityStatusClaims(
            issuedAt,
            issuedAt,
            issuedAt + (AiIdentityStatusTokenDefaults.LifetimeMinutes * 60));

        foreach (var replacement in new[]
                 {
                     (JwtRegisteredClaimNames.Iss, "\"other-issuer\""),
                     (JwtRegisteredClaimNames.Aud, "\"iiot-cloud-ai-read\""),
                     (JwtRegisteredClaimNames.Sub, "\"other-system\""),
                     (AiIdentityStatusTokenDefaults.ActorClaimType, "\"ai-delegated-user\"")
                 })
        {
            var invalidClaims = validClaims.Select(claim =>
                string.Equals(claim.Name, replacement.Item1, StringComparison.Ordinal)
                    ? (claim.Name, replacement.Item2)
                    : claim);
            Assert.False(AiIdentityStatusTokenValidator.IsValidRawToken(
                CreateUnsignedToken(invalidClaims)));
        }
    }

    [Fact]
    public void IdentityStatusTokenOptions_ShouldKeepIssuerAudienceAndSecretFixed()
    {
        var valid = new AiIdentityStatusTokenOptions
        {
            Enabled = true,
            SigningSecret = new string('s', 32)
        };
        valid.Validate();

        Assert.Throws<InvalidOperationException>(() => new AiIdentityStatusTokenOptions
        {
            Enabled = true,
            Issuer = "other-issuer",
            SigningSecret = new string('s', 32)
        }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AiIdentityStatusTokenOptions
        {
            Enabled = true,
            Audience = "other-audience",
            SigningSecret = new string('s', 32)
        }.Validate());
        Assert.Throws<InvalidOperationException>(() => new AiIdentityStatusTokenOptions
        {
            Enabled = true,
            SigningSecret = new string('s', 31)
        }.Validate());
    }

    [Fact]
    public void AiReadAndIdentityStatusControllers_ShouldRequireDifferentPolicies()
    {
        var aiReadAuthorize = Assert.Single(
            typeof(AiReadController).GetCustomAttributes<AuthorizeAttribute>());
        var identityAuthorize = Assert.Single(
            typeof(AiIdentityController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(HttpApiPolicies.RequireAiReadDelegation, aiReadAuthorize.Policy);
        Assert.Equal(HttpApiPolicies.RequireAiIdentityStatusToken, identityAuthorize.Policy);
        Assert.NotEqual(aiReadAuthorize.Policy, identityAuthorize.Policy);
    }

    private static IReadOnlyList<(string Name, string JsonValue)> CreateIdentityStatusClaims(
        long issuedAt,
        long notBefore,
        long expiresAt)
    {
        return
        [
            (JwtRegisteredClaimNames.Iss, $"\"{AiIdentityStatusTokenDefaults.DefaultIssuer}\""),
            (JwtRegisteredClaimNames.Aud, $"\"{AiIdentityStatusTokenDefaults.DefaultAudience}\""),
            (JwtRegisteredClaimNames.Sub, $"\"{AiIdentityStatusTokenDefaults.Subject}\""),
            (AiIdentityStatusTokenDefaults.ActorClaimType, $"\"{AiIdentityStatusTokenDefaults.Actor}\""),
            (JwtRegisteredClaimNames.Iat, issuedAt.ToString(CultureInfo.InvariantCulture)),
            (JwtRegisteredClaimNames.Nbf, notBefore.ToString(CultureInfo.InvariantCulture)),
            (JwtRegisteredClaimNames.Exp, expiresAt.ToString(CultureInfo.InvariantCulture))
        ];
    }

    private static string CreateUnsignedToken(
        IEnumerable<(string Name, string JsonValue)> claims)
    {
        var header = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}"));
        var payloadJson = "{" + string.Join(
            ",",
            claims.Select(claim => $"\"{claim.Name}\":{claim.JsonValue}")) + "}";
        var payload = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson));
        return $"{header}.{payload}.c2ln";
    }

    [Fact]
    public async Task DelegatedAuthorization_ShouldRecheckPermissionAndDeviceScopeOnEveryRequest()
    {
        var userId = Guid.NewGuid();
        var firstDeviceId = Guid.NewGuid();
        var profileService = new StubCloudOidcUserProfileService
        {
            Profile = CreateProfile(userId, "status-current")
        };
        var accountStore = new StubIdentityAccountStore([SystemRoles.ProductionViewer]);
        var permissionProvider = new StubPermissionProvider(["AiRead.Device"]);
        var devicePermissionService = new StubDevicePermissionService([firstDeviceId]);
        var service = new DelegatedAiReadAuthorizationService(
            profileService,
            accountStore,
            permissionProvider,
            devicePermissionService);

        var initial = await service.AuthorizeAsync(
            userId,
            "status-current",
            ["AiRead.Device"]);
        Assert.NotNull(initial);
        Assert.False(initial!.IsAdministrator);
        Assert.Equal([firstDeviceId], initial.AllowedDeviceIds);

        devicePermissionService.DeviceIds = [];
        var afterDeviceRevocation = await service.AuthorizeAsync(
            userId,
            "status-current",
            ["AiRead.Device"]);
        Assert.NotNull(afterDeviceRevocation);
        Assert.NotNull(afterDeviceRevocation!.AllowedDeviceIds);
        Assert.Empty(afterDeviceRevocation.AllowedDeviceIds!);

        permissionProvider.Permissions = [];
        Assert.Null(await service.AuthorizeAsync(
            userId,
            "status-current",
            ["AiRead.Device"]));
        Assert.Equal(3, profileService.GetByUserIdCalls);
        Assert.Equal(3, accountStore.GetRolesCalls);
        Assert.Equal(3, permissionProvider.GetPermissionsCalls);
        Assert.Equal(2, devicePermissionService.GetDeviceCalls);
    }

    [Theory]
    [InlineData(false, true, "status-current")]
    [InlineData(true, false, "status-current")]
    [InlineData(true, true, "status-revoked")]
    public async Task DelegatedAuthorization_ShouldRejectLiveIdentityRevocation(
        bool accountEnabled,
        bool employeeActive,
        string liveStatusVersion)
    {
        var userId = Guid.NewGuid();
        var profileService = new StubCloudOidcUserProfileService
        {
            Profile = CreateProfile(userId, liveStatusVersion) with
            {
                AccountEnabled = accountEnabled,
                EmployeeActive = employeeActive
            }
        };
        var accountStore = new StubIdentityAccountStore([SystemRoles.ProductionViewer]);
        var permissionProvider = new StubPermissionProvider(["AiRead.Device"]);
        var devicePermissionService = new StubDevicePermissionService([Guid.NewGuid()]);
        var service = new DelegatedAiReadAuthorizationService(
            profileService,
            accountStore,
            permissionProvider,
            devicePermissionService);

        Assert.Null(await service.AuthorizeAsync(
            userId,
            "status-current",
            ["AiRead.Device"]));
        Assert.Equal(0, accountStore.GetRolesCalls);
        Assert.Equal(0, permissionProvider.GetPermissionsCalls);
        Assert.Equal(0, devicePermissionService.GetDeviceCalls);
    }

    [Fact]
    public async Task DelegatedAuthorization_ShouldGrantGlobalScopeOnlyToCurrentAdmin()
    {
        var userId = Guid.NewGuid();
        var profileService = new StubCloudOidcUserProfileService
        {
            Profile = CreateProfile(userId, "status-current")
        };
        var accountStore = new StubIdentityAccountStore([SystemRoles.Admin]);
        var permissionProvider = new StubPermissionProvider([AiReadPermissions.Device]);
        var devicePermissionService = new StubDevicePermissionService([]);
        var service = new DelegatedAiReadAuthorizationService(
            profileService,
            accountStore,
            permissionProvider,
            devicePermissionService);

        var result = await service.AuthorizeAsync(
            userId,
            "status-current",
            ["AiRead.Device"]);

        Assert.NotNull(result);
        Assert.True(result!.IsAdministrator);
        Assert.Null(result.AllowedDeviceIds);
        Assert.Equal(1, permissionProvider.GetPermissionsCalls);
        Assert.Equal(0, devicePermissionService.GetDeviceCalls);
    }

    [Fact]
    public async Task DelegatedAuthorization_ShouldRejectAdminWithoutCurrentAiReadPermission()
    {
        var userId = Guid.NewGuid();
        var permissionProvider = new StubPermissionProvider([]);
        var service = new DelegatedAiReadAuthorizationService(
            new StubCloudOidcUserProfileService
            {
                Profile = CreateProfile(userId, "status-current")
            },
            new StubIdentityAccountStore([SystemRoles.Admin]),
            permissionProvider,
            new StubDevicePermissionService([]));

        var result = await service.AuthorizeAsync(
            userId,
            "status-current",
            [AiReadPermissions.Device]);

        Assert.Null(result);
        Assert.Equal(1, permissionProvider.GetPermissionsCalls);
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
    [InlineData(IIoTClaimTypes.AiDelegatedUserActor)]
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

    private static ClaimsPrincipal CreateAiPrincipal(string actorType)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(IIoTClaimTypes.ActorType, actorType),
            new Claim(OpenIddictConstants.Claims.Scope, AiReadDelegationDefaults.Scope),
            new Claim(OpenIddictConstants.Claims.Audience, AiReadDelegationDefaults.Audience)
        ], authenticationType: "test");
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

    private sealed class StubIdentityAccountStore(IList<string> roles)
        : IIdentityAccountStore
    {
        public int GetRolesCalls { get; private set; }

        public Task<IList<string>> GetRolesAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            GetRolesCalls++;
            return Task.FromResult(roles);
        }

        public Task<Result<IdentityAccount>> CreateAsync(
            IdentityAccount account,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IdentityAccount?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IdentityAccount?> GetByEmployeeNoAsync(
            string employeeNo,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IdentityAccountStateSnapshot?> GetStateSnapshotAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<IdentityAccountCompareExchangeOutcome>> CompareExchangeStateAsync(
            Guid id,
            IdentityAccountStateSnapshot expected,
            bool isEnabled,
            string securityStamp,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<bool>> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<bool>> AssignRoleAsync(
            Guid id,
            string roleName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<bool>> ReplaceAssignableRoleAsync(
            Guid id,
            string? roleName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubPermissionProvider(IList<string> permissions)
        : IPermissionProvider
    {
        public IList<string> Permissions { get; set; } = permissions;

        public int GetPermissionsCalls { get; private set; }

        public Task<IList<string>> GetPermissionsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            GetPermissionsCalls++;
            return Task.FromResult(Permissions);
        }
    }

    private sealed class StubDevicePermissionService(IReadOnlyList<Guid> deviceIds)
        : IDevicePermissionService
    {
        public IReadOnlyList<Guid> DeviceIds { get; set; } = deviceIds;

        public int GetDeviceCalls { get; private set; }

        public Task<IReadOnlyList<Guid>> GetAccessibleDeviceIdsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            GetDeviceCalls++;
            return Task.FromResult(DeviceIds);
        }
    }
}
