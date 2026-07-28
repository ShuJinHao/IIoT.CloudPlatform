using System.Security.Claims;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.IdentityService.Queries;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class IdentityProviderPersistenceTests
{
    [Fact]
    public async Task DevicePermissionService_ShouldReadCurrentAssignmentsDirectly()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var employee = new IIoT.Core.Employees.Aggregates.Employees.Employee(Guid.NewGuid(), "E1001", "Operator");
        var process = new MfgProcess($"Identity-{Guid.NewGuid():N}", "Identity permission process");
        var firstDevice = new Device("Identity permission device 1", $"IDENTITY-{Guid.NewGuid():N}"[..24], process.Id);
        var secondDevice = new Device("Identity permission device 2", $"IDENTITY-{Guid.NewGuid():N}"[..24], process.Id);
        var firstDeviceId = firstDevice.Id;
        var secondDeviceId = secondDevice.Id;
        employee.AddDeviceAccess(firstDeviceId);
        dbContext.Users.Add(new ApplicationUser
        {
            Id = employee.Id,
            UserName = employee.EmployeeNo,
            IsEnabled = true
        });
        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.AddRange(firstDevice, secondDevice);
        dbContext.Employees.Add(employee);
        await dbContext.SaveChangesAsync();

        var failingCache = new FailingCacheService();
        using var authorityServices = new ServiceCollection()
            .AddSingleton(dbContext)
            .AddSingleton<ICacheService>(failingCache)
            .AddScoped<IDevicePermissionService, DevicePermissionService>()
            .BuildServiceProvider();
        var service = authorityServices.GetRequiredService<IDevicePermissionService>();

        var firstRead = await service.GetAccessibleDeviceIdsAsync(employee.Id);
        employee.AddDeviceAccess(secondDeviceId);
        await dbContext.SaveChangesAsync();
        var secondRead = await service.GetAccessibleDeviceIdsAsync(employee.Id);

        Assert.Equal([firstDeviceId], firstRead);
        Assert.Equal(
            new[] { firstDeviceId, secondDeviceId }.OrderBy(value => value),
            secondRead.OrderBy(value => value));
        Assert.Equal(0, failingCache.CallCount);
    }

    [Fact]
    public async Task PermissionProvider_ShouldReadCurrentRoleClaimsDirectly()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        using var scope = provider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var permissionProvider = TestServiceProviders.CreatePermissionProvider(scope.ServiceProvider);

        const string roleName = "Supervisor";
        await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
        await roleManager.AddClaimAsync(
            await roleManager.FindByNameAsync(roleName) ?? throw new InvalidOperationException("Role was not created."),
            new Claim("Permission", "Device.Read"));

        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "user-001", IsEnabled = true };
        Assert.True((await userManager.CreateAsync(user, "Password123")).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, roleName)).Succeeded);

        var permissions = await permissionProvider.GetPermissionsAsync(user.Id);
        var role = await roleManager.FindByNameAsync(roleName)
                   ?? throw new InvalidOperationException("Role was not created.");
        var addUpdatedClaim = await roleManager.AddClaimAsync(role, new Claim("Permission", "Recipe.Read"));
        var updatedPermissions = await permissionProvider.GetPermissionsAsync(user.Id);

        Assert.True(addUpdatedClaim.Succeeded);
        Assert.Contains("Device.Read", permissions);
        Assert.Contains("Device.Read", updatedPermissions);
        Assert.Contains("Recipe.Read", updatedPermissions);
    }

    [Fact]
    public async Task RolePolicyService_ShouldRejectUnknownRolePermissionWithoutPartialWrite()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        using var scope = provider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var service = new RolePolicyService(userManager, roleManager);
        const string roleName = "Supervisor";
        Assert.True((await roleManager.CreateAsync(new IdentityRole<Guid>(roleName))).Succeeded);
        var role = await roleManager.FindByNameAsync(roleName)
            ?? throw new InvalidOperationException("Role was not created.");
        Assert.True((await roleManager.AddClaimAsync(
            role,
            new Claim(IIoTClaimTypes.Permission, DevicePermissions.Read))).Succeeded);

        var rejected = await service.UpdateRolePermissionsAsync(
            roleName,
            [CloudPermissionCatalog.Recipe.Read, "Permission.Forged"]);
        var afterRejected = await service.GetRolePermissionsAsync(roleName);

        Assert.False(rejected.IsSuccess);
        Assert.Equal([DevicePermissions.Read], afterRejected);

        var normalized = await service.UpdateRolePermissionsAsync(
            roleName,
            [" recipe.read ", "RECIPE.READ"]);
        var afterNormalized = await service.GetRolePermissionsAsync(roleName);

        Assert.True(normalized.IsSuccess);
        Assert.Equal([CloudPermissionCatalog.Recipe.Read], afterNormalized);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RolePolicyService_ShouldAcceptRoleAdminPagePayloadAndPreserveLockedPermissions(
        bool includeLockedPermissions)
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        using var scope = provider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var service = new RolePolicyService(userManager, roleManager);
        Assert.True((await roleManager.CreateAsync(
            new IdentityRole<Guid>(SystemRoles.RoleAdmin))).Succeeded);
        var role = await roleManager.FindByNameAsync(SystemRoles.RoleAdmin)
            ?? throw new InvalidOperationException("RoleAdmin role was not created.");
        string[] lockedPermissions =
        [
            CloudPermissionCatalog.Role.Read,
            CloudPermissionCatalog.Role.Define,
            CloudPermissionCatalog.Role.Update
        ];
        foreach (var permission in lockedPermissions.Append(DevicePermissions.Read))
        {
            Assert.True((await roleManager.AddClaimAsync(
                role,
                new Claim(IIoTClaimTypes.Permission, permission))).Succeeded);
        }

        var pagePayload = includeLockedPermissions
            ? lockedPermissions.Append(CloudPermissionCatalog.Recipe.Read).ToList()
            : new List<string> { CloudPermissionCatalog.Recipe.Read };
        var expected = CloudPermissionCatalog.NormalizeForTargetRole(
            SystemRoles.RoleAdmin,
            pagePayload);
        var update = await service.UpdateRolePermissionsAsync(
            SystemRoles.RoleAdmin,
            pagePayload);
        var permissions = await service.GetRolePermissionsAsync(SystemRoles.RoleAdmin);

        Assert.True(expected.IsValid);
        Assert.True(update.IsSuccess);
        Assert.NotNull(permissions);
        Assert.Equal(
            expected.Permissions.OrderBy(permission => permission),
            permissions.OrderBy(permission => permission));
        Assert.DoesNotContain(DevicePermissions.Read, permissions);
    }

    [Fact]
    public async Task RolePolicyService_ShouldAcceptHrAdminPagePayloadAndPreserveRoleRead()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        using var scope = provider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var service = new RolePolicyService(userManager, roleManager);
        Assert.True((await roleManager.CreateAsync(
            new IdentityRole<Guid>(SystemRoles.HrAdmin))).Succeeded);
        var role = await roleManager.FindByNameAsync(SystemRoles.HrAdmin)
            ?? throw new InvalidOperationException("HrAdmin role was not created.");
        Assert.True((await roleManager.AddClaimAsync(
            role,
            new Claim(IIoTClaimTypes.Permission, CloudPermissionCatalog.Role.Read))).Succeeded);
        List<string> pagePayload =
        [
            CloudPermissionCatalog.Role.Read,
            CloudPermissionCatalog.Employee.Read,
            CloudPermissionCatalog.Employee.Update,
            CloudPermissionCatalog.Employee.Deactivate
        ];
        var expected = CloudPermissionCatalog.NormalizeForTargetRole(
            SystemRoles.HrAdmin,
            pagePayload);

        var update = await service.UpdateRolePermissionsAsync(
            SystemRoles.HrAdmin,
            pagePayload);
        var permissions = await service.GetRolePermissionsAsync(SystemRoles.HrAdmin);

        Assert.True(expected.IsValid);
        Assert.True(update.IsSuccess);
        Assert.NotNull(permissions);
        Assert.Equal(
            expected.Permissions.OrderBy(permission => permission),
            permissions.OrderBy(permission => permission));
        Assert.Contains(CloudPermissionCatalog.Role.Read, permissions);
        Assert.DoesNotContain(CloudPermissionCatalog.Role.Define, permissions);
        Assert.DoesNotContain(CloudPermissionCatalog.Role.Update, permissions);
    }

    [Fact]
    public async Task RolePolicyService_ShouldRejectGovernancePermissionsForOrdinaryRoleWithoutPartialWrite()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        using var scope = provider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var service = new RolePolicyService(userManager, roleManager);
        const string roleName = "Supervisor";
        Assert.True((await roleManager.CreateAsync(new IdentityRole<Guid>(roleName))).Succeeded);
        var role = await roleManager.FindByNameAsync(roleName)
            ?? throw new InvalidOperationException("Role was not created.");
        Assert.True((await roleManager.AddClaimAsync(
            role,
            new Claim(IIoTClaimTypes.Permission, DevicePermissions.Read))).Succeeded);

        var update = await service.UpdateRolePermissionsAsync(
            roleName,
            [
                CloudPermissionCatalog.Role.Read,
                CloudPermissionCatalog.Role.Define,
                CloudPermissionCatalog.Role.Update
            ]);
        var permissions = await service.GetRolePermissionsAsync(roleName);

        Assert.False(update.IsSuccess);
        Assert.Equal([DevicePermissions.Read], permissions);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData(" Admin ")]
    [InlineData("ADMIN ")]
    [InlineData("admin")]
    public async Task IdentityAccountStore_ShouldRejectAssigningAdminLikeRole(
        string roleName)
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        using var scope = provider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        Assert.True((await roleManager.CreateAsync(new IdentityRole<Guid>(roleName))).Succeeded);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"assign-admin-like-{Guid.NewGuid():N}",
            IsEnabled = true
        };
        Assert.True((await userManager.CreateAsync(user, "Password123")).Succeeded);
        var store = new IdentityAccountStore(userManager, roleManager);

        var result = await store.AssignRoleAsync(user.Id, roleName);
        var assignedRoles = await userManager.GetRolesAsync(user);

        Assert.False(result.IsSuccess);
        Assert.Empty(assignedRoles);
    }

    [Fact]
    public async Task RolePolicyService_ShouldRejectUnknownPersonalPermissionWithoutPartialWrite()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        using var scope = provider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var service = new RolePolicyService(userManager, roleManager);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "user-permission-test",
            IsEnabled = true
        };
        Assert.True((await userManager.CreateAsync(user, "Password123")).Succeeded);
        Assert.True((await userManager.AddClaimAsync(
            user,
            new Claim(IIoTClaimTypes.Permission, DevicePermissions.Read))).Succeeded);

        var rejected = await service.UpdateUserPersonalPermissionsAsync(
            user.Id,
            [CloudPermissionCatalog.Recipe.Read, "Permission.Forged"]);
        var afterRejected = await service.GetUserPersonalPermissionsAsync(user.Id);

        Assert.False(rejected.IsSuccess);
        Assert.Equal([DevicePermissions.Read], afterRejected);

        var normalized = await service.UpdateUserPersonalPermissionsAsync(
            user.Id,
            [" recipe.read ", "RECIPE.READ"]);
        var afterNormalized = await service.GetUserPersonalPermissionsAsync(user.Id);

        Assert.True(normalized.IsSuccess);
        Assert.Equal([CloudPermissionCatalog.Recipe.Read], afterNormalized);
    }

    [Fact]
    public async Task RolePolicyService_ShouldProtectAdminRoleAtInfrastructureBoundary()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        using var scope = provider.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var service = new RolePolicyService(userManager, roleManager);
        Assert.True((await roleManager.CreateAsync(
            new IdentityRole<Guid>(SystemRoles.Admin))).Succeeded);
        var adminRole = await roleManager.FindByNameAsync(SystemRoles.Admin)
            ?? throw new InvalidOperationException("Admin role was not created.");
        Assert.True((await roleManager.AddClaimAsync(
            adminRole,
            new Claim(IIoTClaimTypes.Permission, DevicePermissions.Read))).Succeeded);

        var createResult = await service.CreateRoleAsync(" admin ");
        var updateResult = await service.UpdateRolePermissionsAsync(
            " ADMIN ",
            [CloudPermissionCatalog.Recipe.Read]);
        var permissions = await service.GetRolePermissionsAsync(SystemRoles.Admin);

        Assert.False(createResult.IsSuccess);
        Assert.False(updateResult.IsSuccess);
        Assert.Equal([DevicePermissions.Read], permissions);
    }

    private sealed class FailingCacheService : ICacheService
    {
        public int CallCount { get; private set; }

        public Task<T?> GetOrSetAsync<T>(
            string key,
            Func<CancellationToken, Task<T?>> factory,
            Func<T?, bool> shouldCache,
            TimeSpan absoluteExpireTime,
            CancellationToken cancellationToken = default)
            => Fail<T>();

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
            => Fail();

        public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
            => Fail();

        private Task<T?> Fail<T>()
        {
            CallCount++;
            return Task.FromException<T?>(new InvalidOperationException("cache unavailable"));
        }

        private Task Fail()
        {
            CallCount++;
            return Task.FromException(new InvalidOperationException("cache unavailable"));
        }
    }
}
