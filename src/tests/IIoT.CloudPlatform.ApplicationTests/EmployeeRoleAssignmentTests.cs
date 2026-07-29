using IIoT.EmployeeService.Commands.Employees;
using IIoT.EmployeeService.Validators;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
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
        Assert.Equal([targetId], identityStore.RotatedSecurityStampIds);
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
        Assert.Equal([targetId], identityStore.RotatedSecurityStampIds);
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
        Assert.Empty(identityStore.RotatedSecurityStampIds);
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
        Assert.Empty(identityStore.RotatedSecurityStampIds);
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
        Assert.Empty(identityStore.RotatedSecurityStampIds);
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
        Assert.Empty(identityStore.RotatedSecurityStampIds);
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
        Assert.Empty(identityStore.RotatedSecurityStampIds);
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
        Assert.Empty(identityStore.RotatedSecurityStampIds);
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
        identityStore.RotateSecurityStampResult = Result.Failure("rotation failed");
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
        RecordingUnitOfWork unitOfWork,
        StubHumanSessionRevocationService sessionRevocationService,
        StubAdminTargetGuard targetGuard,
        TestCurrentUser currentUser,
        RecordingAuditTrailService auditTrailService,
        StubEmployeeLookupService? employeeLookupService = null)
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
            currentUser,
            auditTrailService);
}
