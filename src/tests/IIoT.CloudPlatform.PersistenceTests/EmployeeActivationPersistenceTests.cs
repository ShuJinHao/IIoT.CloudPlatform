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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class EmployeeActivationPersistenceTests
{
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
            new StubAdminTargetGuard());
    }
}
