using System.Data.Common;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Auditing;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Persistence;
using IIoT.EntityFrameworkCore.QueryServices;
using IIoT.EntityFrameworkCore.Repository;
using IIoT.EntityFrameworkCore.Uploads;
using IIoT.ProductionService.Commands.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Authorization;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class ProductionRetryTransactionPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    [Fact]
    public async Task ProductionRetryStrategy_ShouldReplayAllEmployeeWritesAccessAndDeviceDelete()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(90));
        var interceptor = new ThrowOnceBeforeCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);

        await VerifyOnboardRetryAsync(provider, interceptor, budget.Token);
        await VerifyProfileRetryAsync(provider, interceptor, budget.Token);
        await VerifyDeactivateRetryAsync(provider, interceptor, budget.Token);
        await VerifyActivateRetryAsync(provider, interceptor, budget.Token);
        await VerifyTerminateRetryAsync(provider, interceptor, budget.Token);
        await VerifyRoleRetryAsync(provider, interceptor, budget.Token);
        await VerifyEmployeeAccessRetryAsync(provider, interceptor, budget.Token);
        await VerifyDeviceDeleteRetryAsync(provider, interceptor, budget.Token);
        await VerifyEdgeRotationAsync(
            provider,
            interceptor.Arm,
            budget.Token);

        Assert.Equal(9, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task CommitConfirmationLoss_ShouldNotDuplicateOnboardActivateTerminateOrDeviceDelete()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(90));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);

        await VerifyOnboardCommitRecoveryAsync(provider, interceptor, budget.Token);
        await VerifyActivateCommitRecoveryAsync(provider, interceptor, budget.Token);
        await VerifyTerminateCommitRecoveryAsync(provider, interceptor, budget.Token);
        await VerifyDeviceDeleteCommitRecoveryAsync(provider, interceptor, budget.Token);
        await VerifyEdgeRotationAsync(
            provider,
            () => interceptor.Arm(),
            budget.Token);

        Assert.Equal(5, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task OnboardBlankRoleCommitConfirmationLoss_ShouldReturnOriginalSuccess()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);

        await VerifyOnboardCommitRecoveryAsync(
            provider,
            interceptor,
            budget.Token,
            useBlankRoleName: true);

        Assert.Equal(1, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task CallerCancellationDuringCommit_ShouldRollbackWithoutRetry()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            budget.Token);
        var interceptor = new CancelOnceBeforeCommitInterceptor(cancellation);
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "CANCEL",
            accountEnabled: true,
            employeeActive: true,
            withSession: false,
            budget.Token);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var identityStore = CreateIdentityStore(services);
        var handler = new UpdateEmployeeProfileHandler(
            new EfRepository<Employee>(dbContext),
            CreateUnitOfWork(dbContext),
            new AdminTargetGuard(identityStore));

        interceptor.Arm();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.Handle(
                new UpdateEmployeeProfileCommand(seed.EmployeeId, "Canceled Name"),
                cancellation.Token));

        Assert.Equal(1, interceptor.ExceptionsThrown);
        dbContext.ChangeTracker.Clear();
        var employee = await dbContext.Employees
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == seed.EmployeeId,
                budget.Token);
        Assert.Equal(seed.RealName, employee.RealName);
        Assert.False(dbContext.HasPendingDomainEvents);
    }

    [Fact]
    public async Task RolledBackDeleteAttempt_ShouldAuditImpactObservedByCommittingAttempt()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var interceptor = new AddDeviceLogBeforeDeleteRetryInterceptor(
            budget.ConnectionString);
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var (process, device) = CreateProcessAndDevice("IMPACT");
        process.ClearDomainEvents();
        device.ClearDomainEvents();
        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(budget.Token);
        dbContext.ChangeTracker.Clear();
        var audit = new RecordingAuditTrailService();
        var deletionService = new CapturingDeviceDeletionService(
            new EfDeviceDeletionDependencyService(dbContext));
        var handler = new DeleteDeviceHandler(
            HumanAdmin(),
            new EfRepository<Device>(dbContext),
            deletionService,
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            audit);

        interceptor.Arm(device.Id);
        var result = await handler.Handle(
            new DeleteDeviceCommand(device.Id),
            budget.Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal(1, interceptor.ExceptionsThrown);
        var deletion = Assert.IsType<DeviceCascadeDeletionResult>(
            deletionService.LastDeletionResult);
        Assert.Equal(1, deletion.Impact.DeviceLogs);
        Assert.Equal(1, deletion.Impact.TotalAssociatedRows);
        Assert.Contains(
            "\"device_logs\":1",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceDeleteCommitRecovery_ShouldAccumulateRepeatedLateSessionsBeforeSuccess()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var interceptor = new AddLateRefreshSessionAfterCommitInterceptor(
            budget.ConnectionString);
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seeded = await SeedDeviceWithAllDependenciesAsync(
            services,
            budget.Token);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var audit = new RecordingAuditTrailService();
        var deletionService = new CapturingDeviceDeletionService(
            new EfDeviceDeletionDependencyService(dbContext));
        var handler = new DeleteDeviceHandler(
            HumanAdmin(),
            new EfRepository<Device>(dbContext),
            deletionService,
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            audit);

        interceptor.Arm(seeded.DeviceId, commitLosses: 2);
        var result = await handler.Handle(
            new DeleteDeviceCommand(seeded.DeviceId),
            budget.Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal(2, interceptor.ExceptionsThrown);
        Assert.Equal(2, interceptor.LateSessionsInserted);
        var deletion = Assert.IsType<DeviceCascadeDeletionResult>(
            deletionService.LastDeletionResult);
        Assert.True(deletion.DeviceDeleted);
        Assert.Equal(14, deletion.Impact.TotalAssociatedRows);
        Assert.Equal(3, deletion.Impact.RefreshTokenSessions);
        Assert.Contains(
            "\"refresh_token_sessions\":3",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);

        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.Devices
            .AsNoTracking()
            .AnyAsync(
                device => device.Id == seeded.DeviceId,
                budget.Token));
        var remainingImpact = await new EfDeviceDeletionDependencyService(dbContext)
            .GetImpactAsync(seeded.DeviceId, budget.Token);
        Assert.Equal(0, remainingImpact.TotalAssociatedRows);
    }

    [Fact]
    public async Task RolledBackReplayCleanup_ShouldNotDoubleCountLateSession()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var interceptor = new AddLateRefreshSessionAfterCommitInterceptor(
            budget.ConnectionString);
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seeded = await SeedDeviceWithAllDependenciesAsync(
            services,
            budget.Token);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var audit = new RecordingAuditTrailService();
        var deletionService = new CapturingDeviceDeletionService(
            new EfDeviceDeletionDependencyService(dbContext));
        var handler = new DeleteDeviceHandler(
            HumanAdmin(),
            new EfRepository<Device>(dbContext),
            deletionService,
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            audit);

        interceptor.Arm(
            seeded.DeviceId,
            failFirstCleanupBeforeCommit: true);
        var result = await handler.Handle(
            new DeleteDeviceCommand(seeded.DeviceId),
            budget.Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal(2, interceptor.ExceptionsThrown);
        Assert.Equal(1, interceptor.LateSessionsInserted);
        var deletion = Assert.IsType<DeviceCascadeDeletionResult>(
            deletionService.LastDeletionResult);
        Assert.Equal(13, deletion.Impact.TotalAssociatedRows);
        Assert.Equal(2, deletion.Impact.RefreshTokenSessions);
        Assert.Contains(
            "\"refresh_token_sessions\":2",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);

        dbContext.ChangeTracker.Clear();
        var remainingImpact =
            await new EfDeviceDeletionDependencyService(dbContext)
                .GetImpactAsync(seeded.DeviceId, budget.Token);
        Assert.Equal(0, remainingImpact.TotalAssociatedRows);
    }

    [Fact]
    public async Task EdgeSessionIssueAfterStaleRead_ShouldWaitForDeleteAndLeaveNoOrphan()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var interceptor = new PauseOnceBeforeCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var bootstrapScope = provider.CreateAsyncScope();
        var bootstrapContext = bootstrapScope.ServiceProvider
            .GetRequiredService<IIoTDbContext>();
        var (process, device) = CreateProcessAndDevice("SESSIONRACE");
        process.ClearDomainEvents();
        device.ClearDomainEvents();
        bootstrapContext.MfgProcesses.Add(process);
        bootstrapContext.Devices.Add(device);
        await bootstrapContext.SaveChangesAsync(budget.Token);
        bootstrapContext.ChangeTracker.Clear();

        Assert.True(await bootstrapContext.Devices
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.Id == device.Id,
                budget.Token));

        await using var deletionScope = provider.CreateAsyncScope();
        var deletionContext = deletionScope.ServiceProvider
            .GetRequiredService<IIoTDbContext>();
        interceptor.Arm();
        var deletionTask = new EfDeviceDeletionDependencyService(
                deletionContext)
            .DeleteCascadeAsync(device.Id, budget.Token);
        await interceptor.WaitUntilCommitAsync(budget.Token);

        var refreshApplicationName =
            $"edge-session-issue-race-{Guid.NewGuid():N}";
        await using var refreshContext = CreateRetryContext(
            budget.ConnectionString,
            refreshApplicationName);
        var refreshTokenService = new EfRefreshTokenService(
            refreshContext,
            Options.Create(new RefreshTokenOptions()));
        var issueTask = refreshTokenService.IssueAsync(
            IIoTClaimTypes.EdgeDeviceActor,
            device.Id,
            budget.Token);
        try
        {
            await WaitForLockWaitAsync(
                budget.ConnectionString,
                refreshApplicationName,
                budget.Token);
            Assert.False(issueTask.IsCompleted);
        }
        finally
        {
            interceptor.Continue();
        }

        var deletionResult = await deletionTask;
        Assert.True(deletionResult.DeviceDeleted);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => issueTask);

        refreshContext.ChangeTracker.Clear();
        Assert.False(await refreshContext.RefreshTokenSessions
            .AsNoTracking()
            .AnyAsync(
                session =>
                    session.ActorType == IIoTClaimTypes.EdgeDeviceActor
                    && session.SubjectId == device.Id,
                budget.Token));
    }

    [Fact]
    public async Task EdgeSessionRotationAfterStaleRead_ShouldWaitForDeleteAndLeaveNoOrphan()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var interceptor = new PauseOnceBeforeCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var bootstrapScope = provider.CreateAsyncScope();
        var bootstrapContext = bootstrapScope.ServiceProvider
            .GetRequiredService<IIoTDbContext>();
        var (process, device) = CreateProcessAndDevice("ROTATERACE");
        process.ClearDomainEvents();
        device.ClearDomainEvents();
        bootstrapContext.MfgProcesses.Add(process);
        bootstrapContext.Devices.Add(device);
        await bootstrapContext.SaveChangesAsync(budget.Token);
        bootstrapContext.ChangeTracker.Clear();
        var refreshApplicationName =
            $"edge-session-rotate-race-{Guid.NewGuid():N}";
        await using var refreshContext = CreateRetryContext(
            budget.ConnectionString,
            refreshApplicationName);
        var refreshTokenService = new EfRefreshTokenService(
            refreshContext,
            Options.Create(new RefreshTokenOptions()));
        var issued = await refreshTokenService.IssueAsync(
            IIoTClaimTypes.EdgeDeviceActor,
            device.Id,
            budget.Token);
        refreshContext.ChangeTracker.Clear();

        await using var deletionScope = provider.CreateAsyncScope();
        var deletionContext = deletionScope.ServiceProvider
            .GetRequiredService<IIoTDbContext>();
        interceptor.Arm();
        var deletionTask = new EfDeviceDeletionDependencyService(
                deletionContext)
            .DeleteCascadeAsync(device.Id, budget.Token);
        await interceptor.WaitUntilCommitAsync(budget.Token);

        var rotationTask = refreshTokenService.RotateAsync(
            IIoTClaimTypes.EdgeDeviceActor,
            issued.Token,
            budget.Token);
        try
        {
            await WaitForLockWaitAsync(
                budget.ConnectionString,
                refreshApplicationName,
                budget.Token);
            Assert.False(rotationTask.IsCompleted);
        }
        finally
        {
            interceptor.Continue();
        }

        var deletionResult = await deletionTask;
        Assert.True(deletionResult.DeviceDeleted);
        var rotationResult = await rotationTask;
        Assert.False(rotationResult.IsSuccess);

        refreshContext.ChangeTracker.Clear();
        Assert.False(await refreshContext.RefreshTokenSessions
            .AsNoTracking()
            .AnyAsync(
                session =>
                    session.ActorType == IIoTClaimTypes.EdgeDeviceActor
                    && session.SubjectId == device.Id,
                budget.Token));
    }

    private static async Task VerifyOnboardRetryAsync(
        ServiceProvider provider,
        ThrowOnceBeforeCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var unique = Guid.NewGuid().ToString("N");
        var employeeNo = $"TX-ON-{unique}"[..24];
        var roleName = $"TxOn{unique}"[..28];
        await CreateRoleAsync(services, roleName);
        var handler = CreateOnboardHandler(services);

        interceptor.Arm();
        var result = await handler.Handle(
            new OnboardEmployeeCommand(
                employeeNo,
                "Retry Onboard",
                "Retry123!",
                roleName),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            1,
            await dbContext.Employees.CountAsync(
                employee => employee.EmployeeNo == employeeNo,
                cancellationToken));
        Assert.Equal(
            1,
            await dbContext.Users.CountAsync(
                user => user.Id == result.Value,
                cancellationToken));
        Assert.Equal(
            [roleName],
            await CreateIdentityStore(services).GetRolesAsync(
                result.Value,
                cancellationToken));
    }

    private static async Task VerifyProfileRetryAsync(
        ServiceProvider provider,
        ThrowOnceBeforeCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "PROFILE",
            accountEnabled: true,
            employeeActive: true,
            withSession: false,
            cancellationToken);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var identityStore = CreateIdentityStore(services);
        var handler = new UpdateEmployeeProfileHandler(
            new EfRepository<Employee>(dbContext),
            CreateUnitOfWork(dbContext),
            new AdminTargetGuard(identityStore));

        interceptor.Arm();
        var result = await handler.Handle(
            new UpdateEmployeeProfileCommand(seed.EmployeeId, "Retry Profile Updated"),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        var employee = await dbContext.Employees
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == seed.EmployeeId,
                cancellationToken);
        Assert.Equal("Retry Profile Updated", employee.RealName);
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "EmployeeRenamedDomainEvent",
                seed.EmployeeId,
                cancellationToken));
    }

    private static async Task VerifyDeactivateRetryAsync(
        ServiceProvider provider,
        ThrowOnceBeforeCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "DEACT",
            accountEnabled: true,
            employeeActive: true,
            withSession: true,
            cancellationToken);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var identityStore = CreateIdentityStore(services);
        var handler = new DeactivateEmployeeHandler(
            new EfRepository<Employee>(dbContext),
            identityStore,
            CreateUnitOfWork(dbContext),
            new HumanSessionRevocationService(dbContext),
            new AdminTargetGuard(identityStore));

        interceptor.Arm();
        var result = await handler.Handle(
            new DeactivateEmployeeCommand(seed.EmployeeId),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.False((await dbContext.Employees
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == seed.EmployeeId,
                cancellationToken)).IsActive);
        Assert.False((await dbContext.Users
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == seed.EmployeeId,
                cancellationToken)).IsEnabled);
        Assert.False(await HasActiveHumanSessionAsync(
            dbContext,
            seed.EmployeeId,
            cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "EmployeeDeactivatedDomainEvent",
                seed.EmployeeId,
                cancellationToken));
    }

    private static async Task VerifyActivateRetryAsync(
        ServiceProvider provider,
        ThrowOnceBeforeCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "ACT",
            accountEnabled: false,
            employeeActive: false,
            withSession: true,
            cancellationToken);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var identityStore = CreateIdentityStore(services);
        var handler = new ActivateEmployeeHandler(
            new EfRepository<Employee>(dbContext),
            identityStore,
            CreateUnitOfWork(dbContext),
            new HumanSessionRevocationService(dbContext),
            new AdminTargetGuard(identityStore));

        interceptor.Arm();
        var result = await handler.Handle(
            new ActivateEmployeeCommand(seed.EmployeeId),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.True((await dbContext.Employees
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == seed.EmployeeId,
                cancellationToken)).IsActive);
        Assert.True((await dbContext.Users
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == seed.EmployeeId,
                cancellationToken)).IsEnabled);
        Assert.False(await HasActiveHumanSessionAsync(
            dbContext,
            seed.EmployeeId,
            cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "EmployeeActivatedDomainEvent",
                seed.EmployeeId,
                cancellationToken));
    }

    private static async Task VerifyTerminateRetryAsync(
        ServiceProvider provider,
        ThrowOnceBeforeCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "TERM",
            accountEnabled: true,
            employeeActive: true,
            withSession: true,
            cancellationToken);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var identityStore = CreateIdentityStore(services);
        var handler = new TerminateEmployeeHandler(
            new EfRepository<Employee>(dbContext),
            identityStore,
            CreateUnitOfWork(dbContext),
            new HumanSessionRevocationService(dbContext),
            new AdminTargetGuard(identityStore));

        interceptor.Arm();
        var result = await handler.Handle(
            new TerminateEmployeeCommand(seed.EmployeeId),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.Employees
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.Id == seed.EmployeeId,
                cancellationToken));
        Assert.False(await dbContext.Users
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.Id == seed.EmployeeId,
                cancellationToken));
        Assert.False(await HasActiveHumanSessionAsync(
            dbContext,
            seed.EmployeeId,
            cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "EmployeeTerminatedDomainEvent",
                seed.EmployeeId,
                cancellationToken));
    }

    private static async Task VerifyRoleRetryAsync(
        ServiceProvider provider,
        ThrowOnceBeforeCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "ROLE",
            accountEnabled: true,
            employeeActive: true,
            withSession: true,
            cancellationToken);
        var unique = Guid.NewGuid().ToString("N");
        var oldRole = $"TxOld{unique}"[..30];
        var newRole = $"TxNew{unique}"[..30];
        await CreateRoleAsync(services, oldRole);
        await CreateRoleAsync(services, newRole);
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(seed.EmployeeId.ToString()))!;
        Assert.True((await userManager.AddToRoleAsync(user, oldRole)).Succeeded);
        var originalStamp = user.SecurityStamp;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        dbContext.ChangeTracker.Clear();
        var identityStore = CreateIdentityStore(services);
        var audit = new RecordingAuditTrailService();
        var handler = new UpdateEmployeeRoleHandler(
            identityStore,
            CreateRolePolicyService(services),
            CreateUnitOfWork(dbContext),
            new HumanSessionRevocationService(dbContext),
            new AdminTargetGuard(identityStore),
            new EmployeeLookupService(dbContext),
            HumanAdmin(),
            audit);

        interceptor.Arm();
        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(seed.EmployeeId, newRole),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            [newRole],
            await identityStore.GetRolesAsync(seed.EmployeeId, cancellationToken));
        var updatedUser = await dbContext.Users
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == seed.EmployeeId,
                cancellationToken);
        Assert.NotEqual(originalStamp, updatedUser.SecurityStamp);
        Assert.False(await HasActiveHumanSessionAsync(
            dbContext,
            seed.EmployeeId,
            cancellationToken));
        var auditEntry = Assert.Single(audit.Entries);
        Assert.True(auditEntry.Succeeded);
        Assert.Contains(
            "\"resultCode\":\"Succeeded\"",
            auditEntry.Summary,
            StringComparison.Ordinal);
    }

    private static async Task VerifyEmployeeAccessRetryAsync(
        ServiceProvider provider,
        ThrowOnceBeforeCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "ACCESS",
            accountEnabled: true,
            employeeActive: true,
            withSession: false,
            cancellationToken);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var (process, device) = CreateProcessAndDevice("ACCESS");
        process.ClearDomainEvents();
        device.ClearDomainEvents();
        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        var identityStore = CreateIdentityStore(services);
        var handler = new UpdateEmployeeAccessHandler(
            new EfRepository<Employee>(dbContext),
            new AdminTargetGuard(identityStore),
            new DeviceReadQueryService(dbContext),
            CreateUnitOfWork(dbContext));

        interceptor.Arm();
        var result = await handler.Handle(
            new UpdateEmployeeAccessCommand(seed.EmployeeId, [device.Id, device.Id]),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            1,
            await dbContext.Set<EmployeeDeviceAccess>()
                .AsNoTracking()
                .CountAsync(
                    access =>
                        access.EmployeeId == seed.EmployeeId
                        && access.DeviceId == device.Id,
                    cancellationToken));
    }

    private static async Task VerifyDeviceDeleteRetryAsync(
        ServiceProvider provider,
        ThrowOnceBeforeCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var (process, device) = CreateProcessAndDevice("DELETE");
        process.ClearDomainEvents();
        device.ClearDomainEvents();
        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        var audit = new RecordingAuditTrailService();
        var handler = new DeleteDeviceHandler(
            HumanAdmin(),
            new EfRepository<Device>(dbContext),
            new EfDeviceDeletionDependencyService(dbContext),
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            audit);

        interceptor.Arm();
        var result = await handler.Handle(
            new DeleteDeviceCommand(device.Id),
            cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Single(audit.Entries);
        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.Devices
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.Id == device.Id,
                cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "DeviceDeletedDomainEvent",
                device.Id,
                cancellationToken));
    }

    private static async Task VerifyEdgeRotationAsync(
        ServiceProvider provider,
        Action armCommitFailure,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider
            .GetRequiredService<IIoTDbContext>();
        var (process, device) = CreateProcessAndDevice("ROTATERETRY");
        process.ClearDomainEvents();
        device.ClearDomainEvents();
        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        var refreshTokenService = new EfRefreshTokenService(
            dbContext,
            Options.Create(new RefreshTokenOptions()));
        var issued = await refreshTokenService.IssueAsync(
            IIoTClaimTypes.EdgeDeviceActor,
            device.Id,
            cancellationToken);
        dbContext.ChangeTracker.Clear();

        armCommitFailure();
        var result = await refreshTokenService.RotateAsync(
            IIoTClaimTypes.EdgeDeviceActor,
            issued.Token,
            cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(device.Id, result.Value!.SubjectId);
        dbContext.ChangeTracker.Clear();
        var sessions = await dbContext.RefreshTokenSessions
            .AsNoTracking()
            .Where(session =>
                session.ActorType == IIoTClaimTypes.EdgeDeviceActor
                && session.SubjectId == device.Id)
            .ToListAsync(cancellationToken);
        Assert.Equal(2, sessions.Count);
        var original = Assert.Single(
            sessions,
            session => session.RevokedReason == "rotated");
        var replacement = Assert.Single(
            sessions,
            session => !session.RevokedAtUtc.HasValue);
        Assert.Equal(replacement.Id, original.ReplacedByTokenId);
    }

    private static async Task VerifyOnboardCommitRecoveryAsync(
        ServiceProvider provider,
        ThrowOnceAfterCommitInterceptor interceptor,
        CancellationToken cancellationToken,
        bool useBlankRoleName = false)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var unique = Guid.NewGuid().ToString("N");
        var employeeNo = $"ACK-ON-{unique}"[..24];
        var roleName = useBlankRoleName
            ? "   "
            : $"AckOn{unique}"[..28];
        if (!useBlankRoleName)
        {
            await CreateRoleAsync(services, roleName);
        }

        var handler = CreateOnboardHandler(services);

        interceptor.Arm();
        var result = await handler.Handle(
            new OnboardEmployeeCommand(
                employeeNo,
                "Commit Recovery Onboard",
                "Retry123!",
                roleName),
            cancellationToken);

        Assert.True(result.IsSuccess);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            1,
            await dbContext.Employees.CountAsync(
                employee => employee.Id == result.Value,
                cancellationToken));
        Assert.Equal(
            1,
            await dbContext.Users.CountAsync(
                user => user.Id == result.Value,
                cancellationToken));
        string[] expectedRoles = useBlankRoleName ? [] : [roleName];
        Assert.Equal(
            expectedRoles,
            await CreateIdentityStore(services).GetRolesAsync(
                result.Value,
                cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "EmployeeOnboardedDomainEvent",
                result.Value,
                cancellationToken));
    }

    private static async Task VerifyTerminateCommitRecoveryAsync(
        ServiceProvider provider,
        ThrowOnceAfterCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "ACKTERM",
            accountEnabled: true,
            employeeActive: true,
            withSession: true,
            cancellationToken);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var identityStore = CreateIdentityStore(services);
        var handler = new TerminateEmployeeHandler(
            new EfRepository<Employee>(dbContext),
            identityStore,
            CreateUnitOfWork(dbContext),
            new HumanSessionRevocationService(dbContext),
            new AdminTargetGuard(identityStore));

        interceptor.Arm();
        var result = await handler.Handle(
            new TerminateEmployeeCommand(seed.EmployeeId),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.Employees
            .AsNoTracking()
            .AnyAsync(
                employee => employee.Id == seed.EmployeeId,
                cancellationToken));
        Assert.False(await dbContext.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == seed.EmployeeId,
                cancellationToken));
        Assert.False(await HasActiveHumanSessionAsync(
            dbContext,
            seed.EmployeeId,
            cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "EmployeeTerminatedDomainEvent",
                seed.EmployeeId,
                cancellationToken));
    }

    private static async Task VerifyActivateCommitRecoveryAsync(
        ServiceProvider provider,
        ThrowOnceAfterCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "ACKACT",
            accountEnabled: true,
            employeeActive: true,
            withSession: true,
            cancellationToken);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var originalStamp = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == seed.EmployeeId)
            .Select(user => user.SecurityStamp)
            .SingleAsync(cancellationToken);
        var identityStore = CreateIdentityStore(services);
        var handler = new ActivateEmployeeHandler(
            new EfRepository<Employee>(dbContext),
            identityStore,
            CreateUnitOfWork(dbContext),
            new HumanSessionRevocationService(dbContext),
            new AdminTargetGuard(identityStore));
        string? committedStamp = null;

        interceptor.Arm(async callbackCancellationToken =>
        {
            await using var connection = new NpgsqlConnection(
                dbContext.Database.GetConnectionString());
            await connection.OpenAsync(callbackCancellationToken);
            await using var command = new NpgsqlCommand(
                """
                select "SecurityStamp"
                from "AspNetUsers"
                where "Id" = @employee_id
                """,
                connection);
            command.Parameters.AddWithValue(
                "employee_id",
                seed.EmployeeId);
            committedStamp = (string?)await command.ExecuteScalarAsync(
                callbackCancellationToken);
        });
        var result = await handler.Handle(
            new ActivateEmployeeCommand(seed.EmployeeId),
            cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(committedStamp));
        dbContext.ChangeTracker.Clear();
        var persistedStamp = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == seed.EmployeeId)
            .Select(user => user.SecurityStamp)
            .SingleAsync(cancellationToken);
        Assert.NotEqual(originalStamp, persistedStamp);
        Assert.Equal(committedStamp, persistedStamp);
        Assert.False(await HasActiveHumanSessionAsync(
            dbContext,
            seed.EmployeeId,
            cancellationToken));
    }

    private static async Task VerifyDeviceDeleteCommitRecoveryAsync(
        ServiceProvider provider,
        ThrowOnceAfterCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seeded = await SeedDeviceWithAllDependenciesAsync(
            services,
            cancellationToken);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var audit = new RecordingAuditTrailService();
        var capturingDeletionService = new CapturingDeviceDeletionService(
            new EfDeviceDeletionDependencyService(dbContext));
        var handler = new DeleteDeviceHandler(
            HumanAdmin(),
            new EfRepository<Device>(dbContext),
            capturingDeletionService,
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            audit);

        interceptor.Arm();
        var result = await handler.Handle(
            new DeleteDeviceCommand(seeded.DeviceId),
            cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        var deletion = Assert.IsType<DeviceCascadeDeletionResult>(
            capturingDeletionService.LastDeletionResult);
        Assert.True(deletion.DeviceDeleted);
        Assert.Equal(12, deletion.Impact.TotalAssociatedRows);
        Assert.All(
            new[]
            {
                deletion.Impact.Recipes,
                deletion.Impact.Capacities,
                deletion.Impact.DeviceLogs,
                deletion.Impact.PassStations,
                deletion.Impact.ClientStates,
                deletion.Impact.ClientVersionSnapshots,
                deletion.Impact.ClientPluginVersions,
                deletion.Impact.RuntimeHeartbeats,
                deletion.Impact.UploadReceiveRegistrations,
                deletion.Impact.EmployeeDeviceAccesses,
                deletion.Impact.RefreshTokenSessions,
                deletion.Impact.EdgeHostPlcRuntimeStates
            },
            count => Assert.Equal(1, count));
        var auditEntry = Assert.Single(audit.Entries);
        Assert.True(auditEntry.Succeeded);
        Assert.Contains(
            "\"edge_host_plc_runtime_states\":1",
            auditEntry.Summary,
            StringComparison.Ordinal);

        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.Devices
            .AsNoTracking()
            .AnyAsync(
                device => device.Id == seeded.DeviceId,
                cancellationToken));
        var remainingImpact = await new EfDeviceDeletionDependencyService(dbContext)
            .GetImpactAsync(seeded.DeviceId, cancellationToken);
        Assert.Equal(0, remainingImpact.TotalAssociatedRows);
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "DeviceDeletedDomainEvent",
                seeded.DeviceId,
                cancellationToken));
    }

    private static OnboardEmployeeHandler CreateOnboardHandler(
        IServiceProvider services)
    {
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        return new OnboardEmployeeHandler(
            CreateIdentityStore(services),
            new IdentityPasswordService(
                services.GetRequiredService<UserManager<ApplicationUser>>()),
            CreateRolePolicyService(services),
            new EfRepository<Employee>(dbContext),
            CreateUnitOfWork(dbContext),
            HumanAdmin(),
            new RecordingPermissionProvider());
    }

    private static IdentityAccountStore CreateIdentityStore(
        IServiceProvider services)
        => new(
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<RoleManager<IdentityRole<Guid>>>());

    private static RolePolicyService CreateRolePolicyService(
        IServiceProvider services)
        => new(
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<RoleManager<IdentityRole<Guid>>>());

    private static EfUnitOfWork CreateUnitOfWork(IIoTDbContext dbContext)
        => new(dbContext, NullLogger<EfUnitOfWork>.Instance);

    private static async Task CreateRoleAsync(
        IServiceProvider services,
        string roleName)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        Assert.True((await roleManager.CreateAsync(
            new IdentityRole<Guid>(roleName))).Succeeded);
    }

    private static async Task<SeededEmployee> SeedEmployeeAsync(
        IServiceProvider services,
        string prefix,
        bool accountEnabled,
        bool employeeActive,
        bool withSession,
        CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var unique = Guid.NewGuid().ToString("N");
        var employeeNo = $"TX-{prefix}-{unique}"[..Math.Min(24, 4 + prefix.Length + unique.Length)];
        var realName = $"Retry {prefix}";
        var employee = TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            employeeNo,
            realName,
            accountEnabled,
            employeeActive);
        dbContext.Users.Local
            .Single(user => user.Id == employee.Id)
            .SecurityStamp = Guid.NewGuid().ToString("N");
        employee.ClearDomainEvents();
        if (withSession)
        {
            dbContext.RefreshTokenSessions.Add(new RefreshTokenSession
            {
                Id = Guid.NewGuid(),
                ActorType = IIoTClaimTypes.HumanActor,
                SubjectId = employee.Id,
                TokenHash = $"tx-{prefix}-{Guid.NewGuid():N}",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return new SeededEmployee(employee.Id, employeeNo, realName);
    }

    private static (MfgProcess Process, Device Device) CreateProcessAndDevice(
        string prefix)
    {
        var unique = Guid.NewGuid().ToString("N");
        var process = new MfgProcess(
            $"{prefix}-{unique}"[..24],
            $"Retry {prefix}");
        var device = new Device(
            $"Retry device {prefix} {unique}",
            $"{prefix}-{unique}"[..24],
            process.Id);
        return (process, device);
    }

    private static async Task<SeededDevice> SeedDeviceWithAllDependenciesAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var (process, device) = CreateProcessAndDevice("ACKDEL");
        var employee = TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            $"TX-ACKDEL-{Guid.NewGuid():N}"[..24],
            "Device dependency employee");
        employee.AddDeviceAccess(device.Id);
        employee.ClearDomainEvents();
        process.ClearDomainEvents();
        device.ClearDomainEvents();

        var recipe = new Recipe(
            $"Retry recipe {Guid.NewGuid():N}",
            process.Id,
            device.Id,
            "{}");
        recipe.ClearDomainEvents();
        var plugin = new DeviceClientPluginVersion(
            "cp",
            "CP",
            "1.0.0",
            "1",
            true);
        var snapshot = new DeviceClientVersionSnapshot(
            device.Id,
            device.Code,
            "1.0.0",
            "1",
            "stable",
            DateTime.UtcNow,
            [plugin]);
        var clientState = new DeviceClientState(device.Id, device.Code);
        var runtimeHeartbeat = new EdgeDeviceRuntimeHeartbeat(
            device.Id,
            device.Code,
            $"runtime-{Guid.NewGuid():N}",
            "cp",
            "1.0.0",
            "1",
            "Running",
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow);
        var plcState = new EdgeHostPlcRuntimeState(
            device.Id,
            device.Code,
            "PLC1");
        plcState.ReplaceReport(
            "PLC 1",
            true,
            EdgeHostPlcRuntimeStatus.Connected,
            DateTime.UtcNow);

        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.Add(device);
        dbContext.Recipes.Add(recipe);
        dbContext.DeviceClientVersionSnapshots.Add(snapshot);
        dbContext.DeviceClientStates.Add(clientState);
        dbContext.EdgeDeviceRuntimeHeartbeats.Add(runtimeHeartbeat);
        dbContext.EdgeHostPlcRuntimeStates.Add(plcState);
        dbContext.UploadReceiveRegistrations.Add(
            UploadReceiveRegistration.Create(
                device.Id,
                "tx-retry",
                null,
                $"tx-{Guid.NewGuid():N}",
                Guid.NewGuid()));
        dbContext.RefreshTokenSessions.Add(new RefreshTokenSession
        {
            Id = Guid.NewGuid(),
            ActorType = IIoTClaimTypes.EdgeDeviceActor,
            SubjectId = device.Id,
            TokenHash = $"tx-edge-{Guid.NewGuid():N}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        var passPayload = """{"plcCode":"PLC1","plcName":"PLC 1"}""";
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            insert into hourly_capacity
            (
                id, device_id, date, shift_code, hour, minute, time_label,
                total_count, ok_count, ng_count, plc_name, reported_at
            )
            values
            (
                {Guid.NewGuid()}, {device.Id}, current_date, 'D', 1, 0, '01:00',
                1, 1, 0, 'PLC 1', now()
            );

            insert into device_logs
            (
                id, device_id, level, message, log_time, received_at,
                idempotency_key
            )
            values
            (
                {Guid.NewGuid()}, {device.Id}, 'Info', 'retry delete',
                now(), now(), {Guid.NewGuid().ToString("N")}
            );

            insert into pass_station_records
            (
                id, device_id, type_key, barcode, cell_result,
                completed_time, received_at, deduplication_key, payload_jsonb
            )
            values
            (
                {Guid.NewGuid()}, {device.Id}, 'cp', 'TX-RETRY', 'OK',
                localtimestamp, localtimestamp, {Guid.NewGuid().ToString("N")},
                cast({passPayload} as jsonb)
            );
            """, cancellationToken);
        dbContext.ChangeTracker.Clear();
        var impact = await new EfDeviceDeletionDependencyService(dbContext)
            .GetImpactAsync(device.Id, cancellationToken);
        Assert.Equal(12, impact.TotalAssociatedRows);
        return new SeededDevice(device.Id);
    }

    private static async Task<int> CountOutboxAsync(
        IIoTDbContext dbContext,
        string eventType,
        Guid aggregateId,
        CancellationToken cancellationToken)
    {
        var aggregateIdText = aggregateId.ToString();
        var payloads = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.EventType.Contains(eventType))
            .Select(message => message.Payload)
            .ToListAsync(
                cancellationToken);

        return payloads.Count(payload =>
            payload.Contains(aggregateIdText, StringComparison.OrdinalIgnoreCase));
    }

    private static Task<bool> HasActiveHumanSessionAsync(
        IIoTDbContext dbContext,
        Guid employeeId,
        CancellationToken cancellationToken)
        => dbContext.RefreshTokenSessions
            .AsNoTracking()
            .AnyAsync(
                session =>
                    session.ActorType == IIoTClaimTypes.HumanActor
                    && session.SubjectId == employeeId
                    && !session.RevokedAtUtc.HasValue,
                cancellationToken);

    private static ServiceProvider CreateRetryProvider(
        string connectionString,
        DbTransactionInterceptor interceptor)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<IIoTDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql.EnableRetryOnFailure(
                        3,
                        TimeSpan.FromMilliseconds(50),
                        null))
                .AddInterceptors(interceptor));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IIoTDbContext>();
        return services.BuildServiceProvider();
    }

    private static IIoTDbContext CreateRetryContext(
        string connectionString,
        string applicationName)
    {
        var namedConnectionString = new NpgsqlConnectionStringBuilder(
            connectionString)
        {
            ApplicationName = applicationName
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(
                namedConnectionString,
                npgsql => npgsql.EnableRetryOnFailure(
                    3,
                    TimeSpan.FromMilliseconds(50),
                    null))
            .Options;
        return new IIoTDbContext(options);
    }

    private static async Task WaitForLockWaitAsync(
        string connectionString,
        string applicationName,
        CancellationToken testToken)
    {
        using var readinessTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(testToken);
        readinessTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var readinessToken = readinessTimeout.Token;
        var observerConnectionString = new NpgsqlConnectionStringBuilder(
            connectionString)
        {
            ApplicationName =
                $"device-session-lock-observer-{Guid.NewGuid():N}"
        }.ConnectionString;

        try
        {
            await using var observer = new NpgsqlConnection(
                observerConnectionString);
            await observer.OpenAsync(readinessToken);
            while (true)
            {
                await using var command = new NpgsqlCommand(
                    """
                    SELECT EXISTS (
                        SELECT 1
                        FROM pg_stat_activity AS activity
                        WHERE activity.application_name = @application_name
                          AND activity.state = 'active'
                          AND activity.wait_event_type = 'Lock'
                          AND EXISTS (
                              SELECT 1
                              FROM pg_locks AS waiting_lock
                              WHERE waiting_lock.pid = activity.pid
                                AND NOT waiting_lock.granted
                          )
                    )
                    """,
                    observer);
                command.Parameters.AddWithValue(
                    "application_name",
                    applicationName);
                if (await command.ExecuteScalarAsync(readinessToken) is true)
                {
                    return;
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(25),
                    readinessToken);
            }
        }
        catch (OperationCanceledException)
            when (!testToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Edge session operation '{applicationName}' did not enter a PostgreSQL lock wait within 10 seconds.");
        }
    }

    private static ICurrentUser HumanAdmin()
        => new HumanCurrentUser(Guid.NewGuid());

    private sealed class HumanCurrentUser(Guid id) : ICurrentUser
    {
        public string Id => id.ToString();

        public string UserName => $"tx-admin-{id:N}";

        public IReadOnlyCollection<string> Roles => [SystemRoles.Admin];

        public string ActorType => IIoTClaimTypes.HumanActor;

        public IReadOnlyCollection<string> Permissions => [];

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

    private sealed class CapturingDeviceDeletionService(
        IDeviceDeletionDependencyQueryService inner)
        : IDeviceDeletionDependencyQueryService
    {
        public DeviceCascadeDeletionResult? LastDeletionResult { get; private set; }

        public Task<DeviceDeletionDependencies> GetDependenciesAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
            => inner.GetDependenciesAsync(deviceId, cancellationToken);

        public Task<DeviceDeletionImpact> GetImpactAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
            => inner.GetImpactAsync(deviceId, cancellationToken);

        public async Task<DeviceCascadeDeletionResult> DeleteCascadeAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            LastDeletionResult = await inner.DeleteCascadeAsync(
                deviceId,
                cancellationToken);
            return LastDeletionResult;
        }
    }

    private sealed class ThrowOnceBeforeCommitInterceptor : DbTransactionInterceptor
    {
        private int armed;
        private int exceptionsThrown;

        public int ExceptionsThrown => Volatile.Read(ref exceptionsThrown);

        public void Arm() => Volatile.Write(ref armed, 1);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                Interlocked.Increment(ref exceptionsThrown);
                throw RetryablePostgresException(
                    "simulated transient before commit");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowOnceAfterCommitInterceptor : DbTransactionInterceptor
    {
        private int armed;
        private int exceptionsThrown;
        private Func<CancellationToken, Task>? afterCommit;

        public int ExceptionsThrown => Volatile.Read(ref exceptionsThrown);

        public void Arm(
            Func<CancellationToken, Task>? afterCommitAction = null)
        {
            afterCommit = afterCommitAction;
            Volatile.Write(ref armed, 1);
        }

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                var callback = afterCommit;
                afterCommit = null;
                if (callback is not null)
                {
                    await callback(cancellationToken);
                }

                Interlocked.Increment(ref exceptionsThrown);
                throw RetryablePostgresException(
                    "simulated commit confirmation loss");
            }
        }
    }

    private sealed class AddDeviceLogBeforeDeleteRetryInterceptor(
        string connectionString)
        : DbTransactionInterceptor
    {
        private int armed;
        private int insertBeforeNextTransaction;
        private int exceptionsThrown;
        private Guid deviceId;

        public int ExceptionsThrown => Volatile.Read(ref exceptionsThrown);

        public void Arm(Guid targetDeviceId)
        {
            deviceId = targetDeviceId;
            Volatile.Write(ref armed, 1);
        }

        public override async ValueTask<InterceptionResult<DbTransaction>>
            TransactionStartingAsync(
                DbConnection connection,
                TransactionStartingEventData eventData,
                InterceptionResult<DbTransaction> result,
                CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(
                    ref insertBeforeNextTransaction,
                    0,
                    1) == 1)
            {
                await using var insertionConnection =
                    new NpgsqlConnection(connectionString);
                await insertionConnection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand(
                    """
                    insert into device_logs
                    (
                        id, device_id, level, message, log_time, received_at,
                        idempotency_key
                    )
                    values
                    (
                        @id, @device_id, 'Info', 'added between retry attempts',
                        now(), now(), @idempotency_key
                    )
                    """,
                    insertionConnection);
                command.Parameters.AddWithValue("id", Guid.NewGuid());
                command.Parameters.AddWithValue("device_id", deviceId);
                command.Parameters.AddWithValue(
                    "idempotency_key",
                    Guid.NewGuid().ToString("N"));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                Volatile.Write(ref insertBeforeNextTransaction, 1);
                Interlocked.Increment(ref exceptionsThrown);
                throw RetryablePostgresException(
                    "simulated transient before delete commit");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class AddLateRefreshSessionAfterCommitInterceptor(
        string connectionString)
        : DbTransactionInterceptor
    {
        private int remainingCommitLosses;
        private int armCleanupFailureAfterCommitLoss;
        private int failCleanupBeforeCommit;
        private int exceptionsThrown;
        private int lateSessionsInserted;
        private Guid deviceId;

        public int ExceptionsThrown => Volatile.Read(ref exceptionsThrown);

        public int LateSessionsInserted => Volatile.Read(
            ref lateSessionsInserted);

        public void Arm(
            Guid targetDeviceId,
            int commitLosses = 1,
            bool failFirstCleanupBeforeCommit = false)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(
                commitLosses,
                1);
            deviceId = targetDeviceId;
            Volatile.Write(
                ref remainingCommitLosses,
                commitLosses);
            Volatile.Write(
                ref armCleanupFailureAfterCommitLoss,
                failFirstCleanupBeforeCommit ? 1 : 0);
        }

        public override ValueTask<InterceptionResult>
            TransactionCommittingAsync(
                DbTransaction transaction,
                TransactionEventData eventData,
                InterceptionResult result,
                CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(
                    ref failCleanupBeforeCommit,
                    0,
                    1) == 1)
            {
                Interlocked.Increment(ref exceptionsThrown);
                throw RetryablePostgresException(
                    "simulated transient before replay cleanup commit");
            }

            return ValueTask.FromResult(result);
        }

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            while (true)
            {
                var remaining = Volatile.Read(
                    ref remainingCommitLosses);
                if (remaining <= 0)
                {
                    return;
                }

                if (Interlocked.CompareExchange(
                        ref remainingCommitLosses,
                        remaining - 1,
                        remaining) == remaining)
                {
                    break;
                }
            }

            await using var insertionConnection =
                new NpgsqlConnection(connectionString);
            await insertionConnection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                insert into refresh_token_sessions
                (
                    "Id", "ActorType", "SubjectId", "TokenHash",
                    "CreatedAtUtc", "ExpiresAtUtc"
                )
                values
                (
                    @id, @actor_type, @subject_id, @token_hash,
                    now(), now() + interval '1 hour'
                )
                """,
                insertionConnection);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue(
                "actor_type",
                IIoTClaimTypes.EdgeDeviceActor);
            command.Parameters.AddWithValue("subject_id", deviceId);
            command.Parameters.AddWithValue(
                "token_hash",
                $"tx-late-edge-{Guid.NewGuid():N}");
            await command.ExecuteNonQueryAsync(cancellationToken);

            Interlocked.Increment(ref lateSessionsInserted);
            Interlocked.Increment(ref exceptionsThrown);
            if (Interlocked.CompareExchange(
                    ref armCleanupFailureAfterCommitLoss,
                    0,
                    1) == 1)
            {
                Volatile.Write(ref failCleanupBeforeCommit, 1);
            }

            throw RetryablePostgresException(
                "simulated commit confirmation loss after late edge session");
        }
    }

    private sealed class CancelOnceBeforeCommitInterceptor(
        CancellationTokenSource cancellation) : DbTransactionInterceptor
    {
        private int armed;
        private int exceptionsThrown;

        public int ExceptionsThrown => Volatile.Read(ref exceptionsThrown);

        public void Arm() => Volatile.Write(ref armed, 1);

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                Interlocked.Increment(ref exceptionsThrown);
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class PauseOnceBeforeCommitInterceptor
        : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource reachedCommit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource continueCommit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int armed;

        public void Arm() => Volatile.Write(ref armed, 1);

        public Task WaitUntilCommitAsync(CancellationToken cancellationToken)
            => reachedCommit.Task.WaitAsync(cancellationToken);

        public void Continue() => continueCommit.TrySetResult();

        public override async ValueTask<InterceptionResult>
            TransactionCommittingAsync(
                DbTransaction transaction,
                TransactionEventData eventData,
                InterceptionResult result,
                CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                reachedCommit.TrySetResult();
                await continueCommit.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }

    private static PostgresException RetryablePostgresException(string message)
        => new(
            message,
            "ERROR",
            "ERROR",
            PostgresErrorCodes.SerializationFailure);

    private sealed record SeededEmployee(
        Guid EmployeeId,
        string EmployeeNo,
        string RealName);

    private sealed record SeededDevice(Guid DeviceId);
}
