using System.Security.Claims;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Persistence;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Authorization;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using Xunit;

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class EmployeeRoleAssignmentPersistenceTests
{
    [Fact]
    public async Task RoleChange_ShouldUseCanonicalRoleRotateVersionRevokeSessionsAndPreserveOtherState()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        var audit = new RecordingAuditTrailService();
        SeededIdentity seed;

        using (var scope = provider.CreateScope())
        {
            seed = await SeedAsync(scope.ServiceProvider);
            var handler = CreateHandler(
                scope.ServiceProvider,
                auditTrailService: audit);

            var result = await handler.Handle(
                new UpdateEmployeeRoleCommand(seed.EmployeeId, "  roleb  "),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        using var verificationScope = provider.CreateScope();
        var services = verificationScope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(seed.EmployeeId.ToString()))!;
        var roles = await userManager.GetRolesAsync(user);
        var claims = await userManager.GetClaimsAsync(user);
        var employee = await dbContext.Employees
            .AsNoTracking()
            .Include(item => item.DeviceAccesses)
            .SingleAsync(item => item.Id == seed.EmployeeId);
        var profile = await new CloudOidcUserProfileService(dbContext)
            .GetByUserIdAsync(seed.EmployeeId);
        var refreshSession = await dbContext.RefreshTokenSessions
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.RefreshSessionId);
        var oidcToken = await dbContext.OpenIddictTokens
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.OidcTokenId);
        var authorization = await dbContext.OpenIddictAuthorizations
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.OidcAuthorizationId);

        Assert.Equal(["RoleB"], roles);
        Assert.True(user.IsEnabled);
        Assert.True(employee.IsActive);
        Assert.Contains(
            claims,
            claim => claim.Type == IIoTClaimTypes.Permission
                     && claim.Value == CloudPermissionCatalog.Device.Read);
        Assert.Contains(
            employee.DeviceAccesses,
            access => access.DeviceId == seed.DeviceId);
        Assert.NotNull(profile);
        Assert.NotEqual(seed.StatusVersion, profile.StatusVersion);
        Assert.NotEqual(seed.SecurityStamp, user.SecurityStamp);
        Assert.NotNull(refreshSession.RevokedAtUtc);
        Assert.Equal("employee-role-changed", refreshSession.RevokedReason);
        Assert.Equal(OpenIddictConstants.Statuses.Revoked, oidcToken.Status);
        Assert.Equal(OpenIddictConstants.Statuses.Revoked, authorization.Status);

        var entry = Assert.Single(audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.Equal("Employee.Role.Update", entry.OperationType);
        Assert.Equal(seed.EmployeeId.ToString(), entry.TargetIdOrKey);
        Assert.Contains("\"beforeRoles\":[\"RoleA\"]", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("\"afterRoles\":[\"RoleB\"]", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("\"requestedRole\":\"roleb\"", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("\"canonicalRole\":\"RoleB\"", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("\"resultCode\":\"Succeeded\"", entry.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NullRole_ShouldClearInactiveEmployeeRoleWithoutEnablingAccount()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        SeededIdentity seed;

        using (var scope = provider.CreateScope())
        {
            seed = await SeedAsync(
                scope.ServiceProvider,
                accountEnabled: false,
                employeeActive: false);
            var handler = CreateHandler(scope.ServiceProvider);

            var result = await handler.Handle(
                new UpdateEmployeeRoleCommand(seed.EmployeeId, null),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        using var verificationScope = provider.CreateScope();
        var services = verificationScope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(seed.EmployeeId.ToString()))!;
        var roles = await userManager.GetRolesAsync(user);
        var employee = await dbContext.Employees
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.EmployeeId);

        Assert.Empty(roles);
        Assert.False(user.IsEnabled);
        Assert.False(employee.IsActive);
        Assert.NotEqual(seed.SecurityStamp, user.SecurityStamp);
    }

    [Fact]
    public async Task UnknownRole_ShouldNotRemoveExistingRole()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        Guid employeeId;

        using (var scope = provider.CreateScope())
        {
            var seed = await SeedAsync(scope.ServiceProvider, createRoleB: false);
            employeeId = seed.EmployeeId;
            var store = CreateIdentityStore(scope.ServiceProvider);

            var result = await store.ReplaceAssignableRoleAsync(
                employeeId,
                "UnknownRole",
                CancellationToken.None);

            Assert.False(result.IsSuccess);
        }

        using var verificationScope = provider.CreateScope();
        var userManager = verificationScope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(employeeId.ToString()))!;

        Assert.Equal(["RoleA"], await userManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task SameCanonicalRole_ShouldNotRotateVersionOrRevokeSessions()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        var audit = new RecordingAuditTrailService();
        SeededIdentity seed;

        using (var scope = provider.CreateScope())
        {
            seed = await SeedAsync(scope.ServiceProvider);
            var handler = CreateHandler(
                scope.ServiceProvider,
                auditTrailService: audit);

            var result = await handler.Handle(
                new UpdateEmployeeRoleCommand(seed.EmployeeId, " rolea "),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        using var verificationScope = provider.CreateScope();
        var services = verificationScope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(seed.EmployeeId.ToString()))!;
        var refreshSession = await dbContext.RefreshTokenSessions
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.RefreshSessionId);
        var oidcToken = await dbContext.OpenIddictTokens
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.OidcTokenId);

        Assert.Equal(["RoleA"], await userManager.GetRolesAsync(user));
        Assert.Equal(seed.SecurityStamp, user.SecurityStamp);
        Assert.Null(refreshSession.RevokedAtUtc);
        Assert.Equal(OpenIddictConstants.Statuses.Valid, oidcToken.Status);
        Assert.Contains(
            "\"resultCode\":\"NoChange\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FailureStage.RolePersistence)]
    [InlineData(FailureStage.StatusVersionRotation)]
    [InlineData(FailureStage.SessionRevocation)]
    public async Task FailureAtAnyTransactionalStage_ShouldRestoreOldRoleVersionAndSessions(
        FailureStage failureStage)
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        SeededIdentity seed;

        using (var scope = provider.CreateScope())
        {
            seed = await SeedAsync(scope.ServiceProvider);
            var realStore = CreateIdentityStore(scope.ServiceProvider);
            IIdentityAccountStore store = failureStage switch
            {
                FailureStage.RolePersistence => new FaultInjectingIdentityAccountStore(
                    realStore,
                    failRolePersistence: true),
                FailureStage.StatusVersionRotation => new FaultInjectingIdentityAccountStore(
                    realStore,
                    failStatusVersionRotation: true),
                _ => realStore
            };
            IHumanSessionRevocationService sessionRevocationService =
                new HumanSessionRevocationService(
                    scope.ServiceProvider.GetRequiredService<IIoTDbContext>());
            if (failureStage == FailureStage.SessionRevocation)
            {
                sessionRevocationService = new RevokingThenThrowSessionService(
                    sessionRevocationService);
            }

            var handler = CreateHandler(
                scope.ServiceProvider,
                store,
                sessionRevocationService);
            var operation = () => handler.Handle(
                new UpdateEmployeeRoleCommand(seed.EmployeeId, "RoleB"),
                CancellationToken.None);

            if (failureStage == FailureStage.SessionRevocation)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(operation);
            }
            else
            {
                Assert.False((await operation()).IsSuccess);
            }
        }

        using var verificationScope = provider.CreateScope();
        var services = verificationScope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(seed.EmployeeId.ToString()))!;
        var refreshSession = await dbContext.RefreshTokenSessions
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.RefreshSessionId);
        var oidcToken = await dbContext.OpenIddictTokens
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.OidcTokenId);
        var authorization = await dbContext.OpenIddictAuthorizations
            .AsNoTracking()
            .SingleAsync(item => item.Id == seed.OidcAuthorizationId);

        Assert.Equal(["RoleA"], await userManager.GetRolesAsync(user));
        Assert.Equal(seed.SecurityStamp, user.SecurityStamp);
        Assert.Null(refreshSession.RevokedAtUtc);
        Assert.Null(refreshSession.RevokedReason);
        Assert.Equal(OpenIddictConstants.Statuses.Valid, oidcToken.Status);
        Assert.Equal(OpenIddictConstants.Statuses.Valid, authorization.Status);
    }

    private static async Task<SeededIdentity> SeedAsync(
        IServiceProvider serviceProvider,
        bool accountEnabled = true,
        bool employeeActive = true,
        bool createRoleB = true)
    {
        var dbContext = serviceProvider.GetRequiredService<IIoTDbContext>();
        var employee = TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            $"E-ROLE-{Guid.NewGuid():N}",
            "Role Target",
            accountEnabled,
            employeeActive);
        var unique = Guid.NewGuid().ToString("N");
        var process = new MfgProcess(
            $"ROLE-{unique}",
            "Role assignment device process");
        var device = new Device(
            $"Role assignment device {unique}",
            $"ROLE-{unique}"[..24],
            process.Id);
        var deviceId = device.Id;
        employee.AddDeviceAccess(deviceId);
        device.ClearDomainEvents();
        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.Add(device);
        var applicationUser = dbContext.Users.Local.Single(user => user.Id == employee.Id);
        applicationUser.SecurityStamp = $"stamp-before-{Guid.NewGuid():N}";
        await dbContext.SaveChangesAsync();

        var roleManager = serviceProvider
            .GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = serviceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await roleManager.CreateAsync(new IdentityRole<Guid>("RoleA"))).Succeeded);
        if (createRoleB)
        {
            Assert.True((await roleManager.CreateAsync(new IdentityRole<Guid>("RoleB"))).Succeeded);
        }

        var user = (await userManager.FindByIdAsync(employee.Id.ToString()))!;
        Assert.True((await userManager.AddToRoleAsync(user, "RoleA")).Succeeded);
        Assert.True((await userManager.AddClaimAsync(
            user,
            new Claim(
                IIoTClaimTypes.Permission,
                CloudPermissionCatalog.Device.Read))).Succeeded);

        var authorization = new OpenIddictEntityFrameworkCoreAuthorization<Guid>
        {
            Id = Guid.NewGuid(),
            Subject = employee.Id.ToString(),
            Status = OpenIddictConstants.Statuses.Valid,
            Type = "permanent"
        };
        var oidcToken = new OpenIddictEntityFrameworkCoreToken<Guid>
        {
            Id = Guid.NewGuid(),
            Authorization = authorization,
            Subject = employee.Id.ToString(),
            Status = OpenIddictConstants.Statuses.Valid,
            Type = "authorization_code"
        };
        var refreshSession = new RefreshTokenSession
        {
            Id = Guid.NewGuid(),
            ActorType = IIoTClaimTypes.HumanActor,
            SubjectId = employee.Id,
            TokenHash = $"role-refresh-{Guid.NewGuid():N}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        dbContext.OpenIddictAuthorizations.Add(authorization);
        dbContext.OpenIddictTokens.Add(oidcToken);
        dbContext.RefreshTokenSessions.Add(refreshSession);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var profile = await new CloudOidcUserProfileService(dbContext)
            .GetByUserIdAsync(employee.Id);
        Assert.NotNull(profile);

        return new SeededIdentity(
            employee.Id,
            deviceId,
            applicationUser.SecurityStamp!,
            profile.StatusVersion!,
            refreshSession.Id,
            oidcToken.Id,
            authorization.Id);
    }

    private static UpdateEmployeeRoleHandler CreateHandler(
        IServiceProvider serviceProvider,
        IIdentityAccountStore? identityAccountStore = null,
        IHumanSessionRevocationService? sessionRevocationService = null,
        RecordingAuditTrailService? auditTrailService = null)
    {
        var dbContext = serviceProvider.GetRequiredService<IIoTDbContext>();
        var store = identityAccountStore ?? CreateIdentityStore(serviceProvider);
        return new UpdateEmployeeRoleHandler(
            store,
            new RolePolicyService(
                serviceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                serviceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>()),
            new EfUnitOfWork(dbContext, NullLogger<EfUnitOfWork>.Instance),
            sessionRevocationService ?? new HumanSessionRevocationService(dbContext),
            new AdminTargetGuard(store),
            new EmployeeLookupService(dbContext),
            new HumanCurrentUser(Guid.NewGuid(), "HrAdmin"),
            auditTrailService ?? new RecordingAuditTrailService());
    }

    private static IdentityAccountStore CreateIdentityStore(
        IServiceProvider serviceProvider)
        => new(
            serviceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            serviceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>());

    public enum FailureStage
    {
        RolePersistence,
        StatusVersionRotation,
        SessionRevocation
    }

    private sealed record SeededIdentity(
        Guid EmployeeId,
        Guid DeviceId,
        string SecurityStamp,
        string StatusVersion,
        Guid RefreshSessionId,
        Guid OidcTokenId,
        Guid OidcAuthorizationId);

    private sealed class HumanCurrentUser(Guid userId, string role)
        : ICurrentUser
    {
        public string Id => userId.ToString();

        public string UserName => $"human-{userId:N}";

        public IReadOnlyCollection<string> Roles => [role];

        public string ActorType => IIoTClaimTypes.HumanActor;

        public IReadOnlyCollection<string> Permissions =>
            [CloudPermissionCatalog.Employee.UpdateAccess];

        public Guid? DeviceId => null;

        public bool IsAuthenticated => true;
    }

    private sealed class RecordingAuditTrailService : IAuditTrailService
    {
        public List<AuditTrailEntry> Entries { get; } = [];

        public Task TryWriteAsync(
            AuditTrailEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<bool> TryWriteConfirmedAsync(
            AuditTrailEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.FromResult(true);
        }
    }

    private sealed class RevokingThenThrowSessionService(
        IHumanSessionRevocationService inner)
        : IHumanSessionRevocationService
    {
        public async Task RevokeAllAsync(
            Guid subjectId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            await inner.RevokeAllAsync(subjectId, reason, cancellationToken);
            throw new InvalidOperationException("injected session revocation failure");
        }
    }

    private sealed class FaultInjectingIdentityAccountStore(
        IIdentityAccountStore inner,
        bool failRolePersistence = false,
        bool failStatusVersionRotation = false)
        : IIdentityAccountStore
    {
        public Task<Result<IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount>>
            CreateAsync(
                IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount account,
                CancellationToken cancellationToken = default)
            => inner.CreateAsync(account, cancellationToken);

        public Task<IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => inner.GetByIdAsync(id, cancellationToken);

        public Task<IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount?>
            GetByEmployeeNoAsync(
                string employeeNo,
                CancellationToken cancellationToken = default)
            => inner.GetByEmployeeNoAsync(employeeNo, cancellationToken);

        public Task<string?> GetSecurityStampAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => inner.GetSecurityStampAsync(id, cancellationToken);

        public Task<Result<bool>> SetEnabledAsync(
            Guid id,
            bool isEnabled,
            CancellationToken cancellationToken = default)
            => inner.SetEnabledAsync(id, isEnabled, cancellationToken);

        public Task<Result<bool>> ActivateWithSecurityStampAsync(
            Guid id,
            string securityStamp,
            CancellationToken cancellationToken = default)
            => inner.ActivateWithSecurityStampAsync(
                id,
                securityStamp,
                cancellationToken);

        public async Task<Result<bool>> RotateSecurityStampAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.RotateSecurityStampAsync(id, cancellationToken);
            return failStatusVersionRotation && result.IsSuccess
                ? Result.Failure("injected status version failure")
                : result;
        }

        public Task<Result<bool>> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => inner.DeleteAsync(id, cancellationToken);

        public Task<Result<bool>> AssignRoleAsync(
            Guid id,
            string roleName,
            CancellationToken cancellationToken = default)
            => inner.AssignRoleAsync(id, roleName, cancellationToken);

        public async Task<Result<bool>> ReplaceAssignableRoleAsync(
            Guid id,
            string? roleName,
            CancellationToken cancellationToken = default)
        {
            var result = await inner.ReplaceAssignableRoleAsync(
                id,
                roleName,
                cancellationToken);
            return failRolePersistence && result.IsSuccess
                ? Result.Failure("injected role persistence failure")
                : result;
        }

        public Task<IList<string>> GetRolesAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => inner.GetRolesAsync(id, cancellationToken);
    }
}
