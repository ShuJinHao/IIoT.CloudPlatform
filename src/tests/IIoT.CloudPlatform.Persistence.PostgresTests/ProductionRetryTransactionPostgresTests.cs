using System.Data.Common;
using System.Text.Json;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Auditing;
using IIoT.EntityFrameworkCore.ClientReleases;
using IIoT.EntityFrameworkCore.EdgeHosts;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Persistence;
using IIoT.EntityFrameworkCore.QueryServices;
using IIoT.EntityFrameworkCore.Repository;
using IIoT.EntityFrameworkCore.Uploads;
using IIoT.MasterDataService.Commands.Processes;
using IIoT.ProductionService.Commands.ClientVersions;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Commands.EdgeHosts;
using IIoT.ProductionService.Commands.Recipes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.Persistence;
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
    public async Task CommitConfirmationLoss_ShouldNotDuplicateAllEmployeeWritesOrDeviceDelete()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(90));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);

        await VerifyOnboardCommitRecoveryAsync(provider, interceptor, budget.Token);
        await VerifyProfileCommitRecoveryAsync(provider, interceptor, budget.Token);
        await VerifyDeactivateCommitRecoveryAsync(provider, interceptor, budget.Token);
        await VerifyActivateCommitRecoveryAsync(provider, interceptor, budget.Token);
        await VerifyTerminateCommitRecoveryAsync(provider, interceptor, budget.Token);
        await VerifyRoleCommitRecoveryAsync(provider, interceptor, budget.Token);
        await VerifyEmployeeAccessCommitRecoveryAsync(
            provider,
            interceptor,
            budget.Token);
        await VerifyDeviceDeleteCommitRecoveryAsync(provider, interceptor, budget.Token);
        await VerifyEdgeRotationAsync(
            provider,
            () => interceptor.Arm(),
            budget.Token);

        Assert.Equal(9, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task BusinessAggregateWrites_ShouldReplayTransientBeforeCommit()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(90));
        var interceptor = new ThrowOnceBeforeCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);

        await VerifyBusinessAggregateWritesAsync(
            provider,
            interceptor.Arm,
            budget.Token);

        Assert.Equal(9, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task BusinessAggregateWrites_ShouldRecoverCommitConfirmationLoss()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(90));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);

        await VerifyBusinessAggregateWritesAsync(
            provider,
            () => interceptor.Arm(),
            budget.Token);

        Assert.Equal(9, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task EdgeReports_ShouldReplayTransientBeforeCommit()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(60));
        var interceptor = new ThrowOnceBeforeCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);

        await VerifyEdgeReportsAsync(
            provider,
            interceptor.Arm,
            budget.Token);

        Assert.Equal(3, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task EdgeReports_ShouldRecoverCommitConfirmationLoss()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(60));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);

        await VerifyEdgeReportsAsync(
            provider,
            () => interceptor.Arm(),
            budget.Token);

        Assert.Equal(3, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task EmployeeMutationObservation_ShouldUseOneSnapshotAcrossConcurrentMutation()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            new ThrowOnceBeforeCommitInterceptor());
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "OBS-SNAPSHOT",
            accountEnabled: true,
            employeeActive: true,
            withSession: false,
            budget.Token);
        var unique = Guid.NewGuid().ToString("N");
        var baselineRole = $"ObserveOld{unique}"[..30];
        var concurrentRole = $"ObserveNew{unique}"[..30];
        await CreateRoleAsync(services, baselineRole);
        await CreateRoleAsync(services, concurrentRole);
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(
            seed.EmployeeId.ToString()))!;
        var baselineStamp = $"baseline-{Guid.NewGuid():N}";
        user.SecurityStamp = baselineStamp;
        Assert.True((await userManager.UpdateAsync(user)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(
            user,
            baselineRole)).Succeeded);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var concurrentRoleId = await dbContext.Roles
            .AsNoTracking()
            .Where(role => role.Name == concurrentRole)
            .Select(role => role.Id)
            .SingleAsync(budget.Token);
        dbContext.ChangeTracker.Clear();

        var pause = new PauseOnceAfterObservationReadInterceptor();
        var observationOptions = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(
                budget.ConnectionString,
                npgsql => npgsql.EnableRetryOnFailure(
                    3,
                    TimeSpan.FromMilliseconds(50),
                    null))
            .AddInterceptors(pause)
            .Options;
        var observationTask = new EmployeeMutationObservationReader(
                observationOptions)
            .ObserveAsync(seed.EmployeeId, budget.Token);
        await pause.FirstReadCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            budget.Token);
        var concurrentStamp = $"concurrent-{Guid.NewGuid():N}";
        try
        {
            await using var connection = new NpgsqlConnection(
                budget.ConnectionString);
            await connection.OpenAsync(budget.Token);
            await using var transaction =
                await connection.BeginTransactionAsync(budget.Token);

            await using (var employeeCommand = new NpgsqlCommand(
                             """
                             update employees
                             set is_active = false
                             where id = @employee_id
                             """,
                             connection,
                             transaction))
            {
                employeeCommand.Parameters.AddWithValue(
                    "employee_id",
                    seed.EmployeeId);
                Assert.Equal(
                    1,
                    await employeeCommand.ExecuteNonQueryAsync(budget.Token));
            }

            await using (var accountCommand = new NpgsqlCommand(
                             """
                             update "AspNetUsers"
                             set "IsEnabled" = false,
                                 "SecurityStamp" = @security_stamp,
                                 "ConcurrencyStamp" = @concurrency_stamp
                             where "Id" = @employee_id
                             """,
                             connection,
                             transaction))
            {
                accountCommand.Parameters.AddWithValue(
                    "employee_id",
                    seed.EmployeeId);
                accountCommand.Parameters.AddWithValue(
                    "security_stamp",
                    concurrentStamp);
                accountCommand.Parameters.AddWithValue(
                    "concurrency_stamp",
                    Guid.NewGuid().ToString("N"));
                Assert.Equal(
                    1,
                    await accountCommand.ExecuteNonQueryAsync(budget.Token));
            }

            await using (var deleteRolesCommand = new NpgsqlCommand(
                             """
                             delete from "AspNetUserRoles"
                             where "UserId" = @employee_id
                             """,
                             connection,
                             transaction))
            {
                deleteRolesCommand.Parameters.AddWithValue(
                    "employee_id",
                    seed.EmployeeId);
                Assert.Equal(
                    1,
                    await deleteRolesCommand.ExecuteNonQueryAsync(
                        budget.Token));
            }

            await using (var addRoleCommand = new NpgsqlCommand(
                             """
                             insert into "AspNetUserRoles" ("UserId", "RoleId")
                             values (@employee_id, @role_id)
                             """,
                             connection,
                             transaction))
            {
                addRoleCommand.Parameters.AddWithValue(
                    "employee_id",
                    seed.EmployeeId);
                addRoleCommand.Parameters.AddWithValue(
                    "role_id",
                    concurrentRoleId);
                Assert.Equal(
                    1,
                    await addRoleCommand.ExecuteNonQueryAsync(budget.Token));
            }

            await transaction.CommitAsync(budget.Token);
        }
        finally
        {
            pause.Resume();
        }

        var observation = await observationTask;
        Assert.True(observation.EmployeeExists);
        Assert.True(observation.EmployeeIsActive);
        Assert.Equal(seed.EmployeeNo, observation.EmployeeNo);
        Assert.Equal(seed.RealName, observation.EmployeeRealName);
        Assert.True(observation.EmployeeRowVersion.HasValue);
        Assert.Empty(observation.EmployeeDeviceIds ?? []);
        Assert.True(observation.AccountExists);
        Assert.True(observation.AccountIsEnabled);
        Assert.Equal(seed.EmployeeNo, observation.AccountEmployeeNo);
        Assert.Equal(baselineStamp, observation.AccountSecurityStamp);
        Assert.Equal([baselineRole], observation.Roles);
        Assert.False(observation.HasActiveHumanSessions);
        Assert.Equal(3, pause.ObservationTransactions.Count);
        var snapshotTransaction = Assert.IsAssignableFrom<DbTransaction>(
            pause.ObservationTransactions[0]);
        Assert.All(
            pause.ObservationTransactions,
            transaction => Assert.Same(snapshotTransaction, transaction));

        var current = await new EmployeeMutationObservationReader(
                new DbContextOptionsBuilder<IIoTDbContext>()
                    .UseNpgsql(budget.ConnectionString)
                    .Options)
            .ObserveAsync(seed.EmployeeId, budget.Token);
        Assert.False(current.EmployeeIsActive);
        Assert.False(current.AccountIsEnabled);
        Assert.Equal(concurrentStamp, current.AccountSecurityStamp);
        Assert.Equal([concurrentRole], current.Roles);
    }

    [Theory]
    [InlineData("deactivate")]
    [InlineData("role-change")]
    [InlineData("another-activation")]
    public async Task ActivateCommitConfirmationLoss_WithConcurrentMutation_ShouldConflictWithoutOverwrite(
        string mutationKind)
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);

        await VerifyActivateCommitDriftAsync(
            provider,
            interceptor,
            mutationKind,
            budget.Token);

        Assert.Equal(1, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task ProfileCommitConfirmationLoss_WithConcurrentRename_ShouldConflictWithoutOverwrite()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "DRIFT-PROFILE",
            accountEnabled: true,
            employeeActive: true,
            withSession: false,
            budget.Token);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var identityStore = CreateIdentityStore(services);
        var handler = new UpdateEmployeeProfileHandler(
            new EfRepository<Employee>(dbContext),
            CreateUnitOfWork(dbContext),
            new AdminTargetGuard(identityStore),
            CreateEmployeeMutationObserver(services));
        const string concurrentName = "Concurrent Profile";

        interceptor.Arm(async callbackCancellationToken =>
        {
            await using var connection = new NpgsqlConnection(
                budget.ConnectionString);
            await connection.OpenAsync(callbackCancellationToken);
            await using var command = new NpgsqlCommand(
                """
                update employees
                set real_name = @real_name
                where id = @employee_id
                """,
                connection);
            command.Parameters.AddWithValue("real_name", concurrentName);
            command.Parameters.AddWithValue("employee_id", seed.EmployeeId);
            Assert.Equal(
                1,
                await command.ExecuteNonQueryAsync(callbackCancellationToken));
        });

        await Assert.ThrowsAsync<EmployeeWriteConflictException>(() =>
            handler.Handle(
                new UpdateEmployeeProfileCommand(
                    seed.EmployeeId,
                    "Original Profile Target"),
                budget.Token));

        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            concurrentName,
            await dbContext.Employees
                .AsNoTracking()
                .Where(employee => employee.Id == seed.EmployeeId)
                .Select(employee => employee.RealName)
                .SingleAsync(budget.Token));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "EmployeeRenamedDomainEvent",
                seed.EmployeeId,
                budget.Token));
        Assert.False(dbContext.HasPendingDomainEvents);
        Assert.Equal(1, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task AccessCommitConfirmationLoss_WithConcurrentAccessChange_ShouldConflictWithoutOverwrite()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "DRIFT-ACCESS",
            accountEnabled: true,
            employeeActive: true,
            withSession: false,
            budget.Token);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var (requestedProcess, requestedDevice) =
            CreateProcessAndDevice("DRIFT-ACCESS-REQUEST");
        var (concurrentProcess, concurrentDevice) =
            CreateProcessAndDevice("DRIFT-ACCESS-NEW");
        requestedProcess.ClearDomainEvents();
        requestedDevice.ClearDomainEvents();
        concurrentProcess.ClearDomainEvents();
        concurrentDevice.ClearDomainEvents();
        dbContext.MfgProcesses.AddRange(
            requestedProcess,
            concurrentProcess);
        dbContext.Devices.AddRange(
            requestedDevice,
            concurrentDevice);
        await dbContext.SaveChangesAsync(budget.Token);
        dbContext.ChangeTracker.Clear();
        var identityStore = CreateIdentityStore(services);
        var handler = new UpdateEmployeeAccessHandler(
            new EfRepository<Employee>(dbContext),
            new AdminTargetGuard(identityStore),
            new DeviceReadQueryService(dbContext),
            CreateUnitOfWork(dbContext),
            CreateEmployeeMutationObserver(services),
            new EmployeeMutationVersionStore(dbContext));

        interceptor.Arm(async callbackCancellationToken =>
        {
            await using var connection = new NpgsqlConnection(
                budget.ConnectionString);
            await connection.OpenAsync(callbackCancellationToken);
            await using var transaction =
                await connection.BeginTransactionAsync(
                    callbackCancellationToken);
            await using (var deleteCommand = new NpgsqlCommand(
                             """
                             delete from employee_device_accesses
                             where employee_id = @employee_id
                             """,
                             connection,
                             transaction))
            {
                deleteCommand.Parameters.AddWithValue(
                    "employee_id",
                    seed.EmployeeId);
                Assert.Equal(
                    1,
                    await deleteCommand.ExecuteNonQueryAsync(
                        callbackCancellationToken));
            }

            await using (var insertCommand = new NpgsqlCommand(
                             """
                             insert into employee_device_accesses
                                 (employee_id, device_id)
                             values (@employee_id, @device_id)
                             """,
                             connection,
                             transaction))
            {
                insertCommand.Parameters.AddWithValue(
                    "employee_id",
                    seed.EmployeeId);
                insertCommand.Parameters.AddWithValue(
                    "device_id",
                    concurrentDevice.Id);
                Assert.Equal(
                    1,
                    await insertCommand.ExecuteNonQueryAsync(
                        callbackCancellationToken));
            }

            await using (var versionCommand = new NpgsqlCommand(
                             """
                             update employees
                             set is_active = is_active
                             where id = @employee_id
                             """,
                             connection,
                             transaction))
            {
                versionCommand.Parameters.AddWithValue(
                    "employee_id",
                    seed.EmployeeId);
                Assert.Equal(
                    1,
                    await versionCommand.ExecuteNonQueryAsync(
                        callbackCancellationToken));
            }

            await transaction.CommitAsync(callbackCancellationToken);
        });

        await Assert.ThrowsAsync<EmployeeWriteConflictException>(() =>
            handler.Handle(
                new UpdateEmployeeAccessCommand(
                    seed.EmployeeId,
                    [requestedDevice.Id]),
                budget.Token));

        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            [concurrentDevice.Id],
            await dbContext.Set<EmployeeDeviceAccess>()
                .AsNoTracking()
                .Where(access => access.EmployeeId == seed.EmployeeId)
                .OrderBy(access => access.DeviceId)
                .Select(access => access.DeviceId)
                .ToArrayAsync(budget.Token));
        Assert.False(dbContext.HasPendingDomainEvents);
        Assert.Equal(1, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task DeactivateCommitConfirmationLoss_WithConcurrentReactivation_ShouldConflictWithoutOverwrite()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(60));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "DRIFT-DEACTIVATE",
            accountEnabled: true,
            employeeActive: true,
            withSession: true,
            budget.Token);
        var concurrentRole = $"Concurrent{Guid.NewGuid():N}"[..30];
        await CreateRoleAsync(services, concurrentRole);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var concurrentRoleId = await dbContext.Roles
            .AsNoTracking()
            .Where(role => role.Name == concurrentRole)
            .Select(role => role.Id)
            .SingleAsync(budget.Token);
        var identityStore = CreateIdentityStore(services);
        var handler = new DeactivateEmployeeHandler(
            new EfRepository<Employee>(dbContext),
            identityStore,
            CreateUnitOfWork(dbContext),
            new HumanSessionRevocationService(dbContext),
            new AdminTargetGuard(identityStore),
            CreateEmployeeMutationObserver(services));
        var concurrentStamp = $"concurrent-{Guid.NewGuid():N}";

        interceptor.Arm(async callbackCancellationToken =>
        {
            await using var connection = new NpgsqlConnection(
                budget.ConnectionString);
            await connection.OpenAsync(callbackCancellationToken);
            await using var transaction =
                await connection.BeginTransactionAsync(
                    callbackCancellationToken);
            await using (var employeeCommand = new NpgsqlCommand(
                             """
                             update employees
                             set is_active = true
                             where id = @employee_id
                             """,
                             connection,
                             transaction))
            {
                employeeCommand.Parameters.AddWithValue(
                    "employee_id",
                    seed.EmployeeId);
                Assert.Equal(
                    1,
                    await employeeCommand.ExecuteNonQueryAsync(
                        callbackCancellationToken));
            }

            await using (var accountCommand = new NpgsqlCommand(
                             """
                             update "AspNetUsers"
                             set "IsEnabled" = true,
                                 "SecurityStamp" = @security_stamp,
                                 "ConcurrencyStamp" = @concurrency_stamp
                             where "Id" = @employee_id
                             """,
                             connection,
                             transaction))
            {
                accountCommand.Parameters.AddWithValue(
                    "security_stamp",
                    concurrentStamp);
                accountCommand.Parameters.AddWithValue(
                    "concurrency_stamp",
                    Guid.NewGuid().ToString("N"));
                accountCommand.Parameters.AddWithValue(
                    "employee_id",
                    seed.EmployeeId);
                Assert.Equal(
                    1,
                    await accountCommand.ExecuteNonQueryAsync(
                        callbackCancellationToken));
            }

            await using (var roleCommand = new NpgsqlCommand(
                             """
                             insert into "AspNetUserRoles" ("UserId", "RoleId")
                             values (@employee_id, @role_id)
                             """,
                             connection,
                             transaction))
            {
                roleCommand.Parameters.AddWithValue(
                    "employee_id",
                    seed.EmployeeId);
                roleCommand.Parameters.AddWithValue(
                    "role_id",
                    concurrentRoleId);
                Assert.Equal(
                    1,
                    await roleCommand.ExecuteNonQueryAsync(
                        callbackCancellationToken));
            }

            await using (var sessionCommand = new NpgsqlCommand(
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
                             connection,
                             transaction))
            {
                sessionCommand.Parameters.AddWithValue(
                    "id",
                    Guid.NewGuid());
                sessionCommand.Parameters.AddWithValue(
                    "actor_type",
                    IIoTClaimTypes.HumanActor);
                sessionCommand.Parameters.AddWithValue(
                    "subject_id",
                    seed.EmployeeId);
                sessionCommand.Parameters.AddWithValue(
                    "token_hash",
                    $"tx-concurrent-human-{Guid.NewGuid():N}");
                Assert.Equal(
                    1,
                    await sessionCommand.ExecuteNonQueryAsync(
                        callbackCancellationToken));
            }

            await transaction.CommitAsync(callbackCancellationToken);
        });

        await Assert.ThrowsAsync<EmployeeWriteConflictException>(() =>
            handler.Handle(
                new DeactivateEmployeeCommand(seed.EmployeeId),
                budget.Token));

        dbContext.ChangeTracker.Clear();
        Assert.True(await dbContext.Employees
            .AsNoTracking()
            .Where(employee => employee.Id == seed.EmployeeId)
            .Select(employee => employee.IsActive)
            .SingleAsync(budget.Token));
        var account = await dbContext.Users
            .AsNoTracking()
            .SingleAsync(
                user => user.Id == seed.EmployeeId,
                budget.Token);
        Assert.True(account.IsEnabled);
        Assert.Equal(concurrentStamp, account.SecurityStamp);
        Assert.Contains(
            concurrentRole,
            await identityStore.GetRolesAsync(
                seed.EmployeeId,
                budget.Token));
        Assert.True(await HasActiveHumanSessionAsync(
            dbContext,
            seed.EmployeeId,
            budget.Token));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "EmployeeDeactivatedDomainEvent",
                seed.EmployeeId,
                budget.Token));
        Assert.False(dbContext.HasPendingDomainEvents);
        Assert.Equal(1, interceptor.ExceptionsThrown);
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
    public async Task OnboardCommitConfirmationLoss_WithFollowUpAccess_ShouldReturnOriginalSuccess()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var unique = Guid.NewGuid().ToString("N");
        var employeeNo = $"ACK-ACCESS-{unique}"[..24];
        var (process, device) = CreateProcessAndDevice("ACKACCESS");
        process.ClearDomainEvents();
        device.ClearDomainEvents();
        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(budget.Token);
        dbContext.ChangeTracker.Clear();

        interceptor.Arm(async cancellationToken =>
        {
            await using var followUpScope = provider.CreateAsyncScope();
            var followUpContext = followUpScope.ServiceProvider
                .GetRequiredService<IIoTDbContext>();
            var employee = await followUpContext.Employees
                .SingleAsync(
                    candidate => candidate.EmployeeNo == employeeNo,
                    cancellationToken);
            employee.AddDeviceAccess(device.Id);
            await followUpContext.SaveChangesAsync(cancellationToken);
        });

        var result = await CreateOnboardHandler(services).Handle(
            new OnboardEmployeeCommand(
                employeeNo,
                "Commit Recovery Follow-up Access",
                "Retry123!"),
            budget.Token);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            1,
            await dbContext.Set<EmployeeDeviceAccess>()
                .AsNoTracking()
                .CountAsync(
                    access =>
                        access.EmployeeId == result.Value
                        && access.DeviceId == device.Id,
                    budget.Token));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "EmployeeOnboardedDomainEvent",
                result.Value,
                budget.Token));
        Assert.Equal(1, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task CallerCancellationAtCommitWithBaselineOnly_ShouldBeCommitUnknownWithoutRetry()
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
            new AdminTargetGuard(identityStore),
            CreateEmployeeMutationObserver(services));

        interceptor.Arm();
        await Assert.ThrowsAsync<EmployeeWriteCommitUnknownException>(() =>
            handler.Handle(
                new UpdateEmployeeProfileCommand(seed.EmployeeId, "Canceled Name"),
                cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
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
    public async Task DeviceDeleteCancellationAfterCommit_ShouldRecoverOutsideCallerToken()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                budget.Token);
        var interceptor = new CancelOnceAfterWriteCommitInterceptor(
            cancellation);
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var cleanOptions = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(budget.ConnectionString)
            .Options;
        var (process, device) = CreateProcessAndDevice("DELCANCEL");
        process.ClearDomainEvents();
        device.ClearDomainEvents();
        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(budget.Token);
        dbContext.ChangeTracker.Clear();
        var audit = new RecordingAuditTrailService();
        var observationReader = new AfterFirstDeviceObservationReader(
            new CloudWriteObservationReader(
                services.GetRequiredService<
                    DbContextOptions<IIoTDbContext>>()),
            async () =>
            {
                await using var lateContext =
                    new IIoTDbContext(cleanOptions);
                lateContext.DeviceClientStates.Add(
                    new DeviceClientState(
                        device.Id,
                        device.Code));
                await lateContext.SaveChangesAsync(budget.Token);
            });
        var handler = new DeleteDeviceHandler(
            HumanAdmin(),
            new EfRepository<Device>(dbContext),
            new EfDeviceDeletionDependencyService(dbContext),
            new StubCurrentUserDeviceAccessService
            {
                IsAdministrator = true
            },
            audit,
            observationReader);

        interceptor.Arm();
        var result = await handler.Handle(
            new DeleteDeviceCommand(device.Id),
            cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, interceptor.ExceptionsThrown);
        Assert.Single(audit.Entries);
        Assert.Single(audit.ConfirmedEntries);
        var auditCancellationToken =
            Assert.Single(audit.CancellationTokens);
        Assert.True(auditCancellationToken.CanBeCanceled);
        Assert.False(auditCancellationToken.IsCancellationRequested);
        Assert.NotEqual(cancellation.Token, auditCancellationToken);
        using (var auditSummary = JsonDocument.Parse(
                   Assert.Single(audit.ConfirmedEntries).Summary))
        {
            Assert.Equal(
                1,
                auditSummary.RootElement
                    .GetProperty("deleted")
                    .GetProperty("edge_device_client_states")
                    .GetInt64());
        }
        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.Devices
            .AsNoTracking()
            .AnyAsync(
                candidate => candidate.Id == device.Id,
                budget.Token));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "DeviceDeletedDomainEvent",
                device.Id,
                budget.Token));
    }

    [Fact]
    public async Task DeviceRegistrationCancellationAfterCommit_ShouldRecoverAndConfirmAuditOutsideCallerToken()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                budget.Token);
        var interceptor = new CancelOnceAfterWriteCommitInterceptor(
            cancellation);
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var process = new MfgProcess(
            $"REGCANCEL-{Guid.NewGuid():N}"[..24],
            "Registration cancellation process");
        process.ClearDomainEvents();
        dbContext.MfgProcesses.Add(process);
        await dbContext.SaveChangesAsync(budget.Token);
        dbContext.ChangeTracker.Clear();
        var audit = new RecordingAuditTrailService();
        var handler = new RegisterDeviceHandler(
            HumanAdmin(),
            new StubCurrentUserDeviceAccessService
            {
                IsAdministrator = true
            },
            new EfRepository<Device>(dbContext),
            new ProcessReadQueryService(dbContext),
            new DeviceReadQueryService(dbContext),
            audit,
            CreateUnitOfWork(dbContext),
            new CloudWriteObservationReader(
                services.GetRequiredService<
                    DbContextOptions<IIoTDbContext>>()));

        interceptor.Arm();
        var result = await handler.Handle(
            new RegisterDeviceCommand(
                $"Registration cancellation {Guid.NewGuid():N}",
                process.Id),
            cancellation.Token);

        Assert.True(result.IsSuccess);
        var created = Assert.IsType<CreateDeviceResultDto>(result.Value);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, interceptor.ExceptionsThrown);
        Assert.Single(audit.Entries);
        Assert.Single(audit.ConfirmedEntries);
        var auditCancellationToken =
            Assert.Single(audit.CancellationTokens);
        Assert.True(auditCancellationToken.CanBeCanceled);
        Assert.False(auditCancellationToken.IsCancellationRequested);
        Assert.NotEqual(cancellation.Token, auditCancellationToken);
        dbContext.ChangeTracker.Clear();
        var device = await dbContext.Devices
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == created.Id,
                budget.Token);
        Assert.Equal(created.Code, device.Code);
        Assert.Equal(process.Id, device.ProcessId);
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "DeviceRegisteredDomainEvent",
                device.Id,
                budget.Token));
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
            audit,
            new CloudWriteObservationReader(
                services.GetRequiredService<
                    DbContextOptions<IIoTDbContext>>()));

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
            audit,
            new CloudWriteObservationReader(
                services.GetRequiredService<
                    DbContextOptions<IIoTDbContext>>()));

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
            audit,
            new CloudWriteObservationReader(
                services.GetRequiredService<
                    DbContextOptions<IIoTDbContext>>()));

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

    private static async Task VerifyBusinessAggregateWritesAsync(
        ServiceProvider provider,
        Action armCommitFailure,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var options = services.GetRequiredService<
            DbContextOptions<IIoTDbContext>>();
        var observationReader = new CloudWriteObservationReader(options);
        var unique = Guid.NewGuid().ToString("N");
        var access = new StubCurrentUserDeviceAccessService
        {
            IsAdministrator = true
        };

        var processCode = $"TXB1P-{unique[..12]}";
        armCommitFailure();
        var createProcess = await new CreateProcessHandler(
                new EfRepository<MfgProcess>(dbContext),
                CreateUnitOfWork(dbContext),
                observationReader)
            .Handle(
                new CreateProcessCommand(
                    processCode,
                    $"Retry process {unique}"),
                cancellationToken);
        Assert.True(createProcess.IsSuccess);
        var processId = createProcess.Value;
        dbContext.ChangeTracker.Clear();

        var updatedProcessCode = $"{processCode}-U";
        armCommitFailure();
        var updateProcess = await new UpdateProcessHandler(
                new EfRepository<MfgProcess>(dbContext),
                CreateUnitOfWork(dbContext),
                observationReader)
            .Handle(
                new UpdateProcessCommand(
                    processId,
                    updatedProcessCode,
                    $"Updated retry process {unique}"),
                cancellationToken);
        Assert.True(updateProcess.IsSuccess);
        dbContext.ChangeTracker.Clear();

        armCommitFailure();
        var deleteProcess = await new DeleteProcessHandler(
                new EfRepository<MfgProcess>(dbContext),
                new ProcessReadQueryService(dbContext),
                CreateUnitOfWork(dbContext),
                observationReader)
            .Handle(
                new DeleteProcessCommand(processId),
                cancellationToken);
        Assert.True(deleteProcess.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.MfgProcesses
            .AsNoTracking()
            .AnyAsync(
                process => process.Id == processId,
                cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "MfgProcessCreatedDomainEvent",
                processId,
                cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "MfgProcessRenamedDomainEvent",
                processId,
                cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "MfgProcessDeletedDomainEvent",
                processId,
                cancellationToken));

        var deviceProcess = new MfgProcess(
            $"TXB1D-{unique[..12]}",
            $"Device process {unique}");
        deviceProcess.ClearDomainEvents();
        dbContext.MfgProcesses.Add(deviceProcess);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        var audit = new RecordingAuditTrailService();

        armCommitFailure();
        var registerDevice = await new RegisterDeviceHandler(
                HumanAdmin(),
                access,
                new EfRepository<Device>(dbContext),
                new ProcessReadQueryService(dbContext),
                new DeviceReadQueryService(dbContext),
                audit,
                CreateUnitOfWork(dbContext),
                observationReader)
            .Handle(
                new RegisterDeviceCommand(
                    $"Retry device {unique}",
                    deviceProcess.Id),
                cancellationToken);
        Assert.True(registerDevice.IsSuccess);
        var deviceId = registerDevice.Value!.Id;
        dbContext.ChangeTracker.Clear();

        armCommitFailure();
        var updateDevice = await new UpdateDeviceProfileHandler(
                new EfRepository<Device>(dbContext),
                access,
                CreateUnitOfWork(dbContext),
                observationReader)
            .Handle(
                new UpdateDeviceProfileCommand(
                    deviceId,
                    $"Updated retry device {unique}"),
                cancellationToken);
        Assert.True(updateDevice.IsSuccess);
        dbContext.ChangeTracker.Clear();

        armCommitFailure();
        var deleteDevice = await new DeleteDeviceHandler(
                HumanAdmin(),
                new EfRepository<Device>(dbContext),
                new EfDeviceDeletionDependencyService(dbContext),
                access,
                audit,
                observationReader)
            .Handle(
                new DeleteDeviceCommand(deviceId),
                cancellationToken);
        Assert.True(deleteDevice.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.Devices
            .AsNoTracking()
            .AnyAsync(
                device => device.Id == deviceId,
                cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "DeviceRegisteredDomainEvent",
                deviceId,
                cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "DeviceRenamedDomainEvent",
                deviceId,
                cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "DeviceDeletedDomainEvent",
                deviceId,
                cancellationToken));
        Assert.Equal(2, audit.Entries.Count);
        Assert.Equal(
            2,
            audit.Entries
                .Select(entry => entry.IdempotencyKey)
                .Distinct(StringComparer.Ordinal)
                .Count());

        var recipeProcess = new MfgProcess(
            $"TXB1R-{unique[..12]}",
            $"Recipe process {unique}");
        var recipeDevice = new Device(
            $"Recipe device {unique}",
            $"TXB1-{unique[..20]}",
            recipeProcess.Id);
        recipeProcess.ClearDomainEvents();
        recipeDevice.ClearDomainEvents();
        dbContext.MfgProcesses.Add(recipeProcess);
        dbContext.Devices.Add(recipeDevice);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        var recipeName = $"Retry recipe {unique}";

        armCommitFailure();
        var createRecipe = await new CreateRecipeHandler(
                new EfRepository<Recipe>(dbContext),
                access,
                CreateUnitOfWork(dbContext),
                observationReader)
            .Handle(
                new CreateRecipeCommand(
                    recipeName,
                    recipeProcess.Id,
                    recipeDevice.Id,
                    """{"speed":1}"""),
                cancellationToken);
        Assert.True(createRecipe.IsSuccess);
        var sourceRecipeId = createRecipe.Value;
        dbContext.ChangeTracker.Clear();

        armCommitFailure();
        var upgradeRecipe = await new UpgradeRecipeVersionHandler(
                new EfRepository<Recipe>(dbContext),
                access,
                CreateUnitOfWork(dbContext),
                observationReader)
            .Handle(
                new UpgradeRecipeVersionCommand(
                    sourceRecipeId,
                    "V2.0",
                    """{"speed":2}"""),
                cancellationToken);
        Assert.True(upgradeRecipe.IsSuccess);
        var upgradedRecipeId = upgradeRecipe.Value;
        dbContext.ChangeTracker.Clear();

        armCommitFailure();
        var deleteRecipe = await new DeleteRecipeHandler(
                new EfRepository<Recipe>(dbContext),
                access,
                CreateUnitOfWork(dbContext),
                observationReader)
            .Handle(
                new DeleteRecipeCommand(sourceRecipeId),
                cancellationToken);
        Assert.True(deleteRecipe.IsSuccess);
        dbContext.ChangeTracker.Clear();
        var persistedRecipes = await dbContext.Recipes
            .AsNoTracking()
            .Where(recipe =>
                recipe.ProcessId == recipeProcess.Id
                && recipe.DeviceId == recipeDevice.Id
                && recipe.RecipeName == recipeName)
            .ToListAsync(cancellationToken);
        var persistedRecipe = Assert.Single(persistedRecipes);
        Assert.Equal(upgradedRecipeId, persistedRecipe.Id);
        Assert.Equal("V2.0", persistedRecipe.Version);
        Assert.Equal(RecipeStatus.Active, persistedRecipe.Status);
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "RecipeCreatedDomainEvent",
                sourceRecipeId,
                cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "RecipeVersionUpgradedDomainEvent",
                upgradedRecipeId,
                cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "RecipeDeletedDomainEvent",
                sourceRecipeId,
                cancellationToken));
    }

    private static async Task VerifyEdgeReportsAsync(
        ServiceProvider provider,
        Action armCommitFailure,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var options = services.GetRequiredService<
            DbContextOptions<IIoTDbContext>>();
        var observationReader = new CloudWriteObservationReader(options);
        var unique = Guid.NewGuid().ToString("N");
        var (process, device) = CreateProcessAndDevice(
            $"REPORT-{unique[..8]}");
        process.ClearDomainEvents();
        device.ClearDomainEvents();
        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        var identity = new StubDeviceIdentityQueryService
        {
            Exists = true,
            Snapshot = new DeviceIdentitySnapshot(
                device.Id,
                device.Code)
        };
        var reportedAtUtc = DateTime.UtcNow
            .AddSeconds(-5)
            .AddTicks(7);

        armCommitFailure();
        var versionResult = await new ReportDeviceClientVersionHandler(
                identity,
                new EfDeviceClientStateStore(dbContext),
                CreateUnitOfWork(dbContext),
                observationReader,
                TimeProvider.System)
            .Handle(
                new ReportDeviceClientVersionCommand(
                    device.Id,
                    device.Code,
                    "3.1.0",
                    "3.0",
                    [
                        new DeviceClientPluginVersionReportItem(
                            "ap.runtime",
                            "AP runtime",
                            "3.1.0",
                            "3.0")
                    ],
                    ["ap.runtime"],
                    "stable",
                    reportedAtUtc,
                    ["10.0.0.2"],
                    "192.0.2.2"),
                cancellationToken);
        Assert.True(versionResult.IsSuccess);
        dbContext.ChangeTracker.Clear();

        armCommitFailure();
        var heartbeatResult = await new ReportDeviceRuntimeHeartbeatHandler(
                identity,
                new EfDeviceClientStateStore(dbContext),
                TimeProvider.System,
                CreateUnitOfWork(dbContext),
                observationReader)
            .Handle(
                new ReportDeviceRuntimeHeartbeatCommand(
                    device.Id,
                    device.Code,
                    $"runtime-{unique}",
                    "production",
                    "3.1.0",
                    "3.0",
                    "Running",
                    reportedAtUtc.AddMinutes(-1),
                    reportedAtUtc.AddSeconds(1),
                    ["10.0.0.2"],
                    "192.0.2.2"),
                cancellationToken);
        Assert.True(heartbeatResult.IsSuccess);
        dbContext.ChangeTracker.Clear();

        armCommitFailure();
        var plcResult = await new ReportEdgeHostPlcRuntimeStatesHandler(
                identity,
                new EfEdgeHostPlcRuntimeStateStore(dbContext),
                new EfDeviceClientStateStore(dbContext),
                CreateUnitOfWork(dbContext),
                observationReader,
                TimeProvider.System)
            .Handle(
                new ReportEdgeHostPlcRuntimeStatesCommand(
                    device.Id,
                    device.Code,
                    reportedAtUtc.AddSeconds(2),
                    [
                        new EdgeHostPlcRuntimeStateReportItem(
                            "PLC-A",
                            "Line A",
                            true,
                            "Connected",
                            reportedAtUtc.AddSeconds(2),
                            "S01",
                            "S7",
                            "10.0.0.10"),
                        new EdgeHostPlcRuntimeStateReportItem(
                            "PLC-B",
                            "Line B",
                            false,
                            "Disconnected",
                            reportedAtUtc.AddSeconds(2),
                            "S02",
                            "ModbusTcp",
                            "10.0.0.11",
                            "offline")
                    ]),
                cancellationToken);
        Assert.True(plcResult.IsSuccess);
        dbContext.ChangeTracker.Clear();

        Assert.Equal(
            1,
            await dbContext.DeviceClientVersionSnapshots
                .AsNoTracking()
                .CountAsync(
                    snapshot => snapshot.DeviceId == device.Id,
                    cancellationToken));
        Assert.Equal(
            1,
            await dbContext.Set<DeviceClientPluginVersion>()
                .AsNoTracking()
                .CountAsync(
                    plugin =>
                        plugin.DeviceClientVersionSnapshotId
                        == device.Id,
                    cancellationToken));
        Assert.Equal(
            1,
            await dbContext.EdgeDeviceRuntimeHeartbeats
                .AsNoTracking()
                .CountAsync(
                    heartbeat => heartbeat.DeviceId == device.Id,
                    cancellationToken));
        Assert.Equal(
            1,
            await dbContext.DeviceClientStates
                .AsNoTracking()
                .CountAsync(
                    state => state.DeviceId == device.Id,
                    cancellationToken));
        Assert.Equal(
            2,
            await dbContext.EdgeHostPlcRuntimeStates
                .AsNoTracking()
                .CountAsync(
                    state => state.DeviceId == device.Id,
                    cancellationToken));
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
            new AdminTargetGuard(identityStore),
            CreateEmployeeMutationObserver(services));

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
            new AdminTargetGuard(identityStore),
            CreateEmployeeMutationObserver(services));

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
            new AdminTargetGuard(identityStore),
            new EmployeeMutationObservationReader(
                services.GetRequiredService<DbContextOptions<IIoTDbContext>>()));

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
            new AdminTargetGuard(identityStore),
            CreateEmployeeMutationObserver(services));

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
            new EmployeeMutationObservationReader(
                services.GetRequiredService<DbContextOptions<IIoTDbContext>>()),
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

    private static async Task VerifyRoleCommitRecoveryAsync(
        ServiceProvider provider,
        ThrowOnceAfterCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "ACKROLE",
            accountEnabled: true,
            employeeActive: true,
            withSession: true,
            cancellationToken);
        var unique = Guid.NewGuid().ToString("N");
        var oldRole = $"AckOld{unique}"[..30];
        var newRole = $"AckNew{unique}"[..30];
        await CreateRoleAsync(services, oldRole);
        await CreateRoleAsync(services, newRole);
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByIdAsync(
            seed.EmployeeId.ToString()))!;
        Assert.True((await userManager.AddToRoleAsync(user, oldRole)).Succeeded);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        dbContext.ChangeTracker.Clear();
        var identityStore = CreateIdentityStore(services);
        var dbContextOptions =
            services.GetRequiredService<DbContextOptions<IIoTDbContext>>();
        var handler = new UpdateEmployeeRoleHandler(
            identityStore,
            CreateRolePolicyService(services),
            CreateUnitOfWork(dbContext),
            new HumanSessionRevocationService(dbContext),
            new AdminTargetGuard(identityStore),
            new EmployeeLookupService(dbContext),
            new EmployeeMutationObservationReader(dbContextOptions),
            HumanAdmin(),
            new EfAuditTrailService(
                dbContextOptions,
                NullLogger<EfAuditTrailService>.Instance));

        interceptor.Arm();
        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(seed.EmployeeId, newRole),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            [newRole],
            await identityStore.GetRolesAsync(
                seed.EmployeeId,
                cancellationToken));
        var audits = await dbContext.AuditTrails
            .AsNoTracking()
            .Where(entry =>
                entry.OperationType == "Employee.Role.Update"
                && entry.TargetIdOrKey == seed.EmployeeId.ToString())
            .ToListAsync(cancellationToken);
        var audit = Assert.Single(audits);
        Assert.True(audit.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(audit.IdempotencyKey));
        Assert.Contains(
            "\"resultCode\":\"CommitRecovered\"",
            audit.Summary,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TransactionFailed",
            audit.Summary,
            StringComparison.Ordinal);
        Assert.False(await HasActiveHumanSessionAsync(
            dbContext,
            seed.EmployeeId,
            cancellationToken));
        Assert.False(dbContext.HasPendingDomainEvents);
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
            CreateUnitOfWork(dbContext),
            CreateEmployeeMutationObserver(services),
            new EmployeeMutationVersionStore(dbContext));

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
            audit,
            new CloudWriteObservationReader(
                services.GetRequiredService<
                    DbContextOptions<IIoTDbContext>>()));

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
        Assert.False(dbContext.HasPendingDomainEvents);
    }

    private static async Task VerifyProfileCommitRecoveryAsync(
        ServiceProvider provider,
        ThrowOnceAfterCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "ACKPROFILE",
            accountEnabled: true,
            employeeActive: true,
            withSession: false,
            cancellationToken);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var identityStore = CreateIdentityStore(services);
        var handler = new UpdateEmployeeProfileHandler(
            new EfRepository<Employee>(dbContext),
            CreateUnitOfWork(dbContext),
            new AdminTargetGuard(identityStore),
            CreateEmployeeMutationObserver(services));

        interceptor.Arm();
        var result = await handler.Handle(
            new UpdateEmployeeProfileCommand(
                seed.EmployeeId,
                "Commit Recovery Profile"),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            "Commit Recovery Profile",
            await dbContext.Employees
                .AsNoTracking()
                .Where(employee => employee.Id == seed.EmployeeId)
                .Select(employee => employee.RealName)
                .SingleAsync(cancellationToken));
        Assert.Equal(
            1,
            await CountOutboxAsync(
                dbContext,
                "EmployeeRenamedDomainEvent",
                seed.EmployeeId,
                cancellationToken));
        Assert.False(dbContext.HasPendingDomainEvents);
    }

    private static async Task VerifyDeactivateCommitRecoveryAsync(
        ServiceProvider provider,
        ThrowOnceAfterCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "ACKDEACTIVATE",
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
        var handler = new DeactivateEmployeeHandler(
            new EfRepository<Employee>(dbContext),
            identityStore,
            CreateUnitOfWork(dbContext),
            new HumanSessionRevocationService(dbContext),
            new AdminTargetGuard(identityStore),
            CreateEmployeeMutationObserver(services));

        interceptor.Arm();
        var result = await handler.Handle(
            new DeactivateEmployeeCommand(seed.EmployeeId),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.Employees
            .AsNoTracking()
            .Where(employee => employee.Id == seed.EmployeeId)
            .Select(employee => employee.IsActive)
            .SingleAsync(cancellationToken));
        var account = await dbContext.Users
            .AsNoTracking()
            .SingleAsync(
                user => user.Id == seed.EmployeeId,
                cancellationToken);
        Assert.False(account.IsEnabled);
        Assert.NotEqual(originalStamp, account.SecurityStamp);
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
        Assert.False(dbContext.HasPendingDomainEvents);
    }

    private static async Task VerifyEmployeeAccessCommitRecoveryAsync(
        ServiceProvider provider,
        ThrowOnceAfterCommitInterceptor interceptor,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            "ACKACCESS",
            accountEnabled: true,
            employeeActive: true,
            withSession: false,
            cancellationToken);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var (process, device) = CreateProcessAndDevice("ACKACCESS");
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
            CreateUnitOfWork(dbContext),
            CreateEmployeeMutationObserver(services),
            new EmployeeMutationVersionStore(dbContext));

        interceptor.Arm();
        var result = await handler.Handle(
            new UpdateEmployeeAccessCommand(
                seed.EmployeeId,
                [device.Id, device.Id]),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            [device.Id],
            await dbContext.Set<EmployeeDeviceAccess>()
                .AsNoTracking()
                .Where(access => access.EmployeeId == seed.EmployeeId)
                .OrderBy(access => access.DeviceId)
                .Select(access => access.DeviceId)
                .ToArrayAsync(cancellationToken));
        Assert.False(dbContext.HasPendingDomainEvents);
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
            new AdminTargetGuard(identityStore),
            CreateEmployeeMutationObserver(services));

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
        Assert.False(dbContext.HasPendingDomainEvents);
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
            new AdminTargetGuard(identityStore),
            new EmployeeMutationObservationReader(
                services.GetRequiredService<DbContextOptions<IIoTDbContext>>()));
        interceptor.Arm();
        var result = await handler.Handle(
            new ActivateEmployeeCommand(seed.EmployeeId),
            cancellationToken);

        Assert.True(result.IsSuccess);
        dbContext.ChangeTracker.Clear();
        var persistedStamp = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == seed.EmployeeId)
            .Select(user => user.SecurityStamp)
            .SingleAsync(cancellationToken);
        Assert.NotEqual(originalStamp, persistedStamp);
        Assert.False(await HasActiveHumanSessionAsync(
            dbContext,
            seed.EmployeeId,
            cancellationToken));
        Assert.Equal(
            0,
            await CountOutboxAsync(
                dbContext,
                "EmployeeActivatedDomainEvent",
                seed.EmployeeId,
                cancellationToken));
        Assert.False(dbContext.HasPendingDomainEvents);
    }

    private static async Task VerifyActivateCommitDriftAsync(
        ServiceProvider provider,
        ThrowOnceAfterCommitInterceptor interceptor,
        string mutationKind,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var seed = await SeedEmployeeAsync(
            services,
            $"DRIFT-{mutationKind}",
            accountEnabled: false,
            employeeActive: false,
            withSession: true,
            cancellationToken);
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var identityStore = CreateIdentityStore(services);
        Guid? concurrentRoleId = null;
        string? concurrentRoleName = null;
        if (string.Equals(
                mutationKind,
                "role-change",
                StringComparison.Ordinal))
        {
            concurrentRoleName = $"Concurrent{Guid.NewGuid():N}"[..30];
            await CreateRoleAsync(services, concurrentRoleName);
            concurrentRoleId = await dbContext.Roles
                .AsNoTracking()
                .Where(role => role.Name == concurrentRoleName)
                .Select(role => role.Id)
                .SingleAsync(cancellationToken);
        }

        var handler = new ActivateEmployeeHandler(
            new EfRepository<Employee>(dbContext),
            identityStore,
            CreateUnitOfWork(dbContext),
            new HumanSessionRevocationService(dbContext),
            new AdminTargetGuard(identityStore),
            new EmployeeMutationObservationReader(
                services.GetRequiredService<DbContextOptions<IIoTDbContext>>()));
        var newerSecurityStamp = $"concurrent-{Guid.NewGuid():N}";

        interceptor.Arm(async callbackCancellationToken =>
        {
            await using var connection = new NpgsqlConnection(
                dbContext.Database.GetConnectionString());
            await connection.OpenAsync(callbackCancellationToken);
            await using var transaction =
                await connection.BeginTransactionAsync(callbackCancellationToken);

            if (string.Equals(
                    mutationKind,
                    "deactivate",
                    StringComparison.Ordinal))
            {
                await using var employeeCommand = new NpgsqlCommand(
                    """
                    update employees
                    set is_active = false
                    where id = @employee_id
                    """,
                    connection,
                    transaction);
                employeeCommand.Parameters.AddWithValue(
                    "employee_id",
                    seed.EmployeeId);
                Assert.Equal(
                    1,
                    await employeeCommand.ExecuteNonQueryAsync(
                        callbackCancellationToken));
            }

            if (concurrentRoleId.HasValue)
            {
                await using var roleCommand = new NpgsqlCommand(
                    """
                    insert into "AspNetUserRoles" ("UserId", "RoleId")
                    values (@employee_id, @role_id)
                    """,
                    connection,
                    transaction);
                roleCommand.Parameters.AddWithValue(
                    "employee_id",
                    seed.EmployeeId);
                roleCommand.Parameters.AddWithValue(
                    "role_id",
                    concurrentRoleId.Value);
                Assert.Equal(
                    1,
                    await roleCommand.ExecuteNonQueryAsync(
                        callbackCancellationToken));
            }

            await using var accountCommand = new NpgsqlCommand(
                """
                update "AspNetUsers"
                set "IsEnabled" = @is_enabled,
                    "SecurityStamp" = @security_stamp,
                    "ConcurrencyStamp" = @concurrency_stamp
                where "Id" = @employee_id
                """,
                connection,
                transaction);
            var expectedActive = !string.Equals(
                mutationKind,
                "deactivate",
                StringComparison.Ordinal);
            accountCommand.Parameters.AddWithValue(
                "is_enabled",
                expectedActive);
            accountCommand.Parameters.AddWithValue(
                "security_stamp",
                newerSecurityStamp);
            accountCommand.Parameters.AddWithValue(
                "concurrency_stamp",
                Guid.NewGuid().ToString("N"));
            accountCommand.Parameters.AddWithValue(
                "employee_id",
                seed.EmployeeId);
            Assert.Equal(
                1,
                await accountCommand.ExecuteNonQueryAsync(
                    callbackCancellationToken));
            await transaction.CommitAsync(callbackCancellationToken);
        });

        await Assert.ThrowsAsync<EmployeeActivationConflictException>(() =>
            handler.Handle(
                new ActivateEmployeeCommand(seed.EmployeeId),
                cancellationToken));

        dbContext.ChangeTracker.Clear();
        var persistedEmployee = await dbContext.Employees
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == seed.EmployeeId,
                cancellationToken);
        var persistedAccount = await dbContext.Users
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == seed.EmployeeId,
                cancellationToken);
        var expectedFinalActive = !string.Equals(
            mutationKind,
            "deactivate",
            StringComparison.Ordinal);
        Assert.Equal(newerSecurityStamp, persistedAccount.SecurityStamp);
        Assert.Equal(expectedFinalActive, persistedAccount.IsEnabled);
        Assert.Equal(expectedFinalActive, persistedEmployee.IsActive);
        if (concurrentRoleName is not null)
        {
            Assert.Contains(
                concurrentRoleName,
                await identityStore.GetRolesAsync(
                    seed.EmployeeId,
                    cancellationToken));
        }
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
            audit,
            new CloudWriteObservationReader(
                services.GetRequiredService<
                    DbContextOptions<IIoTDbContext>>()));

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
            new RecordingPermissionProvider(),
            CreateEmployeeMutationObserver(services));
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

    private static EmployeeMutationObservationReader CreateEmployeeMutationObserver(
        IServiceProvider services)
    {
        var connectionString = services
            .GetRequiredService<IIoTDbContext>()
            .Database
            .GetConnectionString();
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.EnableRetryOnFailure(
                    3,
                    TimeSpan.FromMilliseconds(50),
                    null))
            .Options;
        return new EmployeeMutationObservationReader(options);
    }

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
        {
            options
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql.EnableRetryOnFailure(
                        3,
                        TimeSpan.FromMilliseconds(50),
                        null));
            if (interceptor is WriteAwareTransactionInterceptor writeAware)
            {
                options.AddInterceptors(
                    new WriteTrackingCommandInterceptor(writeAware),
                    interceptor);
            }
            else
            {
                options.AddInterceptors(interceptor);
            }
        });
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

        public List<AuditTrailEntry> ConfirmedEntries { get; } = [];

        public List<CancellationToken> CancellationTokens { get; } = [];

        public Task TryWriteAsync(
            AuditTrailEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            CancellationTokens.Add(cancellationToken);
            return Task.CompletedTask;
        }

        public Task<bool> TryWriteConfirmedAsync(
            AuditTrailEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            ConfirmedEntries.Add(entry);
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(true);
        }
    }

    private sealed class AfterFirstDeviceObservationReader(
        IDeviceWriteObservationReader inner,
        Func<Task> afterFirstObservation)
        : IDeviceWriteObservationReader
    {
        private int observationCount;

        public async Task<DeviceWriteObservation> ObserveDeviceAsync(
            Guid deviceId,
            string deviceName,
            string clientCode,
            Guid processId,
            CancellationToken cancellationToken)
        {
            var observation = await inner.ObserveDeviceAsync(
                deviceId,
                deviceName,
                clientCode,
                processId,
                cancellationToken);
            if (Interlocked.Increment(ref observationCount) == 1)
            {
                await afterFirstObservation();
            }

            return observation;
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
            CancellationToken cancellationToken = default,
            uint? expectedRowVersion = null)
        {
            LastDeletionResult = await inner.DeleteCascadeAsync(
                deviceId,
                cancellationToken,
                expectedRowVersion);
            return LastDeletionResult;
        }
    }

    private abstract class WriteAwareTransactionInterceptor
        : DbTransactionInterceptor
    {
        private readonly HashSet<DbTransaction> writeTransactions = [];
        private readonly object sync = new();

        public void TrackWrite(
            DbTransaction? transaction,
            string commandText)
        {
            if (transaction is null
                || !IsWriteCommand(commandText))
            {
                return;
            }

            lock (sync)
            {
                writeTransactions.Add(transaction);
            }
        }

        protected bool ConsumeWrite(DbTransaction transaction)
        {
            lock (sync)
            {
                return writeTransactions.Remove(transaction);
            }
        }

        private static bool IsWriteCommand(string commandText)
            => commandText.Contains(
                   "INSERT INTO",
                   StringComparison.OrdinalIgnoreCase)
               || commandText.Contains(
                   "UPDATE ",
                   StringComparison.OrdinalIgnoreCase)
               || commandText.Contains(
                   "DELETE FROM",
                   StringComparison.OrdinalIgnoreCase);
    }

    private sealed class WriteTrackingCommandInterceptor(
        WriteAwareTransactionInterceptor transactionInterceptor)
        : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            transactionInterceptor.TrackWrite(
                command.Transaction,
                command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            transactionInterceptor.TrackWrite(
                command.Transaction,
                command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            transactionInterceptor.TrackWrite(
                command.Transaction,
                command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>>
            NonQueryExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            transactionInterceptor.TrackWrite(
                command.Transaction,
                command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result)
        {
            transactionInterceptor.TrackWrite(
                command.Transaction,
                command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<object>>
            ScalarExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<object> result,
                CancellationToken cancellationToken = default)
        {
            transactionInterceptor.TrackWrite(
                command.Transaction,
                command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowOnceBeforeCommitInterceptor
        : WriteAwareTransactionInterceptor
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
            var hasWrites = ConsumeWrite(transaction);
            if (hasWrites
                && Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                Interlocked.Increment(ref exceptionsThrown);
                throw RetryablePostgresException(
                    "simulated transient before commit");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowOnceAfterCommitInterceptor
        : WriteAwareTransactionInterceptor
    {
        private int armed;
        private int exceptionsThrown;
        private int writeCommitPending;
        private Func<CancellationToken, Task>? afterCommit;

        public int ExceptionsThrown => Volatile.Read(ref exceptionsThrown);

        public void Arm(
            Func<CancellationToken, Task>? afterCommitAction = null)
        {
            afterCommit = afterCommitAction;
            Volatile.Write(ref armed, 1);
        }

        public override ValueTask<InterceptionResult>
            TransactionCommittingAsync(
                DbTransaction transaction,
                TransactionEventData eventData,
                InterceptionResult result,
                CancellationToken cancellationToken = default)
        {
            if (ConsumeWrite(transaction)
                && Volatile.Read(ref armed) == 1)
            {
                Volatile.Write(ref writeCommitPending, 1);
            }

            return ValueTask.FromResult(result);
        }

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref writeCommitPending, 0) == 1
                && Interlocked.CompareExchange(ref armed, 0, 1) == 1)
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

    private sealed class CancelOnceAfterWriteCommitInterceptor(
        CancellationTokenSource cancellation)
        : WriteAwareTransactionInterceptor
    {
        private int armed;
        private int writeCommitPending;
        private int exceptionsThrown;

        public int ExceptionsThrown =>
            Volatile.Read(ref exceptionsThrown);

        public void Arm() => Volatile.Write(ref armed, 1);

        public override ValueTask<InterceptionResult>
            TransactionCommittingAsync(
                DbTransaction transaction,
                TransactionEventData eventData,
                InterceptionResult result,
                CancellationToken cancellationToken = default)
        {
            if (ConsumeWrite(transaction)
                && Volatile.Read(ref armed) == 1)
            {
                Volatile.Write(ref writeCommitPending, 1);
            }

            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(
                    ref writeCommitPending,
                    0) == 1
                && Interlocked.CompareExchange(
                    ref armed,
                    0,
                    1) == 1)
            {
                cancellation.Cancel();
                Interlocked.Increment(ref exceptionsThrown);
                throw new OperationCanceledException(
                    cancellation.Token);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class PauseOnceAfterObservationReadInterceptor
        : DbCommandInterceptor
    {
        private readonly TaskCompletionSource<bool> firstReadCompleted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> resume = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int pauseClaimed;

        public TaskCompletionSource<bool> FirstReadCompleted =>
            firstReadCompleted;

        public List<DbTransaction?> ObservationTransactions { get; } = [];

        public void Resume() => resume.TrySetResult(true);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            ObservationTransactions.Add(command.Transaction);
            if (Interlocked.CompareExchange(ref pauseClaimed, 1, 0) == 0)
            {
                firstReadCompleted.TrySetResult(true);
                await resume.Task.WaitAsync(cancellationToken);
            }

            return result;
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
