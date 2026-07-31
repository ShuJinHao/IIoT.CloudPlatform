using System.Data;
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
using IIoT.Services.Contracts.Events.Capacities;
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
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;

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
    public async Task UploadRegistrationAndOutbox_ShouldRecoverCommitLossAsOneLogicalMessage()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var registry = new EfUploadReceiveRegistry(dbContext);
        var deviceId = Guid.NewGuid();
        var requestId = $"upload-{Guid.NewGuid():N}";
        var deduplicationKey = $"request:{requestId}";
        var integrationEvent = new HourlyCapacityReceivedEvent
        {
            DeviceId = deviceId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            ShiftCode = "D",
            Hour = 10,
            Minute = 30,
            TimeLabel = "10:30",
            TotalCount = 12,
            OkCount = 11,
            NgCount = 1,
            PlcName = "TX-03B3",
            OccurredAtUtc = DateTimeOffset.UtcNow.AddTicks(7),
            ReceivedAtUtc = DateTime.UtcNow
        };

        interceptor.Arm();
        var registered = await registry.RegisterAndEnqueueAsync(
            deviceId,
            "hourly-capacity",
            requestId,
            deduplicationKey,
            integrationEvent,
            budget.Token);
        interceptor.Arm();
        var duplicate = await registry.RegisterAndEnqueueAsync(
            deviceId,
            "hourly-capacity",
            requestId,
            deduplicationKey,
            integrationEvent with
            {
                EventId = Guid.NewGuid(),
                OccurredAtUtc = DateTimeOffset.UtcNow
            },
            budget.Token);

        Assert.Equal(2, interceptor.ExceptionsThrown);
        Assert.False(registered.IsDuplicate);
        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(registered.OutboxMessageId, duplicate.OutboxMessageId);

        dbContext.ChangeTracker.Clear();
        var registration = await dbContext.UploadReceiveRegistrations
            .AsNoTracking()
            .SingleAsync(
                candidate =>
                    candidate.DeviceId == deviceId
                    && candidate.MessageType == "hourly-capacity"
                    && candidate.DeduplicationKey == deduplicationKey,
                budget.Token);
        var outbox = await dbContext.OutboxMessages
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == registered.OutboxMessageId,
                budget.Token);
        Assert.Equal(outbox.Id, registration.OutboxMessageId);
        Assert.Equal(2, registration.SeenCount);
        Assert.Equal(
            1,
            await dbContext.UploadReceiveObservations
                .AsNoTracking()
                .CountAsync(
                    observation =>
                        observation.RegistrationId == registration.Id,
                    budget.Token));
        Assert.Equal(0, outbox.OccurredAtUtc.UtcTicks % TimeSpan.TicksPerMicrosecond);
        Assert.Equal(
            1,
            await dbContext.OutboxMessages
                .AsNoTracking()
                .CountAsync(
                    candidate => candidate.Id == registered.OutboxMessageId,
                    budget.Token));
    }

    [Fact]
    public async Task UploadReceiveObservationRetentionPruner_ShouldDeleteAllExpiredBatchesWithoutFutureDuplicate()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        await using var dbContext = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-upload-retention-{Guid.NewGuid():N}");
        var registry = new EfUploadReceiveRegistry(dbContext);
        var deviceId = Guid.NewGuid();
        var requestId = $"retention-{Guid.NewGuid():N}";
        await registry.RegisterAndEnqueueAsync(
            deviceId,
            "hourly-capacity",
            requestId,
            $"request:{requestId}",
            new HourlyCapacityReceivedEvent
            {
                DeviceId = deviceId,
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                ShiftCode = "D",
                Hour = 11,
                Minute = 0,
                TimeLabel = "11:00",
                TotalCount = 4,
                OkCount = 4,
                NgCount = 0,
                ReceivedAtUtc = DateTime.UtcNow
            },
            budget.Token);
        var registration = await dbContext.UploadReceiveRegistrations
            .AsNoTracking()
            .SingleAsync(
                candidate =>
                    candidate.DeviceId == deviceId
                    && candidate.MessageType == "hourly-capacity",
                budget.Token);
        var observedAtUtc = DateTimeOffset.UtcNow;
        var freshObservationId = Guid.NewGuid();
        var expired = Enumerable
            .Range(
                0,
                EfUploadReceiveObservationRetentionPruner.CleanupBatchSize + 1)
            .Select(index => UploadReceiveObservation.Create(
                Guid.NewGuid(),
                registration.Id,
                observedAtUtc
                - EfUploadReceiveObservationRetentionPruner.Retention
                - TimeSpan.FromMinutes(index + 1)))
            .ToArray();
        dbContext.UploadReceiveObservations.AddRange(expired);
        dbContext.UploadReceiveObservations.Add(
            UploadReceiveObservation.Create(
                freshObservationId,
                registration.Id,
                observedAtUtc - TimeSpan.FromMinutes(1)));
        await dbContext.SaveChangesAsync(budget.Token);
        dbContext.ChangeTracker.Clear();

        var deleted =
            await new EfUploadReceiveObservationRetentionPruner(dbContext)
                .PruneExpiredAsync(observedAtUtc, budget.Token);
        dbContext.ChangeTracker.Clear();

        Assert.Equal(expired.Length, deleted);
        var remaining = await dbContext.UploadReceiveObservations
            .AsNoTracking()
            .Where(
                observation =>
                    observation.RegistrationId == registration.Id)
            .ToListAsync(budget.Token);
        Assert.Single(remaining);
        Assert.Equal(freshObservationId, remaining[0].Id);
    }

    [Fact]
    public async Task SharedPersistenceWrites_ShouldReplayTransientBeforeCommitWithoutDuplicates()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(60));
        var interceptor = new ThrowOnceBeforeCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var registry = new EfUploadReceiveRegistry(dbContext);
        var apiKeys = new EdgeReleaseApiKeyService(dbContext);
        var refreshTokens = new EfRefreshTokenService(
            dbContext,
            Options.Create(new RefreshTokenOptions()));
        var deviceId = Guid.NewGuid();
        var deduplicationKey = $"request:{Guid.NewGuid():N}";

        interceptor.Arm();
        var upload = await registry.RegisterAndEnqueueAsync(
            deviceId,
            "hourly-capacity",
            deduplicationKey,
            deduplicationKey,
            new HourlyCapacityReceivedEvent
            {
                DeviceId = deviceId,
                Date = DateOnly.FromDateTime(DateTime.UtcNow),
                ShiftCode = "D",
                Hour = 11,
                Minute = 0,
                TimeLabel = "11:00",
                TotalCount = 10,
                OkCount = 10,
                NgCount = 0,
                ReceivedAtUtc = DateTime.UtcNow
            },
            budget.Token);
        interceptor.Arm();
        var apiKey = await apiKeys.CreateAsync(
            $"tx-03b3-retry-{Guid.NewGuid():N}",
            [ClientReleasePermissions.Read],
            DateTimeOffset.UtcNow.AddDays(30),
            Guid.NewGuid(),
            new EdgeReleaseApiKeyAuditContext(
                "tx-03b3-admin",
                DateTime.UtcNow),
            budget.Token);
        var subjectId = Guid.NewGuid();
        interceptor.Arm();
        await refreshTokens.IssueHumanAsync(
            subjectId,
            $"identity-{Guid.NewGuid():N}",
            budget.Token);

        Assert.Equal(3, interceptor.ExceptionsThrown);
        Assert.True(apiKey.IsSuccess);
        dbContext.ChangeTracker.Clear();
        Assert.Equal(
            1,
            await dbContext.UploadReceiveRegistrations
                .AsNoTracking()
                .CountAsync(
                    registration =>
                        registration.DeviceId == deviceId
                        && registration.DeduplicationKey == deduplicationKey,
                    budget.Token));
        Assert.Equal(
            1,
            await dbContext.OutboxMessages
                .AsNoTracking()
                .CountAsync(message => message.Id == upload.OutboxMessageId, budget.Token));
        Assert.Equal(
            1,
            await dbContext.EdgeReleaseApiKeys
                .AsNoTracking()
                .CountAsync(key => key.Id == apiKey.Value!.Id, budget.Token));
        Assert.Equal(
            1,
            await dbContext.RefreshTokenSessions
                .AsNoTracking()
                .CountAsync(
                    session =>
                        session.ActorType == IIoTClaimTypes.HumanActor
                        && session.SubjectId == subjectId,
                    budget.Token));
    }

    [Fact]
    public async Task IdentityPolicyAndPasswordWrites_ShouldReplayTransientBeforeCommitExactly()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(60));
        var interceptor = new ThrowOnceBeforeCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);

        await VerifyIdentityPolicyAndPasswordRecoveryAsync(
            provider,
            interceptor.Arm,
            () => interceptor.ExceptionsThrown,
            budget.Token);
    }

    [Fact]
    public async Task ConcurrentFailedPasswordChecks_ShouldEachAdvanceLockoutState()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        const int concurrentChecks = 5;
        var barrier = new PasswordLockConcurrencyBarrierInterceptor(
            concurrentChecks);
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            new ThrowOnceBeforeCommitInterceptor(),
            barrier);
        Guid userId;
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var userManager = seedScope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = $"identity-concurrent-{Guid.NewGuid():N}"[..40],
                IsEnabled = true
            };
            Assert.True((await userManager.CreateAsync(
                user,
                "OldPassword123!")).Succeeded);
            userId = user.Id;
        }

        barrier.Arm();
        var scopes = Enumerable.Range(0, concurrentChecks)
            .Select(_ => provider.CreateAsyncScope())
            .ToArray();
        Result<bool>[] results;
        try
        {
            var checks = scopes
                .Select(scope => CreatePasswordService(scope.ServiceProvider)
                    .CheckPasswordAsync(
                        userId,
                        "WrongPassword123!",
                        budget.Token))
                .ToArray();
            results = await Task.WhenAll(checks);
        }
        finally
        {
            foreach (var scope in scopes)
            {
                await scope.DisposeAsync();
            }
        }

        Assert.All(results, result =>
        {
            Assert.True(result.IsSuccess);
            Assert.False(result.Value);
        });
        await using var verificationScope = provider.CreateAsyncScope();
        var persisted = await verificationScope.ServiceProvider
            .GetRequiredService<IIoTDbContext>()
            .Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == userId, budget.Token);
        Assert.True(persisted.LockoutEnabled);
        Assert.True(persisted.LockoutEnd > DateTimeOffset.UtcNow);
        Assert.Equal(0, persisted.AccessFailedCount);
    }

    [Fact]
    public async Task IdentityPolicyAndPasswordWrites_ShouldRecoverCommitConfirmationLossExactly()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(60));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);

        await VerifyIdentityPolicyAndPasswordRecoveryAsync(
            provider,
            () => interceptor.Arm(),
            () => interceptor.ExceptionsThrown,
            budget.Token);
    }

    [Fact]
    public async Task IdentityPolicyAndPasswordWrites_ShouldPropagateCallerCancellationAfterCommit()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(75));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<IIoTDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var roles = new RolePolicyService(userManager, roleManager, context);
        var passwords = new IdentityPasswordService(userManager, context);
        var unique = Guid.NewGuid().ToString("N");
        var roleName = $"CancelRole{unique}"[..30];
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"identity-cancel-{unique}"[..40],
            IsEnabled = true
        };
        Assert.True((await userManager.CreateAsync(
            user,
            "OldPassword123!")).Succeeded);
        context.ChangeTracker.Clear();

        await AssertCallerCancellationAfterCommitAsync(
            interceptor,
            async token =>
            {
                _ = await roles.DefineRoleAsync(
                    roleName,
                    [CloudPermissionCatalog.Device.Read],
                    token);
            },
            budget.Token);
        Assert.Equal(
            [CloudPermissionCatalog.Device.Read],
            await roles.GetRolePermissionsAsync(roleName));

        await AssertCallerCancellationAfterCommitAsync(
            interceptor,
            async token =>
            {
                _ = await roles.UpdateRolePermissionsAsync(
                    roleName,
                    [CloudPermissionCatalog.Recipe.Read],
                    token);
            },
            budget.Token);
        Assert.Equal(
            [CloudPermissionCatalog.Recipe.Read],
            await roles.GetRolePermissionsAsync(roleName));

        await AssertCallerCancellationAfterCommitAsync(
            interceptor,
            async token =>
            {
                _ = await roles.UpdateUserPersonalPermissionsAsync(
                    user.Id,
                    [CloudPermissionCatalog.Device.Read],
                    token);
            },
            budget.Token);
        Assert.Equal(
            [CloudPermissionCatalog.Device.Read],
            await roles.GetUserPersonalPermissionsAsync(user.Id));

        await AssertCallerCancellationAfterCommitAsync(
            interceptor,
            async token =>
            {
                _ = await passwords.CheckPasswordAsync(
                    user.Id,
                    "WrongPassword123!",
                    token);
            },
            budget.Token);
        context.ChangeTracker.Clear();
        Assert.Equal(
            1,
            (await context.Users.AsNoTracking().SingleAsync(
                candidate => candidate.Id == user.Id,
                budget.Token)).AccessFailedCount);

        await AssertCallerCancellationAfterCommitAsync(
            interceptor,
            async token =>
            {
                _ = await passwords.CheckPasswordAsync(
                    user.Id,
                    "OldPassword123!",
                    token);
            },
            budget.Token);
        context.ChangeTracker.Clear();
        Assert.Equal(
            0,
            (await context.Users.AsNoTracking().SingleAsync(
                candidate => candidate.Id == user.Id,
                budget.Token)).AccessFailedCount);

        await AssertCallerCancellationAfterCommitAsync(
            interceptor,
            async token =>
            {
                _ = await passwords.ChangePasswordAsync(
                    user.Id,
                    "OldPassword123!",
                    "ChangedPassword123!",
                    token);
            },
            budget.Token);

        await AssertCallerCancellationAfterCommitAsync(
            interceptor,
            async token =>
            {
                _ = await passwords.ResetPasswordAsync(
                    user.Id,
                    "ResetPassword123!",
                    token);
            },
            budget.Token);

        Assert.Equal(7, interceptor.ExceptionsThrown);
        context.ChangeTracker.Clear();
        var persistedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id, budget.Token);
        Assert.Equal(
            PasswordVerificationResult.Success,
            userManager.PasswordHasher.VerifyHashedPassword(
                persistedUser,
                persistedUser.PasswordHash!,
                "ResetPassword123!"));
    }

    [Fact]
    public async Task IdentityPolicyAndPasswordWrites_WithPostCommitDrift_ShouldConflictWithoutOverwrite()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(75));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<IIoTDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var roles = new RolePolicyService(userManager, roleManager, context);
        var passwords = new IdentityPasswordService(userManager, context);
        var unique = Guid.NewGuid().ToString("N");
        var roleName = $"DriftRole{unique}"[..30];

        interceptor.Arm(async callbackToken =>
        {
            await using var concurrentScope = provider.CreateAsyncScope();
            var concurrent = concurrentScope.ServiceProvider
                .GetRequiredService<IIoTDbContext>();
            var role = await concurrent.Roles.SingleAsync(
                candidate => candidate.NormalizedName == roleName.ToUpperInvariant(),
                callbackToken);
            role.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            var claims = await concurrent.RoleClaims
                .Where(claim => claim.RoleId == role.Id)
                .ToListAsync(callbackToken);
            concurrent.RoleClaims.RemoveRange(claims);
            concurrent.RoleClaims.Add(new IdentityRoleClaim<Guid>
            {
                RoleId = role.Id,
                ClaimType = IIoTClaimTypes.Permission,
                ClaimValue = CloudPermissionCatalog.Employee.Read
            });
            await concurrent.SaveChangesAsync(callbackToken);
        });
        await Assert.ThrowsAsync<CloudWriteConflictException>(
            () => roles.DefineRoleAsync(
                roleName,
                [CloudPermissionCatalog.Device.Read],
                budget.Token));
        Assert.Equal(
            [CloudPermissionCatalog.Employee.Read],
            await roles.GetRolePermissionsAsync(roleName));

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"identity-drift-{unique}"[..40],
            IsEnabled = true
        };
        Assert.True((await userManager.CreateAsync(
            user,
            "OldPassword123!")).Succeeded);
        context.ChangeTracker.Clear();

        interceptor.Arm(async callbackToken =>
        {
            await using var concurrentScope = provider.CreateAsyncScope();
            var concurrent = concurrentScope.ServiceProvider
                .GetRequiredService<IIoTDbContext>();
            var current = await concurrent.Users.SingleAsync(
                candidate => candidate.Id == user.Id,
                callbackToken);
            current.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            var claims = await concurrent.UserClaims
                .Where(claim => claim.UserId == user.Id)
                .ToListAsync(callbackToken);
            concurrent.UserClaims.RemoveRange(claims);
            concurrent.UserClaims.Add(new IdentityUserClaim<Guid>
            {
                UserId = user.Id,
                ClaimType = IIoTClaimTypes.Permission,
                ClaimValue = CloudPermissionCatalog.Recipe.Read
            });
            await concurrent.SaveChangesAsync(callbackToken);
        });
        await Assert.ThrowsAsync<CloudWriteConflictException>(
            () => roles.UpdateUserPersonalPermissionsAsync(
                user.Id,
                [CloudPermissionCatalog.Device.Read],
                budget.Token));
        Assert.Equal(
            [CloudPermissionCatalog.Recipe.Read],
            await roles.GetUserPersonalPermissionsAsync(user.Id));

        interceptor.Arm(async callbackToken =>
        {
            await using var concurrentScope = provider.CreateAsyncScope();
            var concurrentServices = concurrentScope.ServiceProvider;
            var concurrent = concurrentServices.GetRequiredService<IIoTDbContext>();
            var current = await concurrent.Users.SingleAsync(
                candidate => candidate.Id == user.Id,
                callbackToken);
            current.PasswordHash = userManager.PasswordHasher.HashPassword(
                current,
                "ConcurrentPassword123!");
            current.SecurityStamp = Guid.NewGuid().ToString("N");
            current.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            await concurrent.SaveChangesAsync(callbackToken);
        });
        await Assert.ThrowsAsync<CloudWriteConflictException>(
            () => passwords.ResetPasswordAsync(
                user.Id,
                "ResetPassword123!",
                budget.Token));

        context.ChangeTracker.Clear();
        var persistedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id, budget.Token);
        Assert.Equal(
            PasswordVerificationResult.Success,
            userManager.PasswordHasher.VerifyHashedPassword(
                persistedUser,
                persistedUser.PasswordHash!,
                "ConcurrentPassword123!"));
        Assert.Equal(3, interceptor.ExceptionsThrown);
    }

    [Fact]
    public async Task IdentityPolicyAndPasswordWrites_WhenObservationFails_ShouldRemainCommitUnknown()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(75));

        var roleCommitLoss = new ThrowOnceAfterCommitInterceptor();
        var roleObservationFailure = new FailReadsInterceptor("AspNetRoles");
        await using (var roleProvider = CreateRetryProvider(
                         budget.ConnectionString,
                         roleCommitLoss,
                         roleObservationFailure))
        {
            await using var roleScope = roleProvider.CreateAsyncScope();
            var services = roleScope.ServiceProvider;
            var roles = new RolePolicyService(
                services.GetRequiredService<UserManager<ApplicationUser>>(),
                services.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
                services.GetRequiredService<IIoTDbContext>());
            var roleName = $"UnknownRole{Guid.NewGuid():N}"[..30];
            roleCommitLoss.Arm(_ =>
            {
                roleObservationFailure.Enable();
                return Task.CompletedTask;
            });

            await Assert.ThrowsAsync<CloudWriteCommitUnknownException>(
                () => roles.DefineRoleAsync(
                    roleName,
                    [CloudPermissionCatalog.Device.Read],
                    budget.Token));

            await using var verification = CreateRetryContext(
                budget.ConnectionString,
                $"tx-03c-role-observation-verify-{Guid.NewGuid():N}");
            var role = await verification.Roles
                .AsNoTracking()
                .SingleAsync(
                    candidate => candidate.NormalizedName == roleName.ToUpperInvariant(),
                    budget.Token);
            Assert.Equal(
                1,
                await verification.RoleClaims.AsNoTracking().CountAsync(
                    claim =>
                        claim.RoleId == role.Id &&
                        claim.ClaimType == IIoTClaimTypes.Permission &&
                        claim.ClaimValue == CloudPermissionCatalog.Device.Read,
                    budget.Token));
        }

        var passwordCommitLoss = new ThrowOnceAfterCommitInterceptor();
        var passwordObservationFailure = new FailReadsInterceptor("AspNetUsers");
        await using var passwordProvider = CreateRetryProvider(
            budget.ConnectionString,
            passwordCommitLoss,
            passwordObservationFailure);
        await using var passwordScope = passwordProvider.CreateAsyncScope();
        var passwordServices = passwordScope.ServiceProvider;
        var passwordContext = passwordServices.GetRequiredService<IIoTDbContext>();
        var passwordUserManager = passwordServices
            .GetRequiredService<UserManager<ApplicationUser>>();
        var passwordUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"identity-unknown-{Guid.NewGuid():N}"[..40],
            IsEnabled = true
        };
        Assert.True((await passwordUserManager.CreateAsync(
            passwordUser,
            "OldPassword123!")).Succeeded);
        passwordContext.ChangeTracker.Clear();
        var passwords = new IdentityPasswordService(
            passwordUserManager,
            passwordContext);
        passwordCommitLoss.Arm(_ =>
        {
            passwordObservationFailure.Enable();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<CloudWriteCommitUnknownException>(
            () => passwords.ResetPasswordAsync(
                passwordUser.Id,
                "ResetPassword123!",
                budget.Token));

        await using var passwordVerification = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03c-password-observation-verify-{Guid.NewGuid():N}");
        var persistedUser = await passwordVerification.Users
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == passwordUser.Id,
                budget.Token);
        Assert.Equal(
            PasswordVerificationResult.Success,
            passwordUserManager.PasswordHasher.VerifyHashedPassword(
                persistedUser,
                persistedUser.PasswordHash!,
                "ResetPassword123!"));
    }

    [Fact]
    public async Task IdentityPolicyAndPasswordWrites_WhenBaselineCannotBeEstablished_ShouldRemainCommitUnknown()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(60));

        var roleTransaction = new ThrowOnceBeforeCommitInterceptor();
        var roleReadFailure = new FailNextReadInterceptor("AspNetRoles");
        await using (var roleProvider = CreateRetryProvider(
                         budget.ConnectionString,
                         roleTransaction,
                         roleReadFailure))
        {
            await using var roleScope = roleProvider.CreateAsyncScope();
            var services = roleScope.ServiceProvider;
            var context = services.GetRequiredService<IIoTDbContext>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var roleName = $"BaselineRole{Guid.NewGuid():N}"[..30];
            var role = new IdentityRole<Guid>(roleName)
            {
                Id = Guid.NewGuid()
            };
            Assert.True((await roleManager.CreateAsync(role)).Succeeded);
            Assert.True((await roleManager.AddClaimAsync(
                role,
                new System.Security.Claims.Claim(
                    IIoTClaimTypes.Permission,
                    CloudPermissionCatalog.Device.Read))).Succeeded);
            context.ChangeTracker.Clear();
            var roles = new RolePolicyService(
                userManager,
                roleManager,
                context);
            roleReadFailure.Arm();

            await Assert.ThrowsAsync<CloudWriteCommitUnknownException>(
                () => roles.UpdateRolePermissionsAsync(
                    roleName,
                    [CloudPermissionCatalog.Recipe.Read],
                    budget.Token));

            Assert.Equal(
                [CloudPermissionCatalog.Device.Read],
                await roles.GetRolePermissionsAsync(roleName));
        }

        var passwordTransaction = new ThrowOnceBeforeCommitInterceptor();
        var passwordReadFailure = new FailNextReadInterceptor("AspNetUsers");
        await using var passwordProvider = CreateRetryProvider(
            budget.ConnectionString,
            passwordTransaction,
            passwordReadFailure);
        await using var passwordScope = passwordProvider.CreateAsyncScope();
        var passwordServices = passwordScope.ServiceProvider;
        var passwordContext = passwordServices.GetRequiredService<IIoTDbContext>();
        var userManagerForPassword = passwordServices
            .GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"identity-baseline-{Guid.NewGuid():N}"[..40],
            IsEnabled = true
        };
        Assert.True((await userManagerForPassword.CreateAsync(
            user,
            "OldPassword123!")).Succeeded);
        passwordContext.ChangeTracker.Clear();
        var passwords = new IdentityPasswordService(
            userManagerForPassword,
            passwordContext);
        passwordReadFailure.Arm();

        await Assert.ThrowsAsync<CloudWriteCommitUnknownException>(
            () => passwords.ResetPasswordAsync(
                user.Id,
                "ResetPassword123!",
                budget.Token));

        passwordContext.ChangeTracker.Clear();
        var persistedUser = await passwordContext.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id, budget.Token);
        Assert.Equal(
            PasswordVerificationResult.Success,
            userManagerForPassword.PasswordHasher.VerifyHashedPassword(
                persistedUser,
                persistedUser.PasswordHash!,
                "OldPassword123!"));
    }

    [Fact]
    public async Task EdgeReleaseApiKeyLifecycle_ShouldRecoverCommitLossWithoutPersistingPlaintext()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var service = new EdgeReleaseApiKeyService(dbContext);
        var actorId = Guid.NewGuid();
        var name = $"tx-03b3-{Guid.NewGuid():N}";
        var auditContext = new EdgeReleaseApiKeyAuditContext(
            "tx-03b3-admin",
            DateTime.UtcNow);

        interceptor.Arm();
        var created = await service.CreateAsync(
            name,
            [ClientReleasePermissions.Read, ClientReleasePermissions.Publish],
            DateTimeOffset.UtcNow.AddDays(30),
            actorId,
            auditContext,
            budget.Token);
        Assert.True(created.IsSuccess);
        var plaintext = created.Value!.ApiKey;

        interceptor.Arm();
        var validated = await service.ValidateAsync(plaintext, budget.Token);
        Assert.True(validated.IsSuccess);
        var concurrentValidations = await Task.WhenAll(
            service.ValidateAsync(plaintext, budget.Token),
            service.ValidateAsync(plaintext, budget.Token));
        Assert.All(
            concurrentValidations,
            validation => Assert.True(validation.IsSuccess));

        interceptor.Arm();
        var revoked = await service.RevokeAsync(
            created.Value.Id,
            actorId,
            "rotation",
            auditContext with { ExecutedAtUtc = DateTime.UtcNow },
            budget.Token);
        Assert.True(revoked.IsSuccess);
        Assert.Equal(3, interceptor.ExceptionsThrown);

        dbContext.ChangeTracker.Clear();
        var key = await dbContext.EdgeReleaseApiKeys
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == created.Value.Id,
                budget.Token);
        var audits = await dbContext.AuditTrails
            .AsNoTracking()
            .Where(record =>
                record.TargetType == "EdgeReleaseApiKey"
                && record.TargetIdOrKey == created.Value.Id.ToString())
            .OrderBy(record => record.OperationType)
            .ToListAsync(budget.Token);
        Assert.Equal(EdgeReleaseApiKeyStatuses.Revoked, key.Status);
        Assert.NotNull(key.LastUsedAtUtc);
        Assert.DoesNotContain(plaintext, key.KeyHash, StringComparison.Ordinal);
        Assert.DoesNotContain(plaintext, key.PermissionsJson, StringComparison.Ordinal);
        Assert.Equal(2, audits.Count);
        Assert.All(
            audits,
            audit =>
            {
                Assert.DoesNotContain(plaintext, audit.Summary, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    plaintext,
                    audit.FailureReason ?? string.Empty,
                    StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task EdgeReleaseApiKeyRevoke_ShouldIgnoreConcurrentLastUsedTelemetryVersionDrift()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var pause = new PauseOnceAfterApiKeyPreflightReadInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            new ThrowOnceBeforeCommitInterceptor(),
            pause);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var service = new EdgeReleaseApiKeyService(dbContext);
        var actorId = Guid.NewGuid();
        var auditContext = new EdgeReleaseApiKeyAuditContext(
            "tx-03b3-admin",
            DateTime.UtcNow);
        var created = await service.CreateAsync(
            $"tx-03b3-telemetry-{Guid.NewGuid():N}",
            [ClientReleasePermissions.Read],
            DateTimeOffset.UtcNow.AddDays(30),
            actorId,
            auditContext,
            budget.Token);
        Assert.True(created.IsSuccess);

        pause.Arm();
        var revokeTask = service.RevokeAsync(
            created.Value!.Id,
            actorId,
            "security-revoke",
            auditContext with { ExecutedAtUtc = DateTime.UtcNow },
            budget.Token);
        await pause.WaitUntilPausedAsync(budget.Token);
        try
        {
            await using var telemetryContext = CreateRetryContext(
                budget.ConnectionString,
                $"tx-03b3-api-key-telemetry-{Guid.NewGuid():N}");
            Assert.Equal(
                1,
                await telemetryContext.EdgeReleaseApiKeys
                    .Where(key => key.Id == created.Value.Id)
                    .ExecuteUpdateAsync(
                        updates => updates.SetProperty(
                            key => key.LastUsedAtUtc,
                            DateTimeOffset.UtcNow),
                        budget.Token));
        }
        finally
        {
            pause.Resume();
        }

        var revoked = await revokeTask;
        Assert.True(revoked.IsSuccess);
        await using var verificationContext = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-api-key-revoke-verify-{Guid.NewGuid():N}");
        var key = await verificationContext.EdgeReleaseApiKeys
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == created.Value.Id,
                budget.Token);
        Assert.Equal(EdgeReleaseApiKeyStatuses.Revoked, key.Status);
        Assert.NotNull(key.LastUsedAtUtc);
        Assert.Equal(
            1,
            await verificationContext.AuditTrails
                .AsNoTracking()
                .CountAsync(
                    audit =>
                        audit.OperationType == "ClientRelease.ApiKey.Revoke"
                        && audit.TargetIdOrKey == created.Value.Id.ToString(),
                    budget.Token));
    }

    [Fact]
    public async Task HumanRefreshRotation_ShouldRecoverCommitLossAndRejectSourceReplay()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var service = new EfRefreshTokenService(
            dbContext,
            Options.Create(new RefreshTokenOptions()));
        var subjectId = Guid.NewGuid();

        interceptor.Arm();
        var issued = await service.IssueHumanAsync(
            subjectId,
            $"identity-{Guid.NewGuid():N}",
            budget.Token);
        interceptor.Arm();
        var rotated = await service.RotateAsync(
            IIoTClaimTypes.HumanActor,
            issued.Token,
            budget.Token);
        var competing = await service.RotateAsync(
            IIoTClaimTypes.HumanActor,
            issued.Token,
            budget.Token);

        Assert.True(rotated.IsSuccess);
        Assert.False(competing.IsSuccess);
        Assert.Equal(2, interceptor.ExceptionsThrown);
        dbContext.ChangeTracker.Clear();
        var sessions = await dbContext.RefreshTokenSessions
            .AsNoTracking()
            .Where(session =>
                session.ActorType == IIoTClaimTypes.HumanActor
                && session.SubjectId == subjectId)
            .OrderBy(session => session.CreatedAtUtc)
            .ToListAsync(budget.Token);
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, session => session.RevokedReason == "rotated");
        Assert.Single(sessions, session => !session.RevokedAtUtc.HasValue);
    }

    [Fact]
    public async Task ConcurrentHumanSessionIssue_ShouldSerializeSessionLimit()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            new ThrowOnceBeforeCommitInterceptor());
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var options = Options.Create(new RefreshTokenOptions
        {
            HumanMaxActiveSessions = 1
        });
        var firstService = new EfRefreshTokenService(
            firstScope.ServiceProvider.GetRequiredService<IIoTDbContext>(),
            options);
        var secondService = new EfRefreshTokenService(
            secondScope.ServiceProvider.GetRequiredService<IIoTDbContext>(),
            options);
        var subjectId = Guid.NewGuid();

        await Task.WhenAll(
            firstService.IssueHumanAsync(
                subjectId,
                $"identity-{Guid.NewGuid():N}",
                budget.Token),
            secondService.IssueHumanAsync(
                subjectId,
                $"identity-{Guid.NewGuid():N}",
                budget.Token));

        await using var verificationScope = provider.CreateAsyncScope();
        var dbContext = verificationScope.ServiceProvider
            .GetRequiredService<IIoTDbContext>();
        var sessions = await dbContext.RefreshTokenSessions
            .AsNoTracking()
            .Where(session =>
                session.ActorType == IIoTClaimTypes.HumanActor
                && session.SubjectId == subjectId)
            .ToListAsync(budget.Token);
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, session => !session.RevokedAtUtc.HasValue);
        Assert.Single(sessions, session => session.RevokedReason == "session-limit");
    }

    [Fact]
    public async Task IndependentHumanSessionRevocation_ShouldRecoverCommitLossExactly()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var refreshTokens = new EfRefreshTokenService(
            dbContext,
            Options.Create(new RefreshTokenOptions()));
        var subjectId = Guid.NewGuid();
        await refreshTokens.IssueHumanAsync(
            subjectId,
            $"identity-{Guid.NewGuid():N}",
            budget.Token);
        await refreshTokens.IssueHumanAsync(
            subjectId,
            $"identity-{Guid.NewGuid():N}",
            budget.Token);
        var authorization = new OpenIddictEntityFrameworkCoreAuthorization<Guid>
        {
            Id = Guid.NewGuid(),
            Subject = subjectId.ToString(),
            Status = OpenIddictConstants.Statuses.Valid,
            Type = "permanent",
            ConcurrencyToken = Guid.NewGuid().ToString("N")
        };
        var oidcToken = new OpenIddictEntityFrameworkCoreToken<Guid>
        {
            Id = Guid.NewGuid(),
            Authorization = authorization,
            Subject = subjectId.ToString(),
            Status = OpenIddictConstants.Statuses.Valid,
            Type = "authorization_code",
            ConcurrencyToken = Guid.NewGuid().ToString("N")
        };
        dbContext.OpenIddictAuthorizations.Add(authorization);
        dbContext.OpenIddictTokens.Add(oidcToken);
        await dbContext.SaveChangesAsync(budget.Token);
        dbContext.ChangeTracker.Clear();
        var revocation = new IndependentHumanSessionRevocationService(dbContext);

        interceptor.Arm();
        await revocation.RevokeAllAsync(
            subjectId,
            "manual-revoke",
            budget.Token);

        Assert.Equal(1, interceptor.ExceptionsThrown);
        dbContext.ChangeTracker.Clear();
        var sessions = await dbContext.RefreshTokenSessions
            .AsNoTracking()
            .Where(session =>
                session.ActorType == IIoTClaimTypes.HumanActor
                && session.SubjectId == subjectId)
            .ToListAsync(budget.Token);
        Assert.Equal(2, sessions.Count);
        Assert.All(
            sessions,
            session =>
            {
                Assert.NotNull(session.RevokedAtUtc);
                Assert.Equal("manual-revoke", session.RevokedReason);
            });
        Assert.Equal(
            OpenIddictConstants.Statuses.Revoked,
            (await dbContext.OpenIddictAuthorizations
                .AsNoTracking()
                .SingleAsync(
                    candidate => candidate.Id == authorization.Id,
                    budget.Token)).Status);
        Assert.Equal(
            OpenIddictConstants.Statuses.Revoked,
            (await dbContext.OpenIddictTokens
                .AsNoTracking()
                .SingleAsync(
                    candidate => candidate.Id == oidcToken.Id,
                    budget.Token)).Status);
    }

    [Fact]
    public async Task IndependentHumanSessionRevocation_WithPostCommitDrift_ShouldConflictWithoutOverwrite()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var refreshTokens = new EfRefreshTokenService(
            dbContext,
            Options.Create(new RefreshTokenOptions()));
        var subjectId = Guid.NewGuid();
        await refreshTokens.IssueHumanAsync(
            subjectId,
            $"identity-{Guid.NewGuid():N}",
            budget.Token);
        var revocation = new IndependentHumanSessionRevocationService(dbContext);

        interceptor.Arm(async callbackToken =>
        {
            await using var concurrentScope = provider.CreateAsyncScope();
            var concurrentContext = concurrentScope.ServiceProvider
                .GetRequiredService<IIoTDbContext>();
            var session = await concurrentContext.RefreshTokenSessions
                .SingleAsync(
                    candidate =>
                        candidate.ActorType == IIoTClaimTypes.HumanActor
                        && candidate.SubjectId == subjectId,
                    callbackToken);
            session.RevokedReason = "concurrent-security-action";
            await concurrentContext.SaveChangesAsync(callbackToken);
        });

        await Assert.ThrowsAsync<CloudWriteConflictException>(
            () => revocation.RevokeAllAsync(
                subjectId,
                "manual-revoke",
                budget.Token));

        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.RefreshTokenSessions
            .AsNoTracking()
            .SingleAsync(
                candidate =>
                    candidate.ActorType == IIoTClaimTypes.HumanActor
                    && candidate.SubjectId == subjectId,
                budget.Token);
        Assert.Equal("concurrent-security-action", persisted.RevokedReason);
    }

    [Fact]
    public async Task IndependentHumanSessionRevocation_ShouldSeeSessionCommittedWhileWaitingForSubjectLock()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var subjectId = Guid.NewGuid();
        await using var seedContext = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-session-seed-{Guid.NewGuid():N}");
        var initialSession = new RefreshTokenSession
        {
            Id = Guid.NewGuid(),
            ActorType = IIoTClaimTypes.HumanActor,
            SubjectId = subjectId,
            TokenHash = $"tx-initial-{Guid.NewGuid():N}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        seedContext.RefreshTokenSessions.Add(initialSession);
        await seedContext.SaveChangesAsync(budget.Token);

        await using var lockContext = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-session-lock-holder-{Guid.NewGuid():N}");
        await using var lockTransaction =
            await lockContext.Database.BeginTransactionAsync(budget.Token);
        await RefreshTokenSubjectTransactionLock.AcquireAsync(
            lockContext,
            subjectId,
            budget.Token);

        var revocationApplicationName =
            $"tx-03b3-session-revoke-{Guid.NewGuid():N}";
        await using var revocationContext = CreateRetryContext(
            budget.ConnectionString,
            revocationApplicationName);
        var revocation = new IndependentHumanSessionRevocationService(
            revocationContext);
        var revokeTask = revocation.RevokeAllAsync(
            subjectId,
            "manual-revoke",
            budget.Token);
        await WaitForLockWaitAsync(
            budget.ConnectionString,
            revocationApplicationName,
            budget.Token);

        var lateSessionId = Guid.NewGuid();
        var lateTokenHash = $"tx-late-{Guid.NewGuid():N}";
        await lockContext.Database.ExecuteSqlInterpolatedAsync($"""
            insert into refresh_token_sessions
            (
                "Id", "ActorType", "SubjectId", "TokenHash",
                "CreatedAtUtc", "ExpiresAtUtc"
            )
            values
            (
                {lateSessionId}, {IIoTClaimTypes.HumanActor}, {subjectId},
                {lateTokenHash}, now(), now() + interval '1 hour'
            )
            """, budget.Token);
        await lockTransaction.CommitAsync(budget.Token);

        await Assert.ThrowsAsync<CloudWriteConflictException>(
            () => revokeTask);

        await using var verificationContext = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-session-verify-{Guid.NewGuid():N}");
        var sessions = await verificationContext.RefreshTokenSessions
            .AsNoTracking()
            .Where(session =>
                session.ActorType == IIoTClaimTypes.HumanActor
                && session.SubjectId == subjectId)
            .OrderBy(session => session.Id)
            .ToListAsync(budget.Token);
        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => Assert.Null(session.RevokedAtUtc));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentHumanSessionRevocation_ShouldNotMissOidcGrantCommittedUnderIssuanceLock(
        bool tokenExchange)
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var subjectId = Guid.NewGuid();
        await using var issuanceContext = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-oidc-issuance-{Guid.NewGuid():N}");
        var issuanceLock = new HumanSessionIssuanceLock(
            issuanceContext,
            new HumanSessionIssuanceProcessGate());
        var revocationApplicationName =
            $"tx-03b3-oidc-revoke-{Guid.NewGuid():N}";
        await using var revocationContext = CreateRetryContext(
            budget.ConnectionString,
            revocationApplicationName);
        var revocation = new IndependentHumanSessionRevocationService(
            revocationContext);
        var authorizationId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        Task? revokeTask = null;

        async Task PersistGrantAsync()
        {
            Assert.NotNull(
                issuanceContext.Database.CurrentTransaction);
            revokeTask = revocation.RevokeAllAsync(
                subjectId,
                "manual-revoke",
                budget.Token);
            await WaitForLockWaitAsync(
                budget.ConnectionString,
                revocationApplicationName,
                budget.Token);

            var authorization =
                new OpenIddictEntityFrameworkCoreAuthorization<Guid>
                {
                    Id = authorizationId,
                    Subject = subjectId.ToString(),
                    Status = OpenIddictConstants.Statuses.Valid,
                    Type = "permanent",
                    ConcurrencyToken = Guid.NewGuid().ToString("N")
                };
            issuanceContext.OpenIddictAuthorizations.Add(authorization);
            if (tokenExchange)
            {
                issuanceContext.OpenIddictTokens.Add(
                    new OpenIddictEntityFrameworkCoreToken<Guid>
                    {
                        Id = tokenId,
                        Authorization = authorization,
                        Subject = subjectId.ToString(),
                        Status = OpenIddictConstants.Statuses.Valid,
                        Type = "access_token",
                        ConcurrencyToken = Guid.NewGuid().ToString("N")
                    });
            }

            await issuanceContext.SaveChangesAsync(budget.Token);
        }

        var executed = tokenExchange
            ? await issuanceLock.TryExecuteTokenExchangeAsync(
                PersistGrantAsync,
                budget.Token)
            : await issuanceLock.TryExecuteAuthorizationAsync(
                subjectId,
                PersistGrantAsync,
                budget.Token);
        Assert.True(executed);
        Assert.NotNull(revokeTask);
        await Assert.ThrowsAsync<CloudWriteConflictException>(
            () => revokeTask!);

        await using var verificationContext = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-oidc-verify-{Guid.NewGuid():N}");
        Assert.Equal(
            OpenIddictConstants.Statuses.Valid,
            (await verificationContext.OpenIddictAuthorizations
                .AsNoTracking()
                .SingleAsync(
                    authorization => authorization.Id == authorizationId,
                    budget.Token)).Status);
        Assert.Equal(
            tokenExchange ? 1 : 0,
            await verificationContext.OpenIddictTokens
                .AsNoTracking()
                .CountAsync(token => token.Id == tokenId, budget.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OidcIssuanceSuccessAudit_ShouldCommitAtomicallyWithGrant(
        bool commit)
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var authorizationId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var auditKey = $"oidc-issuance-{Guid.NewGuid():N}";
        await using var context = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-oidc-audit-{Guid.NewGuid():N}");
        var auditTrail = new EfOidcIssuanceAuditTrailService(context);
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(
            async callbackToken =>
            {
                await using var transaction =
                    await context.Database.BeginTransactionAsync(
                        IsolationLevel.ReadCommitted,
                        callbackToken);
                await auditTrail.StageSuccessAsync(
                    new AuditTrailEntry(
                        subjectId,
                        "E-OIDC-AUDIT",
                        "CloudOidcAuthorize",
                        "CloudOidc",
                        "aicopilot",
                        DateTime.UtcNow,
                        true,
                        "OIDC authorize 成功。",
                        IdempotencyKey: auditKey),
                    callbackToken);
                context.OpenIddictAuthorizations.Add(
                    new OpenIddictEntityFrameworkCoreAuthorization<Guid>
                    {
                        Id = authorizationId,
                        Subject = subjectId.ToString(),
                        Status = OpenIddictConstants.Statuses.Valid,
                        Type = "permanent",
                        ConcurrencyToken = Guid.NewGuid().ToString("N")
                    });
                await context.SaveChangesAsync(callbackToken);
                if (commit)
                {
                    await transaction.CommitAsync(callbackToken);
                }
                else
                {
                    await transaction.RollbackAsync(callbackToken);
                }
            },
            budget.Token);

        await using var verificationContext = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-oidc-audit-verify-{Guid.NewGuid():N}");
        Assert.Equal(
            commit ? 1 : 0,
            await verificationContext.AuditTrails
                .AsNoTracking()
                .CountAsync(
                    audit => audit.IdempotencyKey == auditKey,
                    budget.Token));
        Assert.Equal(
            commit ? 1 : 0,
            await verificationContext.OpenIddictAuthorizations
                .AsNoTracking()
                .CountAsync(
                    authorization =>
                        authorization.Id == authorizationId,
                    budget.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OidcIssuanceLock_ShouldRecoverCommittedResponseAfterAcknowledgementLoss(
        bool tokenExchange)
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var context = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-oidc-commit-recovery-{Guid.NewGuid():N}",
            new WriteTrackingCommandInterceptor(interceptor),
            interceptor);
        var auditTrail =
            new EfOidcIssuanceAuditTrailService(
                context,
                () => CreateRetryContext(
                    budget.ConnectionString,
                    $"tx-03b3-oidc-commit-observe-{Guid.NewGuid():N}"));
        var issuanceLock = new HumanSessionIssuanceLock(
            context,
            new HumanSessionIssuanceProcessGate(),
            auditTrail);
        var authorizationId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var auditKey = $"oidc-commit-recovery-{Guid.NewGuid():N}";
        var operationAttempts = 0;
        interceptor.Arm();

        async Task PersistGrantAsync()
        {
            Interlocked.Increment(ref operationAttempts);
            await auditTrail.StageSuccessAsync(
                new AuditTrailEntry(
                    Guid.NewGuid(),
                    "E-OIDC-COMMIT",
                    tokenExchange
                        ? "CloudOidcToken"
                        : "CloudOidcAuthorize",
                    "CloudOidc",
                    "aicopilot",
                    DateTime.UtcNow,
                    true,
                    "OIDC issuance 成功。",
                    IdempotencyKey: auditKey),
                budget.Token);
            var authorization =
                new OpenIddictEntityFrameworkCoreAuthorization<Guid>
                {
                    Id = authorizationId,
                    Subject = Guid.NewGuid().ToString(),
                    Status = OpenIddictConstants.Statuses.Valid,
                    Type = "permanent",
                    ConcurrencyToken = Guid.NewGuid().ToString("N")
                };
            context.OpenIddictAuthorizations.Add(authorization);
            if (tokenExchange)
            {
                context.OpenIddictTokens.Add(
                    new OpenIddictEntityFrameworkCoreToken<Guid>
                    {
                        Id = tokenId,
                        Authorization = authorization,
                        Subject = authorization.Subject,
                        Status = OpenIddictConstants.Statuses.Valid,
                        Type = "access_token",
                        ConcurrencyToken = Guid.NewGuid().ToString("N")
                    });
            }

            await context.SaveChangesAsync(budget.Token);
        }

        var executed = tokenExchange
            ? await issuanceLock.TryExecuteTokenExchangeAsync(
                PersistGrantAsync,
                budget.Token)
            : await issuanceLock.TryExecuteAuthorizationAsync(
                Guid.NewGuid(),
                PersistGrantAsync,
                budget.Token);

        Assert.True(executed);
        Assert.Equal(1, operationAttempts);
        Assert.Equal(1, interceptor.ExceptionsThrown);
        await using var verificationContext = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-oidc-commit-recovery-verify-{Guid.NewGuid():N}");
        Assert.Equal(
            1,
            await verificationContext.AuditTrails
                .AsNoTracking()
                .CountAsync(
                    audit => audit.IdempotencyKey == auditKey,
                    budget.Token));
        Assert.Equal(
            1,
            await verificationContext.OpenIddictAuthorizations
                .AsNoTracking()
                .CountAsync(
                    authorization =>
                        authorization.Id == authorizationId,
                    budget.Token));
        Assert.Equal(
            tokenExchange ? 1 : 0,
            await verificationContext.OpenIddictTokens
                .AsNoTracking()
                .CountAsync(
                    token => token.Id == tokenId,
                    budget.Token));
    }

    [Fact]
    public async Task OidcIssuanceLock_ShouldFailClosedWhenCommitObservationFails()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var context = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-oidc-observation-failure-{Guid.NewGuid():N}",
            new WriteTrackingCommandInterceptor(interceptor),
            interceptor);
        var realAuditTrail =
            new EfOidcIssuanceAuditTrailService(context);
        var auditTrail =
            new ThrowingObservationOidcIssuanceAuditTrailService(
                realAuditTrail);
        var issuanceLock = new HumanSessionIssuanceLock(
            context,
            new HumanSessionIssuanceProcessGate(),
            auditTrail);
        var authorizationId = Guid.NewGuid();
        var auditKey = $"oidc-observation-failure-{Guid.NewGuid():N}";
        interceptor.Arm();

        async Task PersistGrantAsync()
        {
            await auditTrail.StageSuccessAsync(
                new AuditTrailEntry(
                    Guid.NewGuid(),
                    "E-OIDC-UNKNOWN",
                    "CloudOidcToken",
                    "CloudOidc",
                    "aicopilot",
                    DateTime.UtcNow,
                    true,
                    "OIDC token exchange 成功。",
                    IdempotencyKey: auditKey),
                budget.Token);
            context.OpenIddictAuthorizations.Add(
                new OpenIddictEntityFrameworkCoreAuthorization<Guid>
                {
                    Id = authorizationId,
                    Subject = Guid.NewGuid().ToString(),
                    Status = OpenIddictConstants.Statuses.Valid,
                    Type = "permanent",
                    ConcurrencyToken = Guid.NewGuid().ToString("N")
                });
            await context.SaveChangesAsync(budget.Token);
        }

        await Assert.ThrowsAsync<CloudWriteCommitUnknownException>(
            () => issuanceLock.TryExecuteTokenExchangeAsync(
                PersistGrantAsync,
                budget.Token));

        Assert.Equal(1, interceptor.ExceptionsThrown);
        await using var verificationContext = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-oidc-observation-failure-verify-{Guid.NewGuid():N}");
        Assert.Equal(
            1,
            await verificationContext.AuditTrails
                .AsNoTracking()
                .CountAsync(
                    audit => audit.IdempotencyKey == auditKey,
                    budget.Token));
        Assert.Equal(
            1,
            await verificationContext.OpenIddictAuthorizations
                .AsNoTracking()
                .CountAsync(
                    authorization =>
                        authorization.Id == authorizationId,
                    budget.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OidcIssuanceLock_ShouldPropagateCallerCancellationAfterCommit(
        bool tokenExchange)
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        using var callerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                budget.Token);
        var interceptor =
            new CancelOnceAfterWriteCommitInterceptor(
                callerCancellation);
        await using var context = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-oidc-commit-cancel-{Guid.NewGuid():N}",
            new WriteTrackingCommandInterceptor(interceptor),
            interceptor);
        var auditTrail =
            new EfOidcIssuanceAuditTrailService(context);
        var issuanceLock = new HumanSessionIssuanceLock(
            context,
            new HumanSessionIssuanceProcessGate(),
            auditTrail);
        var authorizationId = Guid.NewGuid();
        var auditKey = $"oidc-commit-cancel-{Guid.NewGuid():N}";
        interceptor.Arm();

        async Task PersistGrantAsync()
        {
            await auditTrail.StageSuccessAsync(
                new AuditTrailEntry(
                    Guid.NewGuid(),
                    "E-OIDC-CANCEL",
                    tokenExchange
                        ? "CloudOidcToken"
                        : "CloudOidcAuthorize",
                    "CloudOidc",
                    "aicopilot",
                    DateTime.UtcNow,
                    true,
                    "OIDC issuance 成功。",
                    IdempotencyKey: auditKey),
                callerCancellation.Token);
            context.OpenIddictAuthorizations.Add(
                new OpenIddictEntityFrameworkCoreAuthorization<Guid>
                {
                    Id = authorizationId,
                    Subject = Guid.NewGuid().ToString(),
                    Status = OpenIddictConstants.Statuses.Valid,
                    Type = "permanent",
                    ConcurrencyToken = Guid.NewGuid().ToString("N")
                });
            await context.SaveChangesAsync(
                callerCancellation.Token);
        }

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => tokenExchange
                ? issuanceLock.TryExecuteTokenExchangeAsync(
                    PersistGrantAsync,
                    callerCancellation.Token)
                : issuanceLock.TryExecuteAuthorizationAsync(
                    Guid.NewGuid(),
                    PersistGrantAsync,
                    callerCancellation.Token));

        Assert.Equal(
            callerCancellation.Token,
            exception.CancellationToken);
        Assert.Equal(1, interceptor.ExceptionsThrown);
        await using var verificationContext = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-oidc-commit-cancel-verify-{Guid.NewGuid():N}");
        Assert.Equal(
            1,
            await verificationContext.AuditTrails
                .AsNoTracking()
                .CountAsync(
                    audit => audit.IdempotencyKey == auditKey,
                    budget.Token));
        Assert.Equal(
            1,
            await verificationContext.OpenIddictAuthorizations
                .AsNoTracking()
                .CountAsync(
                    authorization =>
                        authorization.Id == authorizationId,
                    budget.Token));
    }

    [Fact]
    public async Task TokenExchangeProcessGate_ShouldQueueBeforeOpeningAnotherDatabaseConnection()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var processGate = new HumanSessionIssuanceProcessGate();
        var holderApplicationName =
            $"tx-03b3-token-holder-{Guid.NewGuid():N}";
        var waiterApplicationName =
            $"tx-03b3-token-waiter-{Guid.NewGuid():N}";
        await using var holderContext = CreateRetryContext(
            budget.ConnectionString,
            holderApplicationName);
        await using var waiterContext = CreateRetryContext(
            budget.ConnectionString,
            waiterApplicationName);
        var holder = new HumanSessionIssuanceLock(
            holderContext,
            processGate);
        var waiter = new HumanSessionIssuanceLock(
            waiterContext,
            processGate);
        var holderEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHolder = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var holderTask = holder.TryExecuteTokenExchangeAsync(
            async () =>
            {
                holderEntered.SetResult();
                await releaseHolder.Task.WaitAsync(budget.Token);
            },
            budget.Token);
        await holderEntered.Task.WaitAsync(budget.Token);

        try
        {
            var waiterTask = waiter
                .TryExecuteTokenExchangeAsync(
                    () => Task.CompletedTask,
                    budget.Token);
            await WaitForProcessGateWaiterAsync(
                processGate,
                budget.Token);

            Assert.Equal(
                0,
                await CountPostgresSessionsAsync(
                    budget.ConnectionString,
                    waiterApplicationName,
                    budget.Token));

            releaseHolder.SetResult();
            Assert.True(await holderTask.WaitAsync(budget.Token));
            Assert.True(await waiterTask.WaitAsync(budget.Token));
            Assert.Equal(
                1,
                await CountPostgresSessionsAsync(
                    budget.ConnectionString,
                    waiterApplicationName,
                    budget.Token));
        }
        finally
        {
            releaseHolder.TrySetResult();
            await holderTask.WaitAsync(budget.Token);
        }
    }

    [Fact]
    public async Task AuthorizationProcessGate_ShouldQueueSameSubjectBeforeOpeningAnotherDatabaseConnection()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var subjectId = Guid.NewGuid();
        var processGate = new HumanSessionIssuanceProcessGate();
        var holderApplicationName =
            $"tx-03b3-auth-holder-{Guid.NewGuid():N}";
        var waiterApplicationName =
            $"tx-03b3-auth-waiter-{Guid.NewGuid():N}";
        await using var holderContext = CreateRetryContext(
            budget.ConnectionString,
            holderApplicationName);
        await using var waiterContext = CreateRetryContext(
            budget.ConnectionString,
            waiterApplicationName);
        var holder = new HumanSessionIssuanceLock(
            holderContext,
            processGate);
        var waiter = new HumanSessionIssuanceLock(
            waiterContext,
            processGate);
        var holderEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHolder = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var holderTask = holder.TryExecuteAuthorizationAsync(
            subjectId,
            async () =>
            {
                holderEntered.SetResult();
                await releaseHolder.Task.WaitAsync(budget.Token);
            },
            budget.Token);
        await holderEntered.Task.WaitAsync(budget.Token);

        try
        {
            var waiterTask = waiter
                .TryExecuteAuthorizationAsync(
                    subjectId,
                    () => Task.CompletedTask,
                    budget.Token);
            await WaitForAuthorizationProcessGateWaiterAsync(
                processGate,
                subjectId,
                budget.Token);

            Assert.Equal(
                0,
                await CountPostgresSessionsAsync(
                    budget.ConnectionString,
                    waiterApplicationName,
                    budget.Token));

            releaseHolder.SetResult();
            Assert.True(await holderTask.WaitAsync(budget.Token));
            Assert.True(await waiterTask.WaitAsync(budget.Token));
            Assert.Equal(
                1,
                await CountPostgresSessionsAsync(
                    budget.ConnectionString,
                    waiterApplicationName,
                    budget.Token));
        }
        finally
        {
            releaseHolder.TrySetResult();
            await holderTask.WaitAsync(budget.Token);
        }
    }

    [Fact]
    public async Task AuthorizationProcessGate_ShouldCapDistinctSubjectsBeforeOpeningMoreDatabaseConnections()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var processGate = new HumanSessionIssuanceProcessGate();
        var holderApplicationName =
            $"tx-03b3-auth-cap-holders-{Guid.NewGuid():N}";
        var waiterApplicationName =
            $"tx-03b3-auth-cap-waiter-{Guid.NewGuid():N}";
        await using var waiterContext = CreateRetryContext(
            budget.ConnectionString,
            waiterApplicationName);
        var waiter = new HumanSessionIssuanceLock(
            waiterContext,
            processGate);
        var holderContexts = new List<IIoTDbContext>();
        var holderReleases = new List<TaskCompletionSource>();
        var holderTasks = new List<Task<bool>>();
        var waiterEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWaiter = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? waiterTask = null;

        try
        {
            for (var index = 0;
                 index <
                 HumanSessionIssuanceProcessGate
                     .AuthorizationDatabaseLeaseLimit;
                 index++)
            {
                var holderContext = CreateRetryContext(
                    budget.ConnectionString,
                    holderApplicationName);
                holderContexts.Add(holderContext);
                var holder = new HumanSessionIssuanceLock(
                    holderContext,
                    processGate);
                var holderEntered = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseHolder = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                holderReleases.Add(releaseHolder);
                holderTasks.Add(
                    holder.TryExecuteAuthorizationAsync(
                        Guid.NewGuid(),
                        async () =>
                        {
                            holderEntered.SetResult();
                            await releaseHolder.Task.WaitAsync(
                                budget.Token);
                        },
                        budget.Token));
                await holderEntered.Task.WaitAsync(budget.Token);
            }

            Assert.Equal(
                HumanSessionIssuanceProcessGate
                    .AuthorizationDatabaseLeaseLimit,
                await CountPostgresSessionsAsync(
                    budget.ConnectionString,
                    holderApplicationName,
                    budget.Token));

            waiterTask = waiter
                .TryExecuteAuthorizationAsync(
                    Guid.NewGuid(),
                    async () =>
                    {
                        waiterEntered.SetResult();
                        await releaseWaiter.Task.WaitAsync(budget.Token);
                    },
                    budget.Token);
            await WaitForAuthorizationDatabaseLeaseWaiterAsync(
                processGate,
                budget.Token);

            Assert.Equal(
                0,
                await CountPostgresSessionsAsync(
                    budget.ConnectionString,
                    waiterApplicationName,
                    budget.Token));

            holderReleases[0].SetResult();
            Assert.True(
                await holderTasks[0].WaitAsync(budget.Token));
            await waiterEntered.Task.WaitAsync(budget.Token);
            Assert.Equal(
                1,
                await CountPostgresSessionsAsync(
                    budget.ConnectionString,
                    waiterApplicationName,
                    budget.Token));
            releaseWaiter.SetResult();
            Assert.True(await waiterTask.WaitAsync(budget.Token));
            waiterTask = null;
        }
        finally
        {
            foreach (var releaseHolder in holderReleases)
            {
                releaseHolder.TrySetResult();
            }

            releaseWaiter.TrySetResult();
            foreach (var holderTask in holderTasks)
            {
                try
                {
                    await holderTask.WaitAsync(budget.Token);
                }
                catch (OperationCanceledException)
                {
                    // Cleanup observes the test budget cancellation.
                }
            }

            if (waiterTask is not null)
            {
                try
                {
                    await waiterTask.WaitAsync(budget.Token);
                }
                catch (OperationCanceledException)
                {
                    // Cleanup observes the test budget cancellation.
                }
            }

            foreach (var holderContext in holderContexts)
            {
                await holderContext.DisposeAsync();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OidcIssuanceLock_ShouldRetryTransientAdvisoryLockAcquisition(
        bool tokenExchange)
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var interceptor =
            new ThrowOnceOnOidcIssuanceLockInterceptor();
        await using var context = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-oidc-lock-retry-{Guid.NewGuid():N}",
            interceptor);
        var issuanceLock = new HumanSessionIssuanceLock(
            context,
            new HumanSessionIssuanceProcessGate());

        var executed = tokenExchange
            ? await issuanceLock.TryExecuteTokenExchangeAsync(
                () => Task.CompletedTask,
                budget.Token)
            : await issuanceLock.TryExecuteAuthorizationAsync(
                Guid.NewGuid(),
                () => Task.CompletedTask,
                budget.Token);

        Assert.True(executed);
        Assert.Equal(1, interceptor.ExceptionsThrown);
        Assert.Equal(2, interceptor.CommandAttempts);
        Assert.Single(interceptor.AttemptContextIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OidcIssuanceLock_ShouldNotReplayProtectedOperationAfterItStarts(
        bool tokenExchange)
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        await using var context = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-oidc-operation-{Guid.NewGuid():N}");
        var issuanceLock = new HumanSessionIssuanceLock(
            context,
            new HumanSessionIssuanceProcessGate());
        var operationAttempts = 0;

        Task FailAfterStartAsync()
        {
            Interlocked.Increment(ref operationAttempts);
            throw RetryablePostgresException(
                "simulated transient after OIDC issuance started");
        }

        await Assert.ThrowsAsync<PostgresException>(
            () => tokenExchange
                ? issuanceLock.TryExecuteTokenExchangeAsync(
                    FailAfterStartAsync,
                    budget.Token)
                : issuanceLock.TryExecuteAuthorizationAsync(
                    Guid.NewGuid(),
                    FailAfterStartAsync,
                    budget.Token));

        Assert.Equal(1, operationAttempts);
    }

    [Fact]
    public async Task ApiKeyCreateCancellationAfterCommit_ShouldPropagateAndKeepOneRecoverableTarget()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var interceptor = new ThrowOnceAfterCommitInterceptor();
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var service = new EdgeReleaseApiKeyService(dbContext);
        using var callerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
        var name = $"tx-03b3-cancel-{Guid.NewGuid():N}";

        interceptor.Arm(_ =>
        {
            callerCancellation.Cancel();
            return Task.CompletedTask;
        });
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CreateAsync(
                name,
                [ClientReleasePermissions.Read],
                DateTimeOffset.UtcNow.AddDays(30),
                Guid.NewGuid(),
                new EdgeReleaseApiKeyAuditContext(
                    "tx-03b3-admin",
                    DateTime.UtcNow),
                callerCancellation.Token));

        Assert.Equal(callerCancellation.Token, exception.CancellationToken);
        Assert.Equal(1, interceptor.ExceptionsThrown);
        dbContext.ChangeTracker.Clear();
        var key = await dbContext.EdgeReleaseApiKeys
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Name == name, budget.Token);
        Assert.Equal(
            1,
            await dbContext.AuditTrails
                .AsNoTracking()
                .CountAsync(
                    audit =>
                        audit.OperationType == "ClientRelease.ApiKey.Create"
                        && audit.TargetIdOrKey == key.Id.ToString(),
                    budget.Token));
    }

    [Fact]
    public async Task ApiKeyCreateObservationFailure_ShouldReturnCommitUnknownAndKeepCommittedTarget()
    {
        using var budget = await PostgresTestBudget.CreateAsync(
            fixture,
            TimeSpan.FromSeconds(45));
        var commitLoss = new ThrowOnceAfterCommitInterceptor();
        var observationFailure = new FailReadsInterceptor(
            "edge_release_api_keys");
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            commitLoss,
            observationFailure);
        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var service = new EdgeReleaseApiKeyService(dbContext);
        var name = $"tx-03b3-observe-{Guid.NewGuid():N}";
        commitLoss.Arm(_ =>
        {
            observationFailure.Enable();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<CloudWriteCommitUnknownException>(
            () => service.CreateAsync(
                name,
                [ClientReleasePermissions.Read],
                DateTimeOffset.UtcNow.AddDays(30),
                Guid.NewGuid(),
                new EdgeReleaseApiKeyAuditContext(
                    "tx-03b3-admin",
                    DateTime.UtcNow),
                budget.Token));

        Assert.Equal(1, commitLoss.ExceptionsThrown);
        await using var verificationContext = CreateRetryContext(
            budget.ConnectionString,
            $"tx-03b3-observation-verify-{Guid.NewGuid():N}");
        var key = await verificationContext.EdgeReleaseApiKeys
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Name == name, budget.Token);
        Assert.Equal(
            1,
            await verificationContext.AuditTrails
                .AsNoTracking()
                .CountAsync(
                    audit =>
                        audit.OperationType == "ClientRelease.ApiKey.Create"
                        && audit.TargetIdOrKey == key.Id.ToString(),
                    budget.Token));
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
    public async Task DeviceDeleteCommittedAttemptThenRetryCancellation_ShouldRecoverOutsideCallerToken()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                budget.Token);
        var interceptor =
            new CommitThenCancelRetryInterceptor(cancellation);
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var (process, device) = CreateProcessAndDevice("DELRETRYCANCEL");
        process.ClearDomainEvents();
        device.ClearDomainEvents();
        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(budget.Token);
        dbContext.ChangeTracker.Clear();
        var audit = new RecordingAuditTrailService();
        var handler = new DeleteDeviceHandler(
            HumanAdmin(),
            new EfRepository<Device>(dbContext),
            new EfDeviceDeletionDependencyService(dbContext),
            new StubCurrentUserDeviceAccessService
            {
                IsAdministrator = true
            },
            audit,
            new CloudWriteObservationReader(
                services.GetRequiredService<
                    DbContextOptions<IIoTDbContext>>()));

        interceptor.Arm();
        var result = await handler.Handle(
            new DeleteDeviceCommand(device.Id),
            cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(2, interceptor.ExceptionsThrown);
        Assert.Single(audit.Entries);
        Assert.Single(audit.ConfirmedEntries);
        var auditCancellationToken =
            Assert.Single(audit.CancellationTokens);
        Assert.True(auditCancellationToken.CanBeCanceled);
        Assert.False(auditCancellationToken.IsCancellationRequested);
        Assert.NotEqual(cancellation.Token, auditCancellationToken);
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
                    DbContextOptions<IIoTDbContext>>()),
            new EfDeviceClientStateStore(dbContext));

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
    public async Task RolledBackDeleteAttempt_WithConcurrentDeviceMutation_ShouldConflict()
    {
        using var budget = await PostgresTestBudget.CreateAsync(fixture);
        var interceptor = new ChangeDeviceBeforeDeleteRetryInterceptor(
            budget.ConnectionString);
        await using var provider = CreateRetryProvider(
            budget.ConnectionString,
            interceptor);
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dbContext = services.GetRequiredService<IIoTDbContext>();
        var (process, device) = CreateProcessAndDevice("DELETECAS");
        process.ClearDomainEvents();
        device.ClearDomainEvents();
        dbContext.MfgProcesses.Add(process);
        dbContext.Devices.Add(device);
        await dbContext.SaveChangesAsync(budget.Token);
        dbContext.ChangeTracker.Clear();
        var observationReader = new CloudWriteObservationReader(
            services.GetRequiredService<
                DbContextOptions<IIoTDbContext>>());
        var baseline = await observationReader.ObserveDeviceAsync(
            device.Id,
            device.DeviceName,
            device.Code,
            device.ProcessId,
            budget.Token);
        var expectedRowVersion = Assert.IsType<DeviceWriteState>(
            baseline.Target).RowVersion;
        var changedName = $"Concurrent delete CAS {Guid.NewGuid():N}";

        interceptor.Arm(device.Id, changedName);
        var exception = await Assert.ThrowsAsync<
            DeviceDeletionCommitAttemptException>(() =>
            new EfDeviceDeletionDependencyService(dbContext)
                .DeleteCascadeAsync(
                    device.Id,
                    budget.Token,
                    expectedRowVersion));

        var conflict = Assert.IsType<CloudWriteConflictException>(
            exception.InnerException);
        Assert.Equal(CloudWriteConflictException.Code, conflict.ProblemCode);
        Assert.Equal(1, interceptor.ExceptionsThrown);
        dbContext.ChangeTracker.Clear();
        var persisted = await dbContext.Devices
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == device.Id,
                budget.Token);
        Assert.Equal(changedName, persisted.DeviceName);
        Assert.Equal(
            0,
            await CountOutboxAsync(
                dbContext,
                "DeviceDeletedDomainEvent",
                device.Id,
                budget.Token));
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
                observationReader,
                new EfDeviceClientStateStore(dbContext))
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
                services.GetRequiredService<UserManager<ApplicationUser>>(),
                dbContext),
            CreateRolePolicyService(services),
            new EfRepository<Employee>(dbContext),
            CreateUnitOfWork(dbContext),
            HumanAdmin(),
            new RecordingPermissionProvider(),
            CreateEmployeeMutationObserver(services));
    }

    private static async Task VerifyIdentityPolicyAndPasswordRecoveryAsync(
        ServiceProvider provider,
        Action arm,
        Func<int> exceptionsThrown,
        CancellationToken cancellationToken)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<IIoTDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var roles = new RolePolicyService(userManager, roleManager, context);
        var passwords = new IdentityPasswordService(userManager, context);
        var unique = Guid.NewGuid().ToString("N");
        var roleName = $"RetryRole{unique}"[..30];
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"identity-retry-{unique}"[..40],
            IsEnabled = true
        };
        Assert.True((await userManager.CreateAsync(
            user,
            "OldPassword123!")).Succeeded);
        context.ChangeTracker.Clear();

        arm();
        var defined = await roles.DefineRoleAsync(
            roleName,
            [CloudPermissionCatalog.Device.Read],
            cancellationToken);
        Assert.True(defined.IsSuccess && defined.Value);

        arm();
        var roleUpdated = await roles.UpdateRolePermissionsAsync(
            roleName,
            [CloudPermissionCatalog.Recipe.Read],
            cancellationToken);
        Assert.True(roleUpdated.IsSuccess && roleUpdated.Value);

        arm();
        var userUpdated = await roles.UpdateUserPersonalPermissionsAsync(
            user.Id,
            [CloudPermissionCatalog.Device.Read],
            cancellationToken);
        Assert.True(userUpdated.IsSuccess && userUpdated.Value);

        arm();
        var rejectedPassword = await passwords.CheckPasswordAsync(
            user.Id,
            "WrongPassword123!",
            cancellationToken);
        Assert.True(rejectedPassword.IsSuccess);
        Assert.False(rejectedPassword.Value);

        arm();
        var acceptedPassword = await passwords.CheckPasswordAsync(
            user.Id,
            "OldPassword123!",
            cancellationToken);
        Assert.True(acceptedPassword.IsSuccess && acceptedPassword.Value);

        arm();
        var changed = await passwords.ChangePasswordAsync(
            user.Id,
            "OldPassword123!",
            "ChangedPassword123!",
            cancellationToken);
        Assert.True(changed.IsSuccess);

        arm();
        var reset = await passwords.ResetPasswordAsync(
            user.Id,
            "ResetPassword123!",
            cancellationToken);
        Assert.True(reset.IsSuccess && reset.Value);

        Assert.Equal(7, exceptionsThrown());
        context.ChangeTracker.Clear();
        Assert.Equal(
            1,
            await context.Roles
                .AsNoTracking()
                .CountAsync(
                    role => role.NormalizedName == roleName.ToUpperInvariant(),
                    cancellationToken));
        Assert.Equal(
            [CloudPermissionCatalog.Recipe.Read],
            await roles.GetRolePermissionsAsync(roleName));
        Assert.Equal(
            [CloudPermissionCatalog.Device.Read],
            await roles.GetUserPersonalPermissionsAsync(user.Id));
        var persistedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.Id, cancellationToken);
        Assert.Equal(0, persistedUser.AccessFailedCount);
        Assert.Equal(
            PasswordVerificationResult.Success,
            userManager.PasswordHasher.VerifyHashedPassword(
                persistedUser,
                persistedUser.PasswordHash!,
                "ResetPassword123!"));
    }

    private static async Task AssertCallerCancellationAfterCommitAsync(
        ThrowOnceAfterCommitInterceptor interceptor,
        Func<CancellationToken, Task> write,
        CancellationToken testToken)
    {
        using var callerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(testToken);
        interceptor.Arm(_ =>
        {
            callerCancellation.Cancel();
            return Task.CompletedTask;
        });

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => write(callerCancellation.Token));

        Assert.Equal(callerCancellation.Token, exception.CancellationToken);
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
            services.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
            services.GetRequiredService<IIoTDbContext>());

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
        DbTransactionInterceptor interceptor,
        params IInterceptor[] additionalInterceptors)
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

            if (additionalInterceptors.Length > 0)
            {
                options.AddInterceptors(additionalInterceptors);
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

    private static IdentityPasswordService CreatePasswordService(
        IServiceProvider services)
        => new(
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<IIoTDbContext>());

    private static IIoTDbContext CreateRetryContext(
        string connectionString,
        string applicationName,
        params IInterceptor[] interceptors)
    {
        var namedConnectionString = new NpgsqlConnectionStringBuilder(
            connectionString)
        {
            ApplicationName = applicationName
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<IIoTDbContext>();
        options.UseNpgsql(
            namedConnectionString,
            npgsql => npgsql.EnableRetryOnFailure(
                3,
                TimeSpan.FromMilliseconds(50),
                null));
        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        return new IIoTDbContext(options.Options);
    }

    private static async Task WaitForProcessGateWaiterAsync(
        HumanSessionIssuanceProcessGate processGate,
        CancellationToken testToken)
    {
        using var readinessTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(testToken);
        readinessTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            while (processGate.TokenExchangeWaitingCount == 0)
            {
                readinessTimeout.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
        catch (OperationCanceledException)
            when (!testToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Token-exchange operation did not enter the process gate within 10 seconds.");
        }
    }

    private static async Task WaitForAuthorizationProcessGateWaiterAsync(
        HumanSessionIssuanceProcessGate processGate,
        Guid subjectId,
        CancellationToken testToken)
    {
        using var readinessTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(testToken);
        readinessTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            while (processGate.GetAuthorizationWaitingCount(subjectId) == 0)
            {
                readinessTimeout.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
        catch (OperationCanceledException)
            when (!testToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Authorization operation did not enter the process gate within 10 seconds.");
        }
    }

    private static async Task WaitForAuthorizationDatabaseLeaseWaiterAsync(
        HumanSessionIssuanceProcessGate processGate,
        CancellationToken testToken)
    {
        using var readinessTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(testToken);
        readinessTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            while (processGate.AuthorizationDatabaseLeaseWaitingCount == 0)
            {
                readinessTimeout.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
        catch (OperationCanceledException)
            when (!testToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "Authorization operation did not enter the database-lease gate within 10 seconds.");
        }
    }

    private static async Task<int> CountPostgresSessionsAsync(
        string connectionString,
        string applicationName,
        CancellationToken cancellationToken)
    {
        var observerConnectionString = new NpgsqlConnectionStringBuilder(
            connectionString)
        {
            ApplicationName =
                $"token-process-gate-observer-{Guid.NewGuid():N}"
        }.ConnectionString;
        await using var observer = new NpgsqlConnection(
            observerConnectionString);
        await observer.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select count(*)::integer
            from pg_stat_activity
            where application_name = @application_name
            """,
            observer);
        command.Parameters.AddWithValue(
            "application_name",
            applicationName);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
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

    private sealed class PasswordLockConcurrencyBarrierInterceptor(
        int expectedArrivals)
        : DbCommandInterceptor
    {
        private readonly TaskCompletionSource allChecksReached = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int armed;
        private int arrivals;

        public void Arm() => Volatile.Write(ref armed, 1);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref armed) == 0 ||
                !command.CommandText.Contains(
                    "pg_advisory_xact_lock",
                    StringComparison.Ordinal))
            {
                return result;
            }

            var arrival = Interlocked.Increment(ref arrivals);
            if (arrival == expectedArrivals)
            {
                Volatile.Write(ref armed, 0);
                allChecksReached.TrySetResult();
            }

            await allChecksReached.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class FailReadsInterceptor(string tableFragment)
        : DbCommandInterceptor
    {
        private int enabled;

        public void Enable() => Volatile.Write(ref enabled, 1);

        public override ValueTask<InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref enabled) == 1
                && command.CommandText.Contains(
                    tableFragment,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "simulated observation read failure");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailNextReadInterceptor(string tableFragment)
        : DbCommandInterceptor
    {
        private int armed;

        public void Arm() => Volatile.Write(ref armed, 1);

        public override ValueTask<InterceptionResult<DbDataReader>>
            ReaderExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(
                    tableFragment,
                    StringComparison.OrdinalIgnoreCase) &&
                Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                throw new InvalidOperationException(
                    "simulated baseline read failure");
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowOnceOnOidcIssuanceLockInterceptor
        : DbCommandInterceptor
    {
        private readonly object sync = new();
        private readonly HashSet<string> attemptContextIds = [];
        private int armed = 1;
        private int commandAttempts;
        private int exceptionsThrown;

        public int CommandAttempts =>
            Volatile.Read(ref commandAttempts);

        public int ExceptionsThrown =>
            Volatile.Read(ref exceptionsThrown);

        public IReadOnlyCollection<string> AttemptContextIds
        {
            get
            {
                lock (sync)
                {
                    return attemptContextIds.ToArray();
                }
            }
        }

        public override ValueTask<InterceptionResult<int>>
            NonQueryExecutingAsync(
                DbCommand command,
                CommandEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains(
                    "pg_advisory_xact_lock",
                    StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(result);
            }

            Interlocked.Increment(ref commandAttempts);
            lock (sync)
            {
                attemptContextIds.Add(
                    eventData.Context?.ContextId.ToString()
                    ?? "<missing-context>");
            }

            if (Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                Interlocked.Increment(ref exceptionsThrown);
                throw RetryablePostgresException(
                    "simulated transient while acquiring OIDC issuance lock");
            }

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

    private sealed class ThrowingObservationOidcIssuanceAuditTrailService(
        IOidcIssuanceAuditTrailService inner)
        : IOidcIssuanceAuditTrailService
    {
        public Task StageSuccessAsync(
            AuditTrailEntry entry,
            CancellationToken cancellationToken = default)
            => inner.StageSuccessAsync(entry, cancellationToken);

        public Task<bool> IsStagedSuccessCommittedAsync(
            CancellationToken cancellationToken = default)
            => throw new TimeoutException(
                "simulated OIDC commit observation failure");
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

    private sealed class CommitThenCancelRetryInterceptor(
        CancellationTokenSource cancellation)
        : WriteAwareTransactionInterceptor
    {
        private int armed;
        private int writeCommitPending;
        private int cancelRetryTransaction;
        private int exceptionsThrown;

        public int ExceptionsThrown =>
            Volatile.Read(ref exceptionsThrown);

        public void Arm() => Volatile.Write(ref armed, 1);

        public override ValueTask<InterceptionResult<DbTransaction>>
            TransactionStartingAsync(
                DbConnection connection,
                TransactionStartingEventData eventData,
                InterceptionResult<DbTransaction> result,
                CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(
                    ref cancelRetryTransaction,
                    0) == 1)
            {
                cancellation.Cancel();
                Interlocked.Increment(ref exceptionsThrown);
                throw new OperationCanceledException(
                    cancellation.Token);
            }

            return ValueTask.FromResult(result);
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
                Volatile.Write(ref cancelRetryTransaction, 1);
                Interlocked.Increment(ref exceptionsThrown);
                throw RetryablePostgresException(
                    "simulated committed delete before retry cancellation");
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

    private sealed class PauseOnceAfterApiKeyPreflightReadInterceptor
        : DbCommandInterceptor
    {
        private readonly TaskCompletionSource paused = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource resume = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int armed;
        private int pauseClaimed;

        public void Arm() => Volatile.Write(ref armed, 1);

        public Task WaitUntilPausedAsync(CancellationToken cancellationToken)
            => paused.Task.WaitAsync(cancellationToken);

        public void Resume() => resume.TrySetResult();

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref armed) == 1
                && command.Transaction is null
                && command.CommandText.Contains(
                    "edge_release_api_keys",
                    StringComparison.OrdinalIgnoreCase)
                && Interlocked.CompareExchange(
                    ref pauseClaimed,
                    1,
                    0) == 0)
            {
                paused.TrySetResult();
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

    private sealed class ChangeDeviceBeforeDeleteRetryInterceptor(
        string connectionString)
        : DbTransactionInterceptor
    {
        private int armed;
        private int mutateBeforeNextTransaction;
        private int exceptionsThrown;
        private Guid deviceId;
        private string changedName = string.Empty;

        public int ExceptionsThrown => Volatile.Read(ref exceptionsThrown);

        public void Arm(Guid targetDeviceId, string targetName)
        {
            deviceId = targetDeviceId;
            changedName = targetName;
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
                    ref mutateBeforeNextTransaction,
                    0,
                    1) == 1)
            {
                await using var mutationConnection =
                    new NpgsqlConnection(connectionString);
                await mutationConnection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand(
                    """
                    update devices
                    set device_name = @device_name
                    where id = @device_id
                    """,
                    mutationConnection);
                command.Parameters.AddWithValue(
                    "device_name",
                    changedName);
                command.Parameters.AddWithValue(
                    "device_id",
                    deviceId);
                Assert.Equal(
                    1,
                    await command.ExecuteNonQueryAsync(
                        cancellationToken));
            }

            return result;
        }

        public override ValueTask<InterceptionResult>
            TransactionCommittingAsync(
                DbTransaction transaction,
                TransactionEventData eventData,
                InterceptionResult result,
                CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref armed, 0, 1) == 1)
            {
                Volatile.Write(ref mutateBeforeNextTransaction, 1);
                Interlocked.Increment(ref exceptionsThrown);
                throw RetryablePostgresException(
                    "simulated transient before concurrent delete retry");
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
