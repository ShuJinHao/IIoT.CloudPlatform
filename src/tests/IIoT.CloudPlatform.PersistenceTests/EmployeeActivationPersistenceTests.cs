using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Persistence;
using IIoT.EntityFrameworkCore.Repository;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class EmployeeActivationPersistenceTests
{
    [Fact]
    public async Task IdentityAccountStateCompareExchange_ShouldAdvanceConcurrencyAndRejectStaleBaseline()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        Guid employeeId;
        string originalConcurrencyStamp;
        IdentityAccountStateSnapshot baseline;
        using (var scope = provider.CreateScope())
        {
            var services = scope.ServiceProvider;
            var dbContext = services.GetRequiredService<IIoTDbContext>();
            var employee = TestIdentityData.AddEmployeeWithIdentity(
                dbContext,
                "E-ACTIVATE-CAS",
                "CAS User",
                accountEnabled: false,
                employeeActive: false);
            employeeId = employee.Id;
            var user = dbContext.Users.Local.Single(
                candidate => candidate.Id == employeeId);
            user.SecurityStamp = $"baseline-{Guid.NewGuid():N}";
            originalConcurrencyStamp = user.ConcurrencyStamp!;
            await dbContext.SaveChangesAsync();
            var store = new IdentityAccountStore(
                services.GetRequiredService<UserManager<ApplicationUser>>(),
                services.GetRequiredService<RoleManager<IdentityRole<Guid>>>());
            baseline = (await store.GetStateSnapshotAsync(employeeId))!;
            var targetStamp = $"target-{Guid.NewGuid():N}";

            var applied = await store.CompareExchangeStateAsync(
                employeeId,
                baseline,
                isEnabled: true,
                securityStamp: targetStamp);
            var stale = await store.CompareExchangeStateAsync(
                employeeId,
                baseline,
                isEnabled: false,
                securityStamp: $"stale-{Guid.NewGuid():N}");

            Assert.True(applied.IsSuccess);
            Assert.Equal(
                IdentityAccountCompareExchangeOutcome.Applied,
                applied.Value);
            Assert.True(stale.IsSuccess);
            Assert.Equal(
                IdentityAccountCompareExchangeOutcome.Conflict,
                stale.Value);

            dbContext.ChangeTracker.Clear();
            var persisted = await dbContext.Users
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == employeeId);
            Assert.True(persisted.IsEnabled);
            Assert.Equal(targetStamp, persisted.SecurityStamp);
            Assert.NotEqual(
                originalConcurrencyStamp,
                persisted.ConcurrencyStamp);
        }
    }

    [Fact]
    public async Task EmployeeMutationObservationReader_ShouldIgnoreTrackedStaleState()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var employee = TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            "E-ACTIVATE-OBSERVE",
            "Fresh Observer",
            accountEnabled: false,
            employeeActive: false);
        var employeeId = employee.Id;
        var trackedUser = dbContext.Users.Local.Single(
            candidate => candidate.Id == employeeId);
        trackedUser.SecurityStamp = "tracked-stamp";
        await dbContext.SaveChangesAsync();

        var dbContextOptions = GetOptions(dbContext);
        await using (var concurrentContext = new IIoTDbContext(dbContextOptions))
        {
            await concurrentContext.Employees
                .Where(candidate => candidate.Id == employeeId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        candidate => candidate.IsActive,
                        true));
            await concurrentContext.Users
                .Where(candidate => candidate.Id == employeeId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.IsEnabled, true)
                        .SetProperty(
                            candidate => candidate.SecurityStamp,
                            "fresh-stamp"));
        }

        Assert.False(employee.IsActive);
        Assert.False(trackedUser.IsEnabled);
        var observation = await new EmployeeMutationObservationReader(
                dbContextOptions)
            .ObserveAsync(employeeId, CancellationToken.None);

        Assert.True(observation.EmployeeExists);
        Assert.True(observation.EmployeeIsActive);
        Assert.True(observation.AccountExists);
        Assert.True(observation.AccountIsEnabled);
        Assert.Equal("fresh-stamp", observation.AccountSecurityStamp);
    }

    [Fact]
    public async Task ActivateEmployeeHandler_ShouldRollbackEmployeeWhenIdentityIsMissing()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        Guid employeeId;
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
            var employee = TestIdentityData.AddEmployeeWithIdentity(
                dbContext,
                "E-ACTIVATE-MISSING",
                "Missing Identity",
                accountEnabled: false,
                employeeActive: false);
            employeeId = employee.Id;
            await dbContext.SaveChangesAsync();

            var handler = CreateHandler(
                scope.ServiceProvider,
                dbContext,
                new StubHumanSessionRevocationService(),
                new RecordingIdentityAccountStore
                {
                    SetEnabledResult = Result.Success(false)
                });

            var result = await handler.Handle(
                new ActivateEmployeeCommand(employeeId),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
        }

        using var verificationScope = provider.CreateScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<IIoTDbContext>();
        var persisted = await verificationContext.Employees
            .AsNoTracking()
            .SingleAsync(employee => employee.Id == employeeId);
        Assert.False(persisted.IsActive);
    }

    [Fact]
    public async Task ActivateEmployeeHandler_ShouldRollbackBothStatesWhenSessionRevocationFails()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        Guid employeeId;
        using (var scope = provider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
            var employee = TestIdentityData.AddEmployeeWithIdentity(
                dbContext,
                "E-ACTIVATE-ROLLBACK",
                "Rollback User",
                accountEnabled: false,
                employeeActive: false);
            employeeId = employee.Id;
            await dbContext.SaveChangesAsync();

            var handler = CreateHandler(
                scope.ServiceProvider,
                dbContext,
                new StubHumanSessionRevocationService
                {
                    ExceptionToThrow = new InvalidOperationException("revocation failed")
                });

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.Handle(
                    new ActivateEmployeeCommand(employeeId),
                    CancellationToken.None));
        }

        using var verificationScope = provider.CreateScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<IIoTDbContext>();
        var persistedEmployee = await verificationContext.Employees
            .AsNoTracking()
            .SingleAsync(employee => employee.Id == employeeId);
        var persistedIdentity = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == employeeId);

        Assert.False(persistedEmployee.IsActive);
        Assert.False(persistedIdentity.IsEnabled);
    }

    [Fact]
    public async Task AlreadyActiveEmployee_ShouldRotateStatusVersionAndRevokeResidualSession()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        Guid employeeId;
        string originalSecurityStamp;
        using (var scope = provider.CreateScope())
        {
            var services = scope.ServiceProvider;
            var dbContext = services.GetRequiredService<IIoTDbContext>();
            var employee = TestIdentityData.AddEmployeeWithIdentity(
                dbContext,
                "E-ACTIVATE-IDEMPOTENT",
                "Already Active",
                accountEnabled: true,
                employeeActive: true);
            employeeId = employee.Id;
            var user = dbContext.Users.Local.Single(
                candidate => candidate.Id == employeeId);
            originalSecurityStamp = $"before-{Guid.NewGuid():N}";
            user.SecurityStamp = originalSecurityStamp;
            dbContext.RefreshTokenSessions.Add(new RefreshTokenSession
            {
                Id = Guid.NewGuid(),
                ActorType = IIoTClaimTypes.HumanActor,
                SubjectId = employeeId,
                TokenHash = $"activate-idempotent-{Guid.NewGuid():N}",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
            });
            await dbContext.SaveChangesAsync();

            var handler = CreateHandler(
                services,
                dbContext,
                new HumanSessionRevocationService(dbContext));

            var result = await handler.Handle(
                new ActivateEmployeeCommand(employeeId),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        using var verificationScope = provider.CreateScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<IIoTDbContext>();
        var persistedIdentity = await verificationContext.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == employeeId);
        var persistedSession = await verificationContext.RefreshTokenSessions
            .AsNoTracking()
            .SingleAsync(session => session.SubjectId == employeeId);

        Assert.NotEqual(originalSecurityStamp, persistedIdentity.SecurityStamp);
        Assert.NotNull(persistedSession.RevokedAtUtc);
        Assert.Equal(
            "employee-activated-relogin-required",
            persistedSession.RevokedReason);
    }

    private static ActivateEmployeeHandler CreateHandler(
        IServiceProvider serviceProvider,
        IIoTDbContext dbContext,
        IHumanSessionRevocationService sessionRevocationService,
        IIdentityAccountStore? identityAccountStore = null)
    {
        return new ActivateEmployeeHandler(
            new EfRepository<Employee>(dbContext),
            identityAccountStore ?? new IdentityAccountStore(
                serviceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                serviceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>()),
            new EfUnitOfWork(
                dbContext,
                NullLogger<EfUnitOfWork>.Instance),
            sessionRevocationService,
            new StubAdminTargetGuard(),
            new EmployeeMutationObservationReader(
                GetOptions(dbContext)));
    }

    private static DbContextOptions<IIoTDbContext> GetOptions(
        IIoTDbContext dbContext)
        => (DbContextOptions<IIoTDbContext>)
            dbContext.GetService<IDbContextOptions>();
}
