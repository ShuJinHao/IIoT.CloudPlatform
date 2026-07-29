using IIoT.EmployeeService.Commands.Employees;
using IIoT.EmployeeService.Validators;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Result;
using Xunit;

namespace IIoT.CloudPlatform.ApplicationTests;

public sealed class EmployeeRoleAssignmentTests
{
    [Fact]
    public void Command_ShouldUseOnlyEmployeeUpdateAccessAndEmployeeLock()
    {
        var commandType = typeof(UpdateEmployeeRoleCommand);
        var permission = Assert.Single(
            commandType.GetCustomAttributes(typeof(AuthorizeRequirementAttribute), true)
                .Cast<AuthorizeRequirementAttribute>());
        var distributedLock = Assert.Single(
            commandType.GetCustomAttributes(typeof(DistributedLockAttribute), true)
                .Cast<DistributedLockAttribute>());

        Assert.Equal(CloudPermissionCatalog.Employee.UpdateAccess, permission.Permission);
        Assert.Equal("iiot:lock:employee:{EmployeeId}", distributedLock.KeyTemplate);
        Assert.Equal(5, distributedLock.TimeoutSeconds);
        Assert.Null(commandType.GetCustomAttributes(inherit: true)
            .SingleOrDefault(attribute => attribute.GetType().Name == "AdminOnlyAttribute"));
    }

    [Theory]
    [InlineData("HrAdmin")]
    [InlineData(SystemRoles.Admin)]
    public async Task HumanHrAdminAndAdmin_ShouldAssignCanonicalRoleAndRevokeSessions(
        string callerRole)
    {
        var actorId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        var rolePolicy = new StubRolePolicyService
        {
            Roles = ["ProductionViewer", "RoleAdmin"]
        };
        var unitOfWork = new RecordingUnitOfWork();
        var sessions = new StubHumanSessionRevocationService();
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            rolePolicy,
            unitOfWork,
            sessions,
            new StubAdminTargetGuard(),
            Human(actorId, callerRole),
            audit);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, "  roleadmin  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([(targetId, "RoleAdmin")], identityStore.ReplacedRoles);
        Assert.Equal([targetId], identityStore.StateCompareExchanges.Select(change => change.UserId));
        Assert.Equal([(targetId, "employee-role-changed")], sessions.Revocations);
        Assert.Equal(1, unitOfWork.ExecuteResilientCalls);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(1, unitOfWork.CommitCalls);
        Assert.Equal(0, unitOfWork.RollbackCalls);
        var entry = Assert.Single(audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.Equal("Employee.Role.Update", entry.OperationType);
        Assert.Equal(targetId.ToString(), entry.TargetIdOrKey);
        Assert.Contains("\"beforeRoles\":[\"ProductionViewer\"]", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("\"afterRoles\":[\"RoleAdmin\"]", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("\"requestedRole\":\"roleadmin\"", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("\"canonicalRole\":\"RoleAdmin\"", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("\"resultCode\":\"Succeeded\"", entry.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NullRole_ShouldClearAssignableRolesWithoutChangingOtherAccessData()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        var unitOfWork = new RecordingUnitOfWork();
        var sessions = new StubHumanSessionRevocationService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer"] },
            unitOfWork,
            sessions,
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            new RecordingAuditTrailService());

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, null),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([(targetId, (string?)null)], identityStore.ReplacedRoles);
        Assert.Empty(identityStore.RolesByUserId[targetId]);
        Assert.Equal([targetId], identityStore.StateCompareExchanges.Select(change => change.UserId));
        Assert.Single(sessions.Revocations);
        Assert.Equal(1, unitOfWork.CommitCalls);
    }

    [Fact]
    public async Task SameCanonicalRole_ShouldBeIdempotentWithoutVersionRotationOrRevocation()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "RoleAdmin");
        var unitOfWork = new RecordingUnitOfWork();
        var sessions = new StubHumanSessionRevocationService();
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["RoleAdmin"] },
            unitOfWork,
            sessions,
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, " roleadmin "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(identityStore.ReplacedRoles);
        Assert.Empty(identityStore.StateCompareExchanges);
        Assert.Empty(sessions.Revocations);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(1, unitOfWork.RollbackCalls);
        Assert.Contains(
            "\"resultCode\":\"NoChange\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankRole_ShouldBeRejectedWithoutTreatingItAsClear(string roleName)
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer"] },
            new RecordingUnitOfWork(),
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, roleName),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(identityStore.ReplacedRoles);
        Assert.Empty(identityStore.StateCompareExchanges);
        Assert.Contains(
            "\"resultCode\":\"RoleNameBlank\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OverlongRole_ShouldBeRejectedThroughStructuredAudit()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        var rolePolicy = new StubRolePolicyService { Roles = ["ProductionViewer"] };
        var targetGuard = new StubAdminTargetGuard();
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            rolePolicy,
            new RecordingUnitOfWork(),
            new StubHumanSessionRevocationService(),
            targetGuard,
            Human(Guid.NewGuid(), "HrAdmin"),
            audit);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, new string('R', 257)),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, rolePolicy.GetAllRolesCalls);
        Assert.Equal(0, targetGuard.Calls);
        Assert.Empty(identityStore.ReplacedRoles);
        Assert.Contains(
            "\"resultCode\":\"RoleNameTooLong\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData(" admin ")]
    [InlineData("ADMIN")]
    [InlineData("UnknownRole")]
    public async Task IllegalRole_ShouldBeRejectedBeforeTargetOrExistingRoleReads(string roleName)
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        var targetGuard = new StubAdminTargetGuard();
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer", SystemRoles.Admin] },
            new RecordingUnitOfWork(),
            new StubHumanSessionRevocationService(),
            targetGuard,
            Human(Guid.NewGuid(), "HrAdmin"),
            audit);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, roleName),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, targetGuard.Calls);
        Assert.Equal(0, identityStore.GetRolesCalls);
        Assert.Empty(identityStore.ReplacedRoles);
        Assert.Empty(identityStore.StateCompareExchanges);
        var expectedCode = SystemRoles.IsAdminLike(roleName)
            ? "AdminRoleNotAssignable"
            : "RoleNotFound";
        Assert.Contains(
            $"\"resultCode\":\"{expectedCode}\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonAdmin_ShouldNotChangeOwnRole()
    {
        var actorId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(actorId, "HrAdmin");
        var targetGuard = new StubAdminTargetGuard();
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["HrAdmin", "ProductionViewer"] },
            new RecordingUnitOfWork(),
            new StubHumanSessionRevocationService(),
            targetGuard,
            Human(actorId, "HrAdmin"),
            audit);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(actorId, "ProductionViewer"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, targetGuard.Calls);
        Assert.Empty(identityStore.ReplacedRoles);
        Assert.Empty(identityStore.StateCompareExchanges);
        Assert.Contains(
            "\"resultCode\":\"SelfRoleChangeForbidden\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminTarget_ShouldBeRejectedBeforeTransactionOrRoleMutation()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, SystemRoles.Admin);
        var targetGuard = new StubAdminTargetGuard
        {
            GuardResult = Result.Failure(AdminTargetProtectionErrors.AdminTargetProtected)
        };
        var unitOfWork = new RecordingUnitOfWork();
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer"] },
            unitOfWork,
            new StubHumanSessionRevocationService(),
            targetGuard,
            Human(Guid.NewGuid(), SystemRoles.Admin),
            audit);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, "ProductionViewer"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(identityStore.ReplacedRoles);
        Assert.Empty(identityStore.StateCompareExchanges);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(1, unitOfWork.RollbackCalls);
        Assert.Contains(
            "\"resultCode\":\"AdminTargetProtected\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingTarget_ShouldBeRejectedBeforeTransactionOrRoleMutation()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(Guid.NewGuid(), "ProductionViewer");
        var targetGuard = new StubAdminTargetGuard
        {
            GuardResult = Result.Failure(AdminTargetProtectionErrors.TargetNotFound)
        };
        var unitOfWork = new RecordingUnitOfWork();
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer"] },
            unitOfWork,
            new StubHumanSessionRevocationService(),
            targetGuard,
            Human(Guid.NewGuid(), "HrAdmin"),
            audit);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, "ProductionViewer"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(identityStore.ReplacedRoles);
        Assert.Empty(identityStore.StateCompareExchanges);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(1, unitOfWork.RollbackCalls);
        Assert.Contains(
            "\"resultCode\":\"TargetNotFound\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingEmployeeRecord_ShouldBeRejectedBeforeRoleReadOrTransaction()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        var unitOfWork = new RecordingUnitOfWork();
        var employeeLookup = new StubEmployeeLookupService();
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer"] },
            unitOfWork,
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit,
            employeeLookup);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, "ProductionViewer"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, employeeLookup.GetByIdCalls);
        Assert.Equal(0, identityStore.GetRolesCalls);
        Assert.Empty(identityStore.ReplacedRoles);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(1, unitOfWork.RollbackCalls);
        Assert.Contains(
            "\"resultCode\":\"TargetNotFound\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, IIoTClaimTypes.HumanActor)]
    [InlineData(true, IIoTClaimTypes.EdgeDeviceActor)]
    [InlineData(true, IIoTClaimTypes.AiServiceActor)]
    [InlineData(true, IIoTClaimTypes.EdgeReleasePublisherActor)]
    public async Task NonHumanOrAnonymousActor_ShouldBeRejectedWithoutTargetOrRoleAccess(
        bool authenticated,
        string actorType)
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        var rolePolicy = new StubRolePolicyService { Roles = ["RoleAdmin"] };
        var targetGuard = new StubAdminTargetGuard();
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            rolePolicy,
            new RecordingUnitOfWork(),
            new StubHumanSessionRevocationService(),
            targetGuard,
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "machine-or-anonymous",
                ActorType = actorType,
                IsAuthenticated = authenticated
            },
            audit);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, "RoleAdmin"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, rolePolicy.GetAllRolesCalls);
        Assert.Equal(0, targetGuard.Calls);
        Assert.Equal(0, identityStore.GetRolesCalls);
        Assert.Empty(identityStore.ReplacedRoles);
        Assert.Contains(
            "\"resultCode\":\"HumanActorRequired\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task VersionRotationFailure_ShouldRollbackAndSkipSessionRevocation()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        identityStore.CompareExchangeStateResult = Result.Failure("rotation failed");
        var unitOfWork = new RecordingUnitOfWork();
        var sessions = new StubHumanSessionRevocationService();
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer", "RoleAdmin"] },
            unitOfWork,
            sessions,
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, "RoleAdmin"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, unitOfWork.RollbackCalls);
        Assert.Equal(0, unitOfWork.CommitCalls);
        Assert.Empty(sessions.Revocations);
        Assert.Contains(
            "\"resultCode\":\"StatusVersionRotationFailed\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionRevocationFailure_ShouldRollbackAndRecordStableAuditCode()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        var unitOfWork = new RecordingUnitOfWork();
        var sessions = new StubHumanSessionRevocationService
        {
            ExceptionToThrow = new InvalidOperationException("sensitive persistence detail")
        };
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer", "RoleAdmin"] },
            unitOfWork,
            sessions,
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(
                new UpdateEmployeeRoleCommand(targetId, "RoleAdmin"),
                CancellationToken.None));

        Assert.Equal(1, unitOfWork.RollbackCalls);
        Assert.Equal(0, unitOfWork.CommitCalls);
        var entry = Assert.Single(audit.Entries);
        Assert.Contains("\"resultCode\":\"TransactionFailed\"", entry.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive persistence detail", entry.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive persistence detail", entry.FailureReason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessAudit_ShouldCompleteWhenRequestIsCanceledAfterCommit()
    {
        using var cancellation = new CancellationTokenSource();
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        var unitOfWork = new RecordingUnitOfWork
        {
            OnCommit = cancellation.Cancel
        };
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer", "RoleAdmin"] },
            unitOfWork,
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, "RoleAdmin"),
            cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(Assert.Single(audit.CancellationTokens).CanBeCanceled);
        Assert.Contains(
            "\"resultCode\":\"Succeeded\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BaselineReplay_ShouldReuseTargetStampAndWriteOneSuccessAudit()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        identityStore.SecurityStampsByUserId[targetId] = "baseline-stamp";
        var audit = new RecordingAuditTrailService();
        var unitOfWork = new ReplayOnceUnitOfWork(() =>
        {
            identityStore.RolesByUserId[targetId] = ["ProductionViewer"];
            identityStore.SecurityStampsByUserId[targetId] = "baseline-stamp";
            var account = identityStore.AccountById!;
            if (!account.IsEnabled)
            {
                account.Enable();
            }
        });
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer", "RoleAdmin"] },
            unitOfWork,
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, "RoleAdmin"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, unitOfWork.CommitCalls);
        Assert.Equal(2, identityStore.StateCompareExchanges.Count);
        Assert.Single(
            identityStore.StateCompareExchanges
                .Select(change => change.SecurityStamp)
                .Distinct(StringComparer.Ordinal));
        var entry = Assert.Single(audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(entry.IdempotencyKey));
        Assert.Contains(
            "\"resultCode\":\"Succeeded\"",
            entry.Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitConfirmationLoss_WithExactTarget_ShouldRecoverAndAuditOnce()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        identityStore.SecurityStampsByUserId[targetId] = "baseline-stamp";
        var unitOfWork = new RecordingUnitOfWork
        {
            OnCommit = () => throw new InvalidOperationException("commit acknowledgement lost")
        };
        var observer = new StubEmployeeMutationObservationReader
        {
            ObserveAsyncOverride = (_, _) => Task.FromResult(
                ObserveCurrent(identityStore, targetId, employeeIsActive: true))
        };
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer", "RoleAdmin"] },
            unitOfWork,
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit,
            mutationObservationReader: observer);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, "RoleAdmin"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, observer.Calls);
        var entry = Assert.Single(audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(entry.IdempotencyKey));
        Assert.Contains(
            "\"resultCode\":\"CommitRecovered\"",
            entry.Summary,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TransactionFailed",
            entry.Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitConfirmationLoss_WithAdminRoleDrift_ShouldConflictAndAuditObservedRole()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        identityStore.SecurityStampsByUserId[targetId] = "baseline-stamp";
        var observer = new StubEmployeeMutationObservationReader
        {
            ObserveAsyncOverride = (_, _) =>
            {
                var current = ObserveCurrent(
                    identityStore,
                    targetId,
                    employeeIsActive: true);
                return Task.FromResult(
                    current with
                    {
                        Roles = [.. current.Roles, " admin "]
                    });
            }
        };
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer", "RoleAdmin"] },
            new RecordingUnitOfWork
            {
                OnCommit = () => throw new InvalidOperationException(
                    "commit acknowledgement lost")
            },
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit,
            mutationObservationReader: observer);

        await Assert.ThrowsAsync<EmployeeRoleUpdateConflictException>(() =>
            handler.Handle(
                new UpdateEmployeeRoleCommand(targetId, "RoleAdmin"),
                CancellationToken.None));

        Assert.Equal(1, observer.Calls);
        var entry = Assert.Single(audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Contains(
            "\"resultCode\":\"CommitConflict\"",
            entry.Summary,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"admin\"",
            entry.Summary,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "\"resultCode\":\"CommitRecovered\"",
            entry.Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitConfirmationLoss_WithBaselineOnly_ShouldReturnCommitUnknownAndAuditOnce()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        identityStore.SecurityStampsByUserId[targetId] = "baseline-stamp";
        var observer = new StubEmployeeMutationObservationReader
        {
            Observation = new EmployeeMutationObservation(
                EmployeeExists: true,
                EmployeeIsActive: true,
                AccountExists: true,
                AccountIsEnabled: true,
                AccountSecurityStamp: "baseline-stamp",
                Roles: ["ProductionViewer"])
        };
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer", "RoleAdmin"] },
            new RecordingUnitOfWork
            {
                OnCommit = () => throw new InvalidOperationException("commit acknowledgement lost")
            },
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit,
            mutationObservationReader: observer);

        await Assert.ThrowsAsync<EmployeeRoleUpdateCommitUnknownException>(() =>
            handler.Handle(
                new UpdateEmployeeRoleCommand(targetId, "RoleAdmin"),
                CancellationToken.None));

        Assert.Equal(1, observer.Calls);
        var entry = Assert.Single(audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Contains(
            "\"resultCode\":\"CommitUnknown\"",
            entry.Summary,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TransactionFailed",
            entry.Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitConfirmationLoss_WithDrift_ShouldReturnConflictAndAuditOnce()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        identityStore.SecurityStampsByUserId[targetId] = "baseline-stamp";
        var observer = new StubEmployeeMutationObservationReader
        {
            Observation = new EmployeeMutationObservation(
                EmployeeExists: true,
                EmployeeIsActive: true,
                AccountExists: true,
                AccountIsEnabled: false,
                AccountSecurityStamp: "newer-stamp",
                Roles: ["DifferentRole"])
        };
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer", "RoleAdmin"] },
            new RecordingUnitOfWork
            {
                OnCommit = () => throw new InvalidOperationException("commit acknowledgement lost")
            },
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit,
            mutationObservationReader: observer);

        await Assert.ThrowsAsync<EmployeeRoleUpdateConflictException>(() =>
            handler.Handle(
                new UpdateEmployeeRoleCommand(targetId, "RoleAdmin"),
                CancellationToken.None));

        Assert.Equal(1, observer.Calls);
        var entry = Assert.Single(audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Contains(
            "\"resultCode\":\"CommitConflict\"",
            entry.Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitObservationFailure_ShouldReturnCommitUnknownWithoutASecondAudit()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        identityStore.SecurityStampsByUserId[targetId] = "baseline-stamp";
        var observer = new StubEmployeeMutationObservationReader
        {
            ExceptionToThrow = new InvalidOperationException("sensitive observation failure")
        };
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer", "RoleAdmin"] },
            new RecordingUnitOfWork
            {
                OnCommit = () => throw new InvalidOperationException("commit acknowledgement lost")
            },
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit,
            mutationObservationReader: observer);

        await Assert.ThrowsAsync<EmployeeRoleUpdateCommitUnknownException>(() =>
            handler.Handle(
                new UpdateEmployeeRoleCommand(targetId, "RoleAdmin"),
                CancellationToken.None));

        var entry = Assert.Single(audit.Entries);
        Assert.DoesNotContain(
            "sensitive observation failure",
            entry.Summary,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "sensitive observation failure",
            entry.FailureReason ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationDuringCommit_ShouldResolveExactCommittedState()
    {
        var targetId = Guid.NewGuid();
        var identityStore = CreateIdentityStore(targetId, "ProductionViewer");
        identityStore.SecurityStampsByUserId[targetId] = "baseline-stamp";
        var observer = new StubEmployeeMutationObservationReader
        {
            ObserveAsyncOverride = (_, _) => Task.FromResult(
                ObserveCurrent(identityStore, targetId, employeeIsActive: true))
        };
        var audit = new RecordingAuditTrailService();
        var handler = CreateHandler(
            identityStore,
            new StubRolePolicyService { Roles = ["ProductionViewer", "RoleAdmin"] },
            new RecordingUnitOfWork
            {
                OnCommit = () => throw new OperationCanceledException(
                    new CancellationToken(canceled: true))
            },
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            Human(Guid.NewGuid(), "HrAdmin"),
            audit,
            mutationObservationReader: observer);

        var result = await handler.Handle(
            new UpdateEmployeeRoleCommand(targetId, "RoleAdmin"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, observer.Calls);
        Assert.Contains(
            "\"resultCode\":\"CommitRecovered\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "\"resultCode\":\"Canceled\"",
            Assert.Single(audit.Entries).Summary,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_ShouldRouteRoleInputIntoAuditedHandler()
    {
        var validator = new UpdateEmployeeRoleCommandValidator();

        Assert.True(validator.Validate(
            new UpdateEmployeeRoleCommand(Guid.NewGuid(), null)).IsValid);
        Assert.True(validator.Validate(
            new UpdateEmployeeRoleCommand(Guid.NewGuid(), string.Empty)).IsValid);
        Assert.True(validator.Validate(
            new UpdateEmployeeRoleCommand(Guid.NewGuid(), "   ")).IsValid);
        Assert.True(validator.Validate(
            new UpdateEmployeeRoleCommand(Guid.NewGuid(), new string('R', 257))).IsValid);
        Assert.False(validator.Validate(
            new UpdateEmployeeRoleCommand(Guid.Empty, "ProductionViewer")).IsValid);
    }

    private static RecordingIdentityAccountStore CreateIdentityStore(
        Guid targetId,
        params string[] roles)
    {
        var store = new RecordingIdentityAccountStore
        {
            AccountById = IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                targetId,
                $"E-{targetId:N}")
        };
        store.RolesByUserId[targetId] = [.. roles];
        return store;
    }

    private static TestCurrentUser Human(Guid id, string role)
        => new()
        {
            Id = id.ToString(),
            UserName = $"human-{id:N}",
            Roles = [role],
            ActorType = IIoTClaimTypes.HumanActor,
            IsAuthenticated = true
        };

    private static UpdateEmployeeRoleHandler CreateHandler(
        RecordingIdentityAccountStore identityStore,
        StubRolePolicyService rolePolicyService,
        IUnitOfWork unitOfWork,
        StubHumanSessionRevocationService sessionRevocationService,
        StubAdminTargetGuard targetGuard,
        TestCurrentUser currentUser,
        RecordingAuditTrailService auditTrailService,
        StubEmployeeLookupService? employeeLookupService = null,
        IEmployeeMutationObservationReader? mutationObservationReader = null)
        => new(
            identityStore,
            rolePolicyService,
            unitOfWork,
            sessionRevocationService,
            targetGuard,
            employeeLookupService ?? new StubEmployeeLookupService
            {
                Employee = identityStore.AccountById is { } account
                    ? new EmployeeLookupDto(
                        account.Id,
                        account.EmployeeNo,
                        "Target Employee",
                        IsActive: true,
                        DeviceIds: [])
                    : null
            },
            mutationObservationReader ?? new StubEmployeeMutationObservationReader(),
            currentUser,
            auditTrailService);

    private static EmployeeMutationObservation ObserveCurrent(
        RecordingIdentityAccountStore identityStore,
        Guid employeeId,
        bool employeeIsActive)
    {
        identityStore.SecurityStampsByUserId.TryGetValue(
            employeeId,
            out var securityStamp);
        return new EmployeeMutationObservation(
            EmployeeExists: true,
            EmployeeIsActive: employeeIsActive,
            AccountExists: identityStore.AccountById?.Id == employeeId,
            AccountIsEnabled: identityStore.AccountById?.IsEnabled == true,
            AccountSecurityStamp: securityStamp,
            Roles: identityStore.RolesByUserId.TryGetValue(
                employeeId,
                out var roles)
                ? roles.ToArray()
                : []);
    }

    private sealed class ReplayOnceUnitOfWork(Action resetAfterFirstAttempt)
        : IUnitOfWork
    {
        private bool firstCommit = true;

        public int CommitCalls { get; private set; }

        public async Task<TResult> ExecuteResilientAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (RetryOnceException)
            {
                resetAfterFirstAttempt();
                return await operation(cancellationToken);
            }
        }

        public Task BeginTransactionAsync(
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            if (firstCommit)
            {
                firstCommit = false;
                throw new RetryOnceException();
            }

            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class RetryOnceException : Exception
    {
    }
}
