using System.Security.Claims;
using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.IdentityService.Commands;
using IIoT.IdentityService.Queries;
using IIoT.ProductionService.Commands.Bootstrap.Devices;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.CrossCutting.Caching.Options;
using IIoT.Services.Contracts.Authorization;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class AuthorizationAndIdentityBehaviorTests
{
    [Fact]
    public async Task DevicePermissionService_ShouldUseResolvedCacheExpiration()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoT.EntityFrameworkCore.IIoTDbContext>();
        var employee = new IIoT.Core.Employees.Aggregates.Employees.Employee(Guid.NewGuid(), "E1001", "Operator");
        employee.AddDeviceAccess(Guid.NewGuid());
        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync();

        var cacheService = new RecordingCacheService();
        var service = new DevicePermissionService(
            dbContext,
            cacheService,
            Microsoft.Extensions.Options.Options.Create(new PermissionCacheOptions
            {
                ExpirationMinutes = 10,
                ExpirationHours = 2
            }));

        var accessibleDeviceIds = await service.GetAccessibleDeviceIdsAsync(employee.Id, isAdmin: false);

        Assert.Single(accessibleDeviceIds!);
        Assert.Equal(TimeSpan.FromMinutes(10), cacheService.LastAbsoluteExpireTime);
        Assert.Equal(1, cacheService.GetOrSetCalls);
    }

    [Fact]
    public async Task PermissionProvider_ShouldUseSharedCacheKeyForInvalidationCompatibility()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        using var scope = provider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var cacheService = new RecordingCacheService();
        var permissionProvider = new PermissionProvider(
            userManager,
            roleManager,
            cacheService,
            Microsoft.Extensions.Options.Options.Create(new PermissionCacheOptions
            {
                KeyPrefix = "custom-prefix:",
                ExpirationMinutes = 10
            }));

        const string roleName = "Supervisor";
        await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
        await roleManager.AddClaimAsync(
            await roleManager.FindByNameAsync(roleName) ?? throw new InvalidOperationException("Role was not created."),
            new Claim("Permission", "Device.Read"));

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "user-001",
            IsEnabled = true
        };

        var createUser = await userManager.CreateAsync(user, "Password123");
        Assert.True(createUser.Succeeded);
        var addRole = await userManager.AddToRoleAsync(user, roleName);
        Assert.True(addRole.Succeeded);

        var permissions = await permissionProvider.GetPermissionsAsync(user.Id);

        Assert.Contains("Device.Read", permissions);
        Assert.Equal(CacheKeys.PermissionByUser(user.Id), cacheService.LastSetKey);
        Assert.Equal(1, cacheService.GetOrSetCalls);
    }

    [Fact]
    public async Task DefineRolePolicyHandler_ShouldDeleteRoleWhenPermissionAssignmentFails()
    {
        var rolePolicyService = new StubRolePolicyService
        {
            UpdateRolePermissionsResult = Result.Failure("permission update failed")
        };
        var cacheService = new RecordingCacheService();
        var handler = new DefineRolePolicyHandler(rolePolicyService, cacheService);

        var result = await handler.Handle(
            new DefineRolePolicyCommand("Auditor", ["Device.Read"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Auditor", rolePolicyService.DeletedRoleName);
    }

    [Fact]
    public async Task DefineRolePolicyHandler_ShouldNotDeleteExistingRoleWhenPermissionAssignmentFails()
    {
        var rolePolicyService = new StubRolePolicyService
        {
            RoleExists = true,
            UpdateRolePermissionsResult = Result.Failure("permission update failed")
        };
        var cacheService = new RecordingCacheService();
        var handler = new DefineRolePolicyHandler(rolePolicyService, cacheService);

        var result = await handler.Handle(
            new DefineRolePolicyCommand("Auditor", ["Device.Read"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(rolePolicyService.DeletedRoleName);
    }

    [Fact]
    public async Task ChangePasswordHandler_ShouldRejectCrossUserPasswordChange()
    {
        var passwordService = new StubIdentityPasswordService();
        var refreshTokenService = new StubRefreshTokenService();
        var currentUserId = Guid.NewGuid();
        var handler = new ChangePasswordHandler(
            passwordService,
            refreshTokenService,
            new TestCurrentUser
            {
                Id = currentUserId.ToString(),
                Role = "Operator",
                UserName = "operator-001",
                IsAuthenticated = true
            });

        var result = await handler.Handle(
            new ChangePasswordCommand(Guid.NewGuid(), "OldPassword123!", "NewPassword123!"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(passwordService.LastChangedUserId);
    }

    [Fact]
    public async Task ChangePasswordHandler_ShouldAllowCurrentUserToChangeOwnPassword()
    {
        var currentUserId = Guid.NewGuid();
        var passwordService = new StubIdentityPasswordService();
        var refreshTokenService = new StubRefreshTokenService();
        var handler = new ChangePasswordHandler(
            passwordService,
            refreshTokenService,
            new TestCurrentUser
            {
                Id = currentUserId.ToString(),
                Role = "Operator",
                UserName = "operator-001",
                IsAuthenticated = true
            });

        var result = await handler.Handle(
            new ChangePasswordCommand(currentUserId, "OldPassword123!", "NewPassword123!"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(currentUserId, passwordService.LastChangedUserId);
    }

    [Fact]
    public void GetAllRolesQuery_ShouldRequireRoleDefinePermission()
    {
        var attribute = typeof(GetAllRolesQuery)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
            .Cast<AuthorizeRequirementAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal("Role.Define", attribute!.Permission);
    }

    [Fact]
    public async Task DeviceBindingBehavior_ShouldAllowMatchingDeviceId()
    {
        var deviceId = Guid.NewGuid();
        var behavior = new DeviceBindingBehavior<DeviceScopedCommand, Result<bool>>(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "edge-operator",
                Role = SystemRoles.Admin,
                DeviceId = deviceId,
                IsAuthenticated = true
            });

        var result = await behavior.Handle(
            new DeviceScopedCommand(deviceId),
            _ => Task.FromResult(Result.Success(true)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeviceBindingBehavior_ShouldRejectMismatchedDeviceId()
    {
        var behavior = new DeviceBindingBehavior<DeviceScopedCommand, Result<bool>>(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "edge-operator",
                Role = SystemRoles.Admin,
                DeviceId = Guid.NewGuid(),
                IsAuthenticated = true
            });

        await Assert.ThrowsAsync<IIoT.Services.CrossCutting.Exceptions.ForbiddenException>(() =>
            behavior.Handle(
                new DeviceScopedCommand(Guid.NewGuid()),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));
    }

    [Fact]
    public async Task DistributedLockBehavior_ShouldRejectMissingTemplateProperty()
    {
        var behavior = new DistributedLockBehavior<BrokenLockCommand, Result<bool>>(new NoopDistributedLockService());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new BrokenLockCommand(Guid.NewGuid()),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains("MissingProperty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshHumanIdentityHandler_ShouldRevokeTokensWhenAccountUnavailable()
    {
        var subjectId = Guid.NewGuid();
        var account = IdentityAccount.Create(subjectId, "E2001");
        account.Disable();

        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById = account
        };
        var refreshTokenService = new StubRefreshTokenService
        {
            NextRotateSubjectId = subjectId
        };
        var handler = new RefreshHumanIdentityHandler(
            identityStore,
            new StubPermissionProvider(),
            new RecordingCacheService(),
            new StubJwtTokenGenerator(),
            refreshTokenService);

        var result = await handler.Handle(
            new RefreshHumanIdentityCommand("refresh-human"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Contains(refreshTokenService.Revocations, x =>
            x.ActorType == IIoT.Services.Contracts.Identity.IIoTClaimTypes.HumanActor
            && x.SubjectId == subjectId
            && x.Reason == "identity-unavailable");
    }

    [Fact]
    public async Task RefreshEdgeDeviceIdentityHandler_ShouldRevokeTokensWhenDeviceUnavailable()
    {
        var deviceId = Guid.NewGuid();
        var refreshTokenService = new StubRefreshTokenService
        {
            NextRotateSubjectId = deviceId
        };
        var handler = new RefreshEdgeDeviceIdentityHandler(
            new InMemoryRepository<IIoT.Core.Production.Aggregates.Devices.Device>(),
            new StubJwtTokenGenerator(),
            refreshTokenService);

        var result = await handler.Handle(
            new RefreshEdgeDeviceIdentityCommand("refresh-edge"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Contains(refreshTokenService.Revocations, x =>
            x.ActorType == IIoT.Services.Contracts.Identity.IIoTClaimTypes.EdgeDeviceActor
            && x.SubjectId == deviceId
            && x.Reason == "device-unavailable");
    }

    private sealed class StubPermissionProvider : IPermissionProvider
    {
        public Task<IList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IList<string>>([]);
        }
    }
}
