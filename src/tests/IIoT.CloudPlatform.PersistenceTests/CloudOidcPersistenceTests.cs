using System.Security.Cryptography;
using System.Text;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.IdentityService.Queries;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore;
using OpenIddict.EntityFrameworkCore.Models;
using Xunit;

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class CloudOidcPersistenceTests
{
    [Fact]
    public async Task HumanSessionIssuanceProcessGate_ShouldBoundTokenExchangeQueueAndRecoverCapacity()
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        var processGate = new HumanSessionIssuanceProcessGate();
        IAsyncDisposable? holder =
            await processGate.TryEnterTokenExchangeAsync(timeout.Token)
            ?? throw new InvalidOperationException(
                "Initial token-exchange admission was rejected.");
        var waiterTasks = Enumerable
            .Range(
                0,
                HumanSessionIssuanceProcessGate.TokenExchangeQueueLimit)
            .Select(async _ =>
            {
                await using var waiterLease =
                    await processGate.TryEnterTokenExchangeAsync(
                        timeout.Token)
                    ?? throw new InvalidOperationException(
                        "Admitted token-exchange waiter was rejected.");
            })
            .ToArray();

        try
        {
            while (processGate.TokenExchangeWaitingCount !=
                   HumanSessionIssuanceProcessGate.TokenExchangeQueueLimit)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            Assert.Null(
                await processGate.TryEnterTokenExchangeAsync(timeout.Token));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => processGate
                    .TryEnterTokenExchangeAsync(canceled.Token)
                    .AsTask());

            await holder.DisposeAsync();
            holder = null;
            await Task.WhenAll(waiterTasks).WaitAsync(timeout.Token);

            var recovered =
                await processGate.TryEnterTokenExchangeAsync(timeout.Token);
            Assert.NotNull(recovered);
            await recovered.DisposeAsync();
        }
        finally
        {
            if (holder is not null)
            {
                await holder.DisposeAsync();
            }

            timeout.Cancel();
            try
            {
                await Task.WhenAll(waiterTasks);
            }
            catch (OperationCanceledException)
            {
                // Cleanup observes canceled waiters without masking the assertion.
            }
        }
    }

    [Fact]
    public async Task HumanSessionIssuanceProcessGate_ShouldCapAuthorizationDatabaseLeasesAcrossDistinctSubjects()
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        var processGate = new HumanSessionIssuanceProcessGate();
        var holders = new List<IAsyncDisposable>();
        Task<IAsyncDisposable?>? waiterTask = null;

        try
        {
            for (var index = 0;
                 index <
                 HumanSessionIssuanceProcessGate
                     .AuthorizationDatabaseLeaseLimit;
                 index++)
            {
                holders.Add(
                    await processGate.TryEnterAuthorizationAsync(
                        Guid.NewGuid(),
                        timeout.Token)
                    ?? throw new InvalidOperationException(
                        "Authorization admission was unexpectedly full."));
            }

            waiterTask = processGate
                .TryEnterAuthorizationAsync(
                    Guid.NewGuid(),
                    timeout.Token)
                .AsTask();
            while (processGate.AuthorizationDatabaseLeaseWaitingCount != 1)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            Assert.False(waiterTask.IsCompleted);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => processGate
                    .TryEnterAuthorizationAsync(
                        Guid.NewGuid(),
                        canceled.Token)
                    .AsTask());

            await holders[0].DisposeAsync();
            holders.RemoveAt(0);
            await using var waiter =
                await waiterTask.WaitAsync(timeout.Token)
                ?? throw new InvalidOperationException(
                    "Admitted authorization waiter was rejected.");
            waiterTask = null;
        }
        finally
        {
            timeout.Cancel();
            foreach (var holder in holders)
            {
                await holder.DisposeAsync();
            }

            if (waiterTask is not null)
            {
                try
                {
                    await using var waiter = await waiterTask;
                }
                catch (OperationCanceledException)
                {
                    // Cleanup observes cancellation without masking assertions.
                }
            }
        }
    }

    [Fact]
    public async Task HumanSessionIssuanceProcessGate_ShouldBoundOneSubjectWithoutBlockingAnotherSubject()
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        var processGate = new HumanSessionIssuanceProcessGate();
        var subjectId = Guid.NewGuid();
        IAsyncDisposable? holder =
            await processGate.TryEnterAuthorizationAsync(
                subjectId,
                timeout.Token)
            ?? throw new InvalidOperationException(
                "Initial authorization admission was rejected.");
        var waiterTasks = Enumerable
            .Range(
                0,
                HumanSessionIssuanceProcessGate
                    .AuthorizationPerSubjectRequestLimit - 1)
            .Select(async _ =>
            {
                await using var waiterLease =
                    await processGate.TryEnterAuthorizationAsync(
                        subjectId,
                        timeout.Token)
                    ?? throw new InvalidOperationException(
                        "Admitted authorization waiter was rejected.");
            })
            .ToArray();

        try
        {
            while (processGate.GetAuthorizationWaitingCount(subjectId) !=
                   HumanSessionIssuanceProcessGate
                       .AuthorizationPerSubjectRequestLimit - 1)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            Assert.Null(
                await processGate.TryEnterAuthorizationAsync(
                    subjectId,
                    timeout.Token));
            Assert.Equal(
                HumanSessionIssuanceProcessGate
                    .AuthorizationPerSubjectRequestLimit - 1,
                processGate.GetAuthorizationWaitingCount(subjectId));
            var independentSubjectId = Guid.NewGuid();
            var independent =
                await processGate.TryEnterAuthorizationAsync(
                    independentSubjectId,
                    timeout.Token);
            Assert.NotNull(independent);
            await independent.DisposeAsync();
            Assert.Equal(
                0,
                processGate.GetAuthorizationWaitingCount(
                    independentSubjectId));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => processGate
                    .TryEnterAuthorizationAsync(
                        subjectId,
                        canceled.Token)
                    .AsTask());

            await holder.DisposeAsync();
            holder = null;
            await Task.WhenAll(waiterTasks).WaitAsync(timeout.Token);

            var recovered =
                await processGate.TryEnterAuthorizationAsync(
                    subjectId,
                    timeout.Token);
            Assert.NotNull(recovered);
            await recovered.DisposeAsync();
        }
        finally
        {
            if (holder is not null)
            {
                await holder.DisposeAsync();
            }

            timeout.Cancel();
            try
            {
                await Task.WhenAll(waiterTasks);
            }
            catch (OperationCanceledException)
            {
                // Cleanup observes canceled waiters without masking assertions.
            }
        }
    }

    [Fact]
    public async Task HumanSessionIssuanceProcessGate_ShouldBoundGlobalAuthorizationAdmissionAcrossDistinctSubjects()
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(10));
        var processGate = new HumanSessionIssuanceProcessGate();
        var holders = new List<IAsyncDisposable>();
        var waiterCount =
            HumanSessionIssuanceProcessGate.AuthorizationRequestLimit
            - HumanSessionIssuanceProcessGate
                .AuthorizationDatabaseLeaseLimit;
        var waiterTasks = Array.Empty<Task>();

        try
        {
            for (var index = 0;
                 index <
                 HumanSessionIssuanceProcessGate
                     .AuthorizationDatabaseLeaseLimit;
                 index++)
            {
                holders.Add(
                    await processGate.TryEnterAuthorizationAsync(
                        Guid.NewGuid(),
                        timeout.Token)
                    ?? throw new InvalidOperationException(
                        "Authorization admission was unexpectedly full."));
            }

            waiterTasks = Enumerable
                .Range(0, waiterCount)
                .Select(async _ =>
                {
                    await using var waiter =
                        await processGate.TryEnterAuthorizationAsync(
                            Guid.NewGuid(),
                            timeout.Token)
                        ?? throw new InvalidOperationException(
                            "Admitted authorization waiter was rejected.");
                })
                .ToArray();
            while (processGate.AuthorizationDatabaseLeaseWaitingCount !=
                   waiterCount)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            Assert.Null(
                await processGate.TryEnterAuthorizationAsync(
                    Guid.NewGuid(),
                    timeout.Token));

            foreach (var holder in holders)
            {
                await holder.DisposeAsync();
            }

            holders.Clear();
            await Task.WhenAll(waiterTasks).WaitAsync(timeout.Token);
        }
        finally
        {
            foreach (var holder in holders)
            {
                await holder.DisposeAsync();
            }

            timeout.Cancel();
            try
            {
                await Task.WhenAll(waiterTasks);
            }
            catch (OperationCanceledException)
            {
                // Cleanup observes canceled waiters without masking the assertion.
            }
        }
    }

    [Fact]
    public async Task HumanSessionIssuanceLock_ShouldFailClosedWhenOpenIddictUsesAnotherContext()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(
            new NoopMediator());
        using var lockScope = provider.CreateScope();
        using var storeScope = provider.CreateScope();
        var lockContext =
            lockScope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var storeContext =
            storeScope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var issuanceLock = new HumanSessionIssuanceLock(
            lockContext,
            new OpenIddictEntityFrameworkCoreContext<IIoTDbContext>(
                storeContext),
            new HumanSessionIssuanceProcessGate());
        var operationCalled = false;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => issuanceLock.TryExecuteTokenExchangeAsync(
                () =>
                {
                    operationCalled = true;
                    return Task.CompletedTask;
                }));

        Assert.Contains(
            "same DbContext",
            exception.Message,
            StringComparison.Ordinal);
        Assert.False(operationCalled);
    }

    [Fact]
    public async Task CloudOidcUserProfileService_ShouldReturnAccountAndEmployeeState()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var employee = TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            "E-OIDC-001",
            "Cloud OIDC User",
            accountEnabled: false,
            employeeActive: false);
        var userId = employee.Id;
        await dbContext.SaveChangesAsync();
        var securityStamp = dbContext.Users.Single(user => user.Id == userId).SecurityStamp!;

        var service = new CloudOidcUserProfileService(dbContext);

        var profile = await service.GetByEmployeeNoAsync("E-OIDC-001");

        Assert.NotNull(profile);
        Assert.Equal(userId, profile.UserId);
        Assert.Equal("E-OIDC-001", profile.EmployeeNo);
        Assert.Equal("Cloud OIDC User", profile.RealName);
        Assert.False(profile.AccountEnabled);
        Assert.False(profile.EmployeeActive);
        Assert.Null(profile.TenantId);
        Assert.Equal(
            CloudIdentityStatusVersions.Create(
                userId,
                accountEnabled: false,
                employeeActive: false,
                securityStamp),
            profile.StatusVersion);
    }

    [Fact]
    public async Task CloudIdentityStatusHandler_ShouldReturnDeterministicStatusVersion()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var employee = TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            "E-OIDC-STATUS",
            "Cloud Status User");
        var userId = employee.Id;
        await dbContext.SaveChangesAsync();
        var securityStamp = dbContext.Users.Single(user => user.Id == userId).SecurityStamp!;

        var service = new CloudOidcUserProfileService(dbContext);
        var handler = new GetCloudIdentityStatusHandler(service);

        var result = await handler.Handle(
            new GetCloudIdentityStatusQuery(userId, CloudIdentityTenants.Default),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(userId, result.Value.CloudUserId);
        Assert.Equal(CloudIdentityTenants.Default, result.Value.TenantId);
        Assert.True(result.Value.AccountEnabled);
        Assert.True(result.Value.EmployeeActive);
        Assert.Equal(
            CloudIdentityStatusVersions.Create(
                userId,
                accountEnabled: true,
                employeeActive: true,
                securityStamp),
            result.Value.StatusVersion);
    }

    [Fact]
    public async Task IdentityStatusVersion_ShouldRemainStableForProfileOnlyUpdate()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var employee = TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            "E-OIDC-PROFILE",
            "Original Name");
        await dbContext.SaveChangesAsync();

        var profileService = new CloudOidcUserProfileService(dbContext);
        var before = await profileService.GetByUserIdAsync(employee.Id);

        employee.Rename(employee.EmployeeNo, "Updated Name");
        dbContext.Employees.Update(employee);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var after = await profileService.GetByUserIdAsync(employee.Id);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal("Updated Name", after.RealName);
        Assert.Equal(before.StatusVersion, after.StatusVersion);
    }

    [Fact]
    public async Task IdentityStatusVersion_ShouldNeverReviveAfterDisableAndReenable()
    {
        using var provider = TestServiceProviders.CreateIdentityServiceProvider();
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var employee = TestIdentityData.AddEmployeeWithIdentity(
            dbContext,
            "E-OIDC-REACTIVATE",
            "Reactivated User");
        await dbContext.SaveChangesAsync();

        var profileService = new CloudOidcUserProfileService(dbContext);
        var identityStore = new IdentityAccountStore(
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
            scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>());

        var before = await profileService.GetByUserIdAsync(employee.Id);
        var disabledResult = await identityStore.SetEnabledAsync(employee.Id, false);
        var disabled = await profileService.GetByUserIdAsync(employee.Id);
        var enabledResult = await identityStore.SetEnabledAsync(employee.Id, true);
        var reactivated = await profileService.GetByUserIdAsync(employee.Id);
        var repeatedEnableResult = await identityStore.SetEnabledAsync(employee.Id, true);
        var repeatedActivation = await profileService.GetByUserIdAsync(employee.Id);

        Assert.True(disabledResult.IsSuccess);
        Assert.True(enabledResult.IsSuccess);
        Assert.True(repeatedEnableResult.IsSuccess);
        Assert.NotNull(before);
        Assert.NotNull(disabled);
        Assert.NotNull(reactivated);
        Assert.NotNull(repeatedActivation);
        Assert.False(disabled.AccountEnabled);
        Assert.True(reactivated.AccountEnabled);
        Assert.NotEqual(before.StatusVersion, disabled.StatusVersion);
        Assert.NotEqual(before.StatusVersion, reactivated.StatusVersion);
        Assert.NotEqual(disabled.StatusVersion, reactivated.StatusVersion);
        Assert.NotEqual(reactivated.StatusVersion, repeatedActivation.StatusVersion);
    }

    [Fact]
    public async Task HumanSessionRevocationService_ShouldRevokeRefreshTokensAndAllOidcGrants()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var subjectId = Guid.NewGuid();
        var otherSubjectId = Guid.NewGuid();
        var authorization = new OpenIddictEntityFrameworkCoreAuthorization<Guid>
        {
            Id = Guid.NewGuid(),
            Subject = subjectId.ToString(),
            Status = OpenIddictConstants.Statuses.Valid,
            Type = "permanent"
        };
        var token = new OpenIddictEntityFrameworkCoreToken<Guid>
        {
            Id = Guid.NewGuid(),
            Authorization = authorization,
            Subject = subjectId.ToString(),
            Status = OpenIddictConstants.Statuses.Valid,
            Type = "authorization_code"
        };
        var otherToken = new OpenIddictEntityFrameworkCoreToken<Guid>
        {
            Id = Guid.NewGuid(),
            Subject = otherSubjectId.ToString(),
            Status = OpenIddictConstants.Statuses.Valid,
            Type = "access_token"
        };
        var refreshSession = new RefreshTokenSession
        {
            Id = Guid.NewGuid(),
            ActorType = IIoTClaimTypes.HumanActor,
            SubjectId = subjectId,
            TokenHash = "human-token",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        var machineSession = new RefreshTokenSession
        {
            Id = Guid.NewGuid(),
            ActorType = IIoTClaimTypes.EdgeDeviceActor,
            SubjectId = subjectId,
            TokenHash = "machine-token",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        dbContext.OpenIddictAuthorizations.Add(authorization);
        dbContext.OpenIddictTokens.AddRange(token, otherToken);
        dbContext.RefreshTokenSessions.AddRange(refreshSession, machineSession);
        await dbContext.SaveChangesAsync();

        await using (var transaction =
                     await dbContext.Database.BeginTransactionAsync())
        {
            await new HumanSessionRevocationService(dbContext).RevokeAllAsync(
                subjectId,
                "employee-deactivated");
            await transaction.CommitAsync();
        }
        dbContext.ChangeTracker.Clear();

        var persistedAuthorization = await dbContext.OpenIddictAuthorizations
            .SingleAsync(item => item.Id == authorization.Id);
        var persistedToken = await dbContext.OpenIddictTokens
            .SingleAsync(item => item.Id == token.Id);
        var persistedOtherToken = await dbContext.OpenIddictTokens
            .SingleAsync(item => item.Id == otherToken.Id);
        var persistedHumanSession = await dbContext.RefreshTokenSessions
            .SingleAsync(item => item.Id == refreshSession.Id);
        var persistedMachineSession = await dbContext.RefreshTokenSessions
            .SingleAsync(item => item.Id == machineSession.Id);

        Assert.Equal(OpenIddictConstants.Statuses.Revoked, persistedAuthorization.Status);
        Assert.Equal(OpenIddictConstants.Statuses.Revoked, persistedToken.Status);
        Assert.Equal(OpenIddictConstants.Statuses.Valid, persistedOtherToken.Status);
        Assert.NotNull(persistedHumanSession.RevokedAtUtc);
        Assert.Equal("employee-deactivated", persistedHumanSession.RevokedReason);
        Assert.Null(persistedMachineSession.RevokedAtUtc);
    }

    [Fact]
    public async Task HumanRefreshToken_ShouldPreserveIssuedStatusVersionAcrossRotation()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var service = new EfRefreshTokenService(
            dbContext,
            Options.Create(new RefreshTokenOptions
            {
                HumanMaxActiveSessions = 0
            }));
        var subjectId = Guid.NewGuid();

        var issued = await service.IssueHumanAsync(subjectId, "status-at-login");
        var rotated = await service.RotateAsync(
            IIoTClaimTypes.HumanActor,
            issued.Token);

        Assert.StartsWith("h1.", issued.Token, StringComparison.Ordinal);
        Assert.True(rotated.IsSuccess);
        Assert.Equal(subjectId, rotated.Value!.SubjectId);
        Assert.Equal("status-at-login", rotated.Value.IdentityStatusVersion);
        Assert.StartsWith("h1.", rotated.Value.RefreshToken.Token, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyHumanRefreshTokenWithoutStatusVersion_ShouldBeRevokedOnUse()
    {
        using var provider = TestServiceProviders.CreateEfServiceProvider(new NoopMediator());
        using var scope = provider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        var legacyToken = "legacy-human-refresh-token";
        var session = new RefreshTokenSession
        {
            Id = Guid.NewGuid(),
            ActorType = IIoTClaimTypes.HumanActor,
            SubjectId = Guid.NewGuid(),
            TokenHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(legacyToken))),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        var sibling = new RefreshTokenSession
        {
            Id = Guid.NewGuid(),
            ActorType = IIoTClaimTypes.HumanActor,
            SubjectId = session.SubjectId,
            TokenHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes("independent-active-session"))),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        dbContext.RefreshTokenSessions.AddRange(session, sibling);
        await dbContext.SaveChangesAsync();
        var service = new EfRefreshTokenService(
            dbContext,
            Options.Create(new RefreshTokenOptions()));

        var result = await service.RotateAsync(
            IIoTClaimTypes.HumanActor,
            legacyToken);

        dbContext.ChangeTracker.Clear();
        session = await dbContext.RefreshTokenSessions.SingleAsync(
            candidate => candidate.Id == session.Id);
        sibling = await dbContext.RefreshTokenSessions.SingleAsync(
            candidate => candidate.Id == sibling.Id);
        Assert.False(result.IsSuccess);
        Assert.Equal("status-version-missing", session.RevokedReason);
        Assert.NotNull(session.RevokedAtUtc);
        Assert.Null(sibling.RevokedAtUtc);
        Assert.Null(sibling.RevokedReason);
    }
}
