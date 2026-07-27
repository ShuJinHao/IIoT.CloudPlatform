using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.IdentityService.Commands;
using IIoT.IdentityService.Queries;
using IIoT.ProductionService.Commands.Bootstrap.Devices;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.ProductionService.Commands.Devices;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Authorization;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Exceptions;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using Xunit;

namespace IIoT.CloudPlatform.ApplicationTests;

public sealed class AuthorizationAndIdentityBehaviorTests
{
    [Fact]
    public async Task CurrentUserDeviceAccessService_ShouldReturnAllDeviceScopeForAdminWithoutPermissionQuery()
    {
        var permissionService = new StubDevicePermissionService
        {
            AccessibleDeviceIds = [Guid.NewGuid()]
        };
        var service = new CurrentUserDeviceAccessService(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Roles = [SystemRoles.Admin],
                IsAuthenticated = true
            },
            permissionService);

        var scope = await service.GetAccessibleDeviceIdsAsync();

        Assert.True(scope.IsSuccess);
        Assert.Null(scope.Value);
        Assert.Equal(0, permissionService.GetAccessibleDeviceIdsCalls);
    }

    [Fact]
    public async Task CurrentUserDeviceAccessService_ShouldReturnAssignedDeviceScopeForOperator()
    {
        var userId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var permissionService = new StubDevicePermissionService
        {
            AccessibleDeviceIds = [deviceId]
        };
        var service = new CurrentUserDeviceAccessService(
            new TestCurrentUser
            {
                Id = userId.ToString(),
                Roles = ["Operator"],
                IsAuthenticated = true
            },
            permissionService);

        var scope = await service.GetAccessibleDeviceIdsAsync();

        Assert.True(scope.IsSuccess);
        Assert.Equal([deviceId], scope.Value);
        Assert.Equal(1, permissionService.GetAccessibleDeviceIdsCalls);
        Assert.Equal(userId, permissionService.LastUserId);
    }

    [Fact]
    public async Task CurrentUserDeviceAccessService_ShouldFailForInvalidUserIdWithoutPermissionQuery()
    {
        var permissionService = new StubDevicePermissionService();
        var service = new CurrentUserDeviceAccessService(
            new TestCurrentUser
            {
                Id = "not-a-guid",
                Roles = ["Operator"],
                IsAuthenticated = true
            },
            permissionService);

        var scope = await service.GetAccessibleDeviceIdsAsync();

        Assert.False(scope.IsSuccess);
        Assert.Contains("用户凭证异常", scope.Errors!);
        Assert.Equal(0, permissionService.GetAccessibleDeviceIdsCalls);
    }

    [Fact]
    public async Task CurrentUserDeviceAccessService_ShouldRejectUnassignedDeviceForOperator()
    {
        var allowedDeviceId = Guid.NewGuid();
        var deniedDeviceId = Guid.NewGuid();
        var service = new CurrentUserDeviceAccessService(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Roles = ["Operator"],
                IsAuthenticated = true
            },
            new StubDevicePermissionService
            {
                AccessibleDeviceIds = [allowedDeviceId]
            });

        var access = await service.EnsureCanAccessDeviceAsync(deniedDeviceId);

        Assert.False(access.IsSuccess);
        Assert.Contains("越权", access.Errors!.Single());
    }

    [Fact]
    public async Task AdminTargetGuard_ShouldUsePersistedTrimmedCaseInsensitiveAdminRole()
    {
        var targetUserId = Guid.NewGuid();
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById = IdentityAccount.Create(targetUserId, "A0001")
        };
        identityStore.RolesByUserId[targetUserId] = ["  aDmIn  "];
        var guard = new AdminTargetGuard(identityStore);

        var result = await guard.EnsureMutableNonAdminTargetAsync(targetUserId);

        Assert.False(result.IsSuccess);
        Assert.Contains(AdminTargetProtectionErrors.AdminTargetProtected, result.Errors!);
    }

    [Fact]
    public async Task AdminTargetGuard_ShouldReturnUnifiedErrorForMissingTarget()
    {
        var guard = new AdminTargetGuard(new RecordingIdentityAccountStore());

        var result = await guard.EnsureMutableNonAdminTargetAsync(Guid.NewGuid());

        Assert.False(result.IsSuccess);
        Assert.Contains(AdminTargetProtectionErrors.TargetNotFound, result.Errors!);
    }

    [Fact]
    public void CloudPermissionCatalog_ShouldCanonicalizeTrimAndCaseWhileRejectingUnknownInput()
    {
        var valid = CloudPermissionCatalog.Normalize(
            [" device.read ", "DEVICE.READ", " recipe.update "]);
        var invalid = CloudPermissionCatalog.Normalize(
            ["Device.Read", "Permission.Forged"]);

        Assert.True(valid.IsValid);
        Assert.Equal(
            [DevicePermissions.Read, CloudPermissionCatalog.Recipe.Update],
            valid.Permissions);
        Assert.False(invalid.IsValid);
        Assert.Contains("Permission.Forged", invalid.RejectedPermissions);
    }

    [Fact]
    public void RoleAdminAssignableCatalog_ShouldExcludeGovernanceAndAdminOnlyPermissions()
    {
        var permissions = CloudPermissionCatalog.RoleAdminAssignable;
        var builtInRoleAdmin =
            SystemRolePermissionTemplates.Templates[SystemRoles.RoleAdmin];

        Assert.DoesNotContain(CloudPermissionCatalog.Role.Define, permissions);
        Assert.DoesNotContain(CloudPermissionCatalog.Role.Update, permissions);
        Assert.DoesNotContain(CloudPermissionCatalog.Employee.Terminate, permissions);
        Assert.DoesNotContain(DevicePermissions.Create, permissions);
        Assert.DoesNotContain(DevicePermissions.Delete, permissions);
        Assert.DoesNotContain(DevicePermissions.CascadeDelete, permissions);
        Assert.DoesNotContain(ClientReleasePermissions.HardDelete, permissions);
        Assert.Contains(DevicePermissions.Read, permissions);
        Assert.Contains(CloudPermissionCatalog.Role.Define, builtInRoleAdmin);
        Assert.Contains(CloudPermissionCatalog.Role.Update, builtInRoleAdmin);
    }

    [Fact]
    public async Task DefineRolePolicyHandler_ShouldDeleteRoleWhenPermissionAssignmentFails()
    {
        var rolePolicyService = new StubRolePolicyService
        {
            UpdateRolePermissionsResult = Result.Failure("permission update failed")
        };
        var auditTrail = new RecordingAuditTrailService();
        var handler = new DefineRolePolicyHandler(
            rolePolicyService,
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin-001",
                Roles = [SystemRoles.Admin],
                IsAuthenticated = true
            },
            auditTrail);

        var result = await handler.Handle(
            new DefineRolePolicyCommand("Auditor", ["Device.Read"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Auditor", rolePolicyService.DeletedRoleName);
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "Role.Define"
            && x.TargetIdOrKey == "Auditor"
            && !x.Succeeded);
    }

    [Fact]
    public async Task DefineRolePolicyHandler_ShouldNotDeleteExistingRoleWhenPermissionAssignmentFails()
    {
        var rolePolicyService = new StubRolePolicyService
        {
            RoleExists = true,
            UpdateRolePermissionsResult = Result.Failure("permission update failed")
        };
        var auditTrail = new RecordingAuditTrailService();
        var handler = new DefineRolePolicyHandler(
            rolePolicyService,
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin-001",
                Roles = [SystemRoles.Admin],
                IsAuthenticated = true
            },
            auditTrail);

        var result = await handler.Handle(
            new DefineRolePolicyCommand("Auditor", ["Device.Read"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(rolePolicyService.DeletedRoleName);
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "Role.Define"
            && x.TargetIdOrKey == "Auditor"
            && !x.Succeeded);
    }

    [Fact]
    public async Task UpdateRolePermissionsHandler_ShouldWriteAuditOnSuccess()
    {
        var rolePolicyService = new StubRolePolicyService
        {
            RolePermissions = [DevicePermissions.Read]
        };
        var auditTrail = new RecordingAuditTrailService();
        var handler = new UpdateRolePermissionsHandler(
            rolePolicyService,
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin-001",
                Roles = [SystemRoles.Admin],
                IsAuthenticated = true
            },
            auditTrail);

        var result = await handler.Handle(
            new UpdateRolePermissionsCommand("Supervisor", ["Device.Read", "Recipe.Update"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "Role.Permissions.Update"
            && x.TargetIdOrKey == "Supervisor"
            && x.Succeeded
            && x.Summary.Contains("\"beforePermissions\":[\"Device.Read\"]", StringComparison.Ordinal)
            && x.Summary.Contains(
                "\"afterPermissions\":[\"Device.Read\",\"Recipe.Update\"]",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task DefineRolePolicyHandler_ShouldRejectTrimmedAdminWithoutCreatingOrUpdatingRole()
    {
        var rolePolicyService = new StubRolePolicyService();
        var handler = new DefineRolePolicyHandler(
            rolePolicyService,
            CreateAdminUser(),
            new RecordingAuditTrailService());

        var result = await handler.Handle(
            new DefineRolePolicyCommand("  aDmIn ", [DevicePermissions.Read]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(rolePolicyService.CreatedRoleName);
        Assert.Equal(0, rolePolicyService.UpdateRolePermissionsCalls);
    }

    [Fact]
    public async Task UpdateRolePermissionsHandler_ShouldRejectAdminWithoutWriting()
    {
        var rolePolicyService = new StubRolePolicyService
        {
            RolePermissions = [DevicePermissions.Read]
        };
        var handler = new UpdateRolePermissionsHandler(
            rolePolicyService,
            CreateAdminUser(),
            new RecordingAuditTrailService());

        var result = await handler.Handle(
            new UpdateRolePermissionsCommand(" ADMIN ", [CloudPermissionCatalog.Recipe.Read]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, rolePolicyService.UpdateRolePermissionsCalls);
        Assert.Equal([DevicePermissions.Read], rolePolicyService.RolePermissions);
    }

    [Fact]
    public async Task DefineRolePolicyHandler_ShouldRejectNonAssignablePermissionAsWholeSet()
    {
        var rolePolicyService = new StubRolePolicyService();
        var auditTrail = new RecordingAuditTrailService();
        var handler = new DefineRolePolicyHandler(
            rolePolicyService,
            CreateAdminUser(),
            auditTrail);

        var result = await handler.Handle(
            new DefineRolePolicyCommand(
                "Supervisor",
                [DevicePermissions.Read, CloudPermissionCatalog.Role.Update]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(rolePolicyService.CreatedRoleName);
        Assert.Equal(0, rolePolicyService.UpdateRolePermissionsCalls);
        Assert.Contains(auditTrail.Entries, entry =>
            !entry.Succeeded
            && entry.Summary.Contains("\"reasonCode\":\"PermissionNotAssignable\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateRolePermissionsHandler_ShouldRejectUnknownPermissionWithoutPartialWrite()
    {
        var rolePolicyService = new StubRolePolicyService
        {
            RolePermissions = [DevicePermissions.Read]
        };
        var handler = new UpdateRolePermissionsHandler(
            rolePolicyService,
            CreateAdminUser(),
            new RecordingAuditTrailService());

        var result = await handler.Handle(
            new UpdateRolePermissionsCommand(
                "Supervisor",
                [CloudPermissionCatalog.Recipe.Read, "Permission.Forged"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, rolePolicyService.UpdateRolePermissionsCalls);
        Assert.Equal([DevicePermissions.Read], rolePolicyService.RolePermissions);
    }

    [Fact]
    public async Task UpdateUserPermissionsHandler_ShouldWriteAuditOnFailure()
    {
        var userId = Guid.NewGuid();
        var rolePolicyService = new StubRolePolicyService
        {
            UpdateUserPersonalPermissionsResult = Result.Failure("user permission update failed")
        };
        var auditTrail = new RecordingAuditTrailService();
        var handler = new UpdateUserPermissionsHandler(
            rolePolicyService,
            new StubAdminTargetGuard(),
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin-001",
                Roles = [SystemRoles.Admin],
                IsAuthenticated = true
            },
            auditTrail);

        var result = await handler.Handle(
            new UpdateUserPermissionsCommand(userId, ["Device.Read"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "User.Permissions.Update"
            && x.TargetIdOrKey == userId.ToString()
            && !x.Succeeded);
    }

    [Fact]
    public async Task UpdateUserPermissionsHandler_ShouldRejectUnknownPermissionWithoutPartialWrite()
    {
        var userId = Guid.NewGuid();
        var rolePolicyService = new StubRolePolicyService
        {
            UserPersonalPermissions = [DevicePermissions.Read]
        };
        var auditTrail = new RecordingAuditTrailService();
        var handler = new UpdateUserPermissionsHandler(
            rolePolicyService,
            new StubAdminTargetGuard(),
            CreateAdminUser(),
            auditTrail);

        var result = await handler.Handle(
            new UpdateUserPermissionsCommand(
                userId,
                [" device.read ", "DEVICE.READ", "Permission.Forged"]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, rolePolicyService.UpdateUserPersonalPermissionsCalls);
        Assert.Equal([DevicePermissions.Read], rolePolicyService.UserPersonalPermissions);
        Assert.Contains(auditTrail.Entries, entry =>
            !entry.Succeeded
            && entry.Summary.Contains("\"beforePermissions\":[\"Device.Read\"]", StringComparison.Ordinal)
            && entry.Summary.Contains("\"afterPermissions\":[\"Device.Read\"]", StringComparison.Ordinal)
            && entry.Summary.Contains("\"reasonCode\":\"PermissionNotDefined\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateUserPermissionsHandler_ShouldCanonicalizeBeforeWriting()
    {
        var userId = Guid.NewGuid();
        var rolePolicyService = new StubRolePolicyService();
        var handler = new UpdateUserPermissionsHandler(
            rolePolicyService,
            new StubAdminTargetGuard(),
            CreateAdminUser(),
            new RecordingAuditTrailService());

        var result = await handler.Handle(
            new UpdateUserPermissionsCommand(
                userId,
                [" device.read ", "DEVICE.READ", " recipe.update "]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [DevicePermissions.Read, CloudPermissionCatalog.Recipe.Update],
            rolePolicyService.LastUpdatedUserPermissions);
    }

    [Fact]
    public async Task PersonalPermissionHandlers_ShouldRejectAdminTargetBeforeReadingOrWriting()
    {
        var userId = Guid.NewGuid();
        var rolePolicyService = new StubRolePolicyService
        {
            UserPersonalPermissions = [DevicePermissions.Read]
        };
        var targetGuard = new StubAdminTargetGuard
        {
            GuardResult = Result.Failure(AdminTargetProtectionErrors.AdminTargetProtected)
        };
        var auditTrail = new RecordingAuditTrailService();
        var updateHandler = new UpdateUserPermissionsHandler(
            rolePolicyService,
            targetGuard,
            CreateAdminUser(),
            auditTrail);
        var queryHandler = new GetUserPersonalPermissionsHandler(
            rolePolicyService,
            targetGuard);

        var updateResult = await updateHandler.Handle(
            new UpdateUserPermissionsCommand(userId, [CloudPermissionCatalog.Recipe.Read]),
            CancellationToken.None);
        var queryResult = await queryHandler.Handle(
            new GetUserPersonalPermissionsQuery(userId),
            CancellationToken.None);

        Assert.False(updateResult.IsSuccess);
        Assert.False(queryResult.IsSuccess);
        Assert.Equal(0, rolePolicyService.UpdateUserPersonalPermissionsCalls);
        Assert.Equal(0, rolePolicyService.GetUserPersonalPermissionsCalls);
        Assert.Equal([DevicePermissions.Read], rolePolicyService.UserPersonalPermissions);
        Assert.Contains(auditTrail.Entries, entry =>
            !entry.Succeeded
            && entry.Summary.Contains(
                "\"reasonCode\":\"AdminTargetProtected\"",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResetPasswordHandler_ShouldResetOrdinaryUserWithoutAuditingPasswordContent()
    {
        const string newPassword = "Sensitive-Reset-Password-123!";
        var userId = Guid.NewGuid();
        var passwordService = new StubIdentityPasswordService();
        var refreshTokenService = new StubRefreshTokenService();
        var auditTrail = new RecordingAuditTrailService();
        var handler = new ResetPasswordHandler(
            passwordService,
            refreshTokenService,
            new StubAdminTargetGuard(),
            CreateAdminUser(),
            auditTrail);

        var result = await handler.Handle(
            new ResetPasswordCommand(userId, newPassword),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, passwordService.LastResetUserId);
        Assert.Contains(refreshTokenService.Revocations, revocation =>
            revocation.SubjectId == userId
            && revocation.Reason == "password-reset");
        var auditText = string.Join(
            Environment.NewLine,
            auditTrail.Entries.Select(entry => $"{entry.Summary} {entry.FailureReason}"));
        Assert.DoesNotContain(newPassword, auditText, StringComparison.Ordinal);
        Assert.DoesNotContain("hash", auditText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetPasswordHandler_ShouldRejectAdminSelfTargetWithoutPasswordMutation()
    {
        const string newPassword = "Must-Not-Be-Written-123!";
        var adminUserId = Guid.NewGuid();
        var passwordService = new StubIdentityPasswordService();
        var auditTrail = new RecordingAuditTrailService();
        var handler = new ResetPasswordHandler(
            passwordService,
            new StubRefreshTokenService(),
            new StubAdminTargetGuard
            {
                GuardResult = Result.Failure(AdminTargetProtectionErrors.AdminTargetProtected)
            },
            new TestCurrentUser
            {
                Id = adminUserId.ToString(),
                UserName = "admin-001",
                Roles = [SystemRoles.Admin],
                IsAuthenticated = true
            },
            auditTrail);

        var result = await handler.Handle(
            new ResetPasswordCommand(adminUserId, newPassword),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(passwordService.LastResetUserId);
        Assert.DoesNotContain(
            newPassword,
            string.Join(
                Environment.NewLine,
                auditTrail.Entries.Select(entry => $"{entry.Summary} {entry.FailureReason}")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangePasswordHandler_ShouldRejectCrossUserPasswordChange()
    {
        var passwordService = new StubIdentityPasswordService();
        var refreshTokenService = new StubRefreshTokenService();
        var currentUserId = Guid.NewGuid();
        var handler = new ChangePasswordHandler(
            passwordService,
            refreshTokenService,
            new TestCurrentUser
            {
                Id = currentUserId.ToString(),
                Roles = ["Operator"],
                UserName = "operator-001",
                IsAuthenticated = true
            });

        var result = await handler.Handle(
            new ChangePasswordCommand(Guid.NewGuid(), "OldPassword123!", "NewPassword123!"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(passwordService.LastChangedUserId);
    }

    [Fact]
    public async Task ChangePasswordHandler_ShouldAllowCurrentUserToChangeOwnPassword()
    {
        var currentUserId = Guid.NewGuid();
        var passwordService = new StubIdentityPasswordService();
        var refreshTokenService = new StubRefreshTokenService();
        var handler = new ChangePasswordHandler(
            passwordService,
            refreshTokenService,
            new TestCurrentUser
            {
                Id = currentUserId.ToString(),
                Roles = ["Operator"],
                UserName = "operator-001",
                IsAuthenticated = true
            });

        var result = await handler.Handle(
            new ChangePasswordCommand(currentUserId, "OldPassword123!", "NewPassword123!"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(currentUserId, passwordService.LastChangedUserId);
    }

    [Fact]
    public void GetAllRolesQuery_ShouldRequireRoleReadPermission()
    {
        var attribute = typeof(GetAllRolesQuery)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
            .Cast<AuthorizeRequirementAttribute>()
            .SingleOrDefault();

        Assert.NotNull(attribute);
        Assert.Equal("Role.Read", attribute!.Permission);
    }

    [Fact]
    public async Task GetAllDefinedPermissions_ShouldExposeRoleAdminAssignableCatalogOnly()
    {
        var handler = new GetAllDefinedPermissionsHandler();

        var result = await handler.Handle(new GetAllDefinedPermissionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var permissions = result.Value!
            .SelectMany(group => group.Permissions)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(ClientReleasePermissions.Read, permissions);
        Assert.Contains(ClientReleasePermissions.GenerateInstaller, permissions);
        Assert.Contains(ClientReleasePermissions.Publish, permissions);
        Assert.Contains(ClientReleasePermissions.Manage, permissions);
        Assert.Contains(DeviceClientOverviewPermissions.Read, permissions);
        Assert.Contains(EdgeHostPermissions.Read, permissions);
        Assert.DoesNotContain("EdgeHost.Manage", permissions);
        Assert.Contains(CloudPermissionCatalog.Role.Read, permissions);
        Assert.DoesNotContain(CloudPermissionCatalog.Role.Define, permissions);
        Assert.DoesNotContain(CloudPermissionCatalog.Role.Update, permissions);
        Assert.DoesNotContain(CloudPermissionCatalog.Employee.Terminate, permissions);
        Assert.DoesNotContain(DevicePermissions.Create, permissions);
        Assert.DoesNotContain(DevicePermissions.Delete, permissions);
        Assert.DoesNotContain(DevicePermissions.CascadeDelete, permissions);
        Assert.DoesNotContain(ClientReleasePermissions.HardDelete, permissions);
        Assert.DoesNotContain("Device.Deactivate", permissions);
        Assert.DoesNotContain(
            typeof(IRolePolicyService),
            typeof(GetAllDefinedPermissionsHandler)
                .GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void SystemRolePermissionTemplates_ShouldGiveProductionViewerEdgeHostReadOnly()
    {
        var permissions = SystemRolePermissionTemplates.Templates[SystemRoles.ProductionViewer];

        Assert.Contains(DeviceClientOverviewPermissions.Read, permissions);
        Assert.Contains(EdgeHostPermissions.Read, permissions);
        Assert.DoesNotContain("EdgeHost.Manage", permissions);
    }

    [Fact]
    public void SystemRolePermissionTemplates_ShouldKeepOverviewPlcAndReleasePermissionsIndependent()
    {
        var deviceAdmin = SystemRolePermissionTemplates.Templates[SystemRoles.DeviceAdmin];
        var installer = SystemRolePermissionTemplates.Templates[SystemRoles.ClientInstallerOperator];
        var releaseManager = SystemRolePermissionTemplates.Templates[SystemRoles.ClientReleaseManager];

        Assert.Contains(DeviceClientOverviewPermissions.Read, deviceAdmin);
        Assert.Contains(EdgeHostPermissions.Read, deviceAdmin);
        Assert.DoesNotContain(ClientReleasePermissions.Read, deviceAdmin);
        Assert.DoesNotContain(DevicePermissions.Create, deviceAdmin);
        Assert.DoesNotContain(DevicePermissions.Delete, deviceAdmin);
        Assert.DoesNotContain(DevicePermissions.CascadeDelete, deviceAdmin);

        Assert.Contains(DeviceClientOverviewPermissions.Read, installer);
        Assert.Contains(EdgeHostPermissions.Read, installer);
        Assert.Contains(ClientReleasePermissions.Read, installer);

        Assert.Contains(DeviceClientOverviewPermissions.Read, releaseManager);
        Assert.DoesNotContain(EdgeHostPermissions.Read, releaseManager);
        Assert.Contains(ClientReleasePermissions.Read, releaseManager);
    }

    [Fact]
    public void HrAdminTemplate_ShouldAllowOrdinaryEmployeeStatusMaintenance()
    {
        var permissions = SystemRolePermissionTemplates.Templates[SystemRoles.HrAdmin];

        Assert.Contains(CloudPermissionCatalog.Employee.Update, permissions);
        Assert.Contains(CloudPermissionCatalog.Employee.UpdateAccess, permissions);
        Assert.Contains(CloudPermissionCatalog.Employee.Deactivate, permissions);
        Assert.DoesNotContain(CloudPermissionCatalog.Employee.Terminate, permissions);
    }

    [Fact]
    public void ClientReleaseDeletionRequests_ShouldRequireAdminOnlyHardDeleteAndPublishLock()
    {
        var publishLock = typeof(PublishEdgeReleaseBundleCommand)
            .GetCustomAttributes(typeof(DistributedLockAttribute), inherit: false)
            .Cast<DistributedLockAttribute>()
            .Single();
        var destructiveRequests = new[]
        {
            typeof(DeleteClientReleasePackageCommand),
            typeof(HardDeleteClientReleaseComponentCommand),
            typeof(RetryClientReleaseComponentDeletionCommand)
        };

        foreach (var requestType in destructiveRequests)
        {
            var permission = requestType
                .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
                .Cast<AuthorizeRequirementAttribute>()
                .Single();
            Assert.Equal(ClientReleasePermissions.HardDelete, permission.Permission);
            Assert.NotEmpty(requestType.GetCustomAttributes(typeof(AdminOnlyAttribute), inherit: false));

            var distributedLock = requestType
                .GetCustomAttributes(typeof(DistributedLockAttribute), inherit: false)
                .Cast<DistributedLockAttribute>()
                .Single();
            Assert.Equal(publishLock.KeyTemplate, distributedLock.KeyTemplate);
            Assert.Equal(publishLock.TimeoutSeconds, distributedLock.TimeoutSeconds);
        }
    }

    [Fact]
    public void ClientReleaseHardDeletePermission_ShouldNotBeGrantedToNonAdminRoleTemplates()
    {
        foreach (var permissions in SystemRolePermissionTemplates.Templates.Values)
        {
            Assert.DoesNotContain(ClientReleasePermissions.HardDelete, permissions);
        }
    }

    [Fact]
    public void RegisterDeviceCommand_ShouldRequireDeviceCreatePermissionAndAdminOnly()
    {
        var permissionAttribute = typeof(RegisterDeviceCommand)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
            .Cast<AuthorizeRequirementAttribute>()
            .SingleOrDefault();
        var adminOnlyAttribute = typeof(RegisterDeviceCommand)
            .GetCustomAttributes(typeof(AdminOnlyAttribute), inherit: false)
            .Cast<AdminOnlyAttribute>()
            .SingleOrDefault();

        Assert.NotNull(permissionAttribute);
        Assert.Equal("Device.Create", permissionAttribute!.Permission);
        Assert.NotNull(adminOnlyAttribute);
    }

    [Fact]
    public void DeleteDeviceCommand_ShouldRequireDeleteAndCascadeDeletePermissions()
    {
        var permissions = typeof(DeleteDeviceCommand)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
            .Cast<AuthorizeRequirementAttribute>()
            .Select(attribute => attribute.Permission)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(DevicePermissions.Delete, permissions);
        Assert.Contains(DevicePermissions.CascadeDelete, permissions);
        Assert.NotEmpty(typeof(DeleteDeviceCommand)
            .GetCustomAttributes(typeof(AdminOnlyAttribute), inherit: false));
    }

    [Fact]
    public void SensitivePersonnelRequests_ShouldRequireAdminOnly()
    {
        Type[] requestTypes =
        [
            typeof(GetUserPersonalPermissionsQuery),
            typeof(UpdateUserPermissionsCommand),
            typeof(ResetPasswordCommand),
            typeof(TerminateEmployeeCommand)
        ];

        foreach (var requestType in requestTypes)
        {
            Assert.NotEmpty(requestType.GetCustomAttributes(
                typeof(AdminOnlyAttribute),
                inherit: false));
        }
    }

    [Fact]
    public async Task NonAdminWithUpdateAccess_ShouldNotWritePersonalPermissions_AndShouldBeAudited()
    {
        var nextCalled = false;
        var auditTrail = new RecordingAuditTrailService();
        var command = new UpdateUserPermissionsCommand(
            Guid.NewGuid(),
            [DevicePermissions.Read]);
        var behavior = new AdminOnlyBehavior<UpdateUserPermissionsCommand, Result<bool>>(
            new StubCurrentUserDeviceAccessService { IsAdministrator = false },
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "hr-admin",
                Roles = [SystemRoles.HrAdmin],
                Permissions = [CloudPermissionCatalog.Employee.UpdateAccess],
                IsAuthenticated = true
            },
            auditTrail);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                command,
                _ =>
                {
                    nextCalled = true;
                    return Task.FromResult(Result.Success(true));
                },
                CancellationToken.None));

        Assert.False(nextCalled);
        Assert.Contains(auditTrail.Entries, entry =>
            entry.OperationType == "User.Permissions.Update"
            && !entry.Succeeded
            && entry.Summary.Contains("\"reasonCode\":\"AdminRequired\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NonAdminWithEmployeePermissions_ShouldNotReadPersonalPermissions()
    {
        var nextCalled = false;
        var query = new GetUserPersonalPermissionsQuery(Guid.NewGuid());
        var behavior = new AdminOnlyBehavior<
            GetUserPersonalPermissionsQuery,
            Result<List<string>>>(
            new StubCurrentUserDeviceAccessService { IsAdministrator = false },
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "hr-admin",
                Roles = [SystemRoles.HrAdmin],
                Permissions =
                [
                    CloudPermissionCatalog.Employee.Read,
                    CloudPermissionCatalog.Employee.UpdateAccess
                ],
                IsAuthenticated = true
            },
            new RecordingAuditTrailService());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                query,
                _ =>
                {
                    nextCalled = true;
                    return Task.FromResult(Result.Success(new List<string>()));
                },
                CancellationToken.None));

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task DeviceAdminWithLegacyDeleteClaims_ShouldNotReachDeleteDeviceHandler()
    {
        var nextCalled = false;
        var command = new DeleteDeviceCommand(Guid.NewGuid());
        var behavior = new AdminOnlyBehavior<DeleteDeviceCommand, Result<bool>>(
            new StubCurrentUserDeviceAccessService { IsAdministrator = false },
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "device-admin",
                Roles = [SystemRoles.DeviceAdmin],
                Permissions =
                [
                    DevicePermissions.Delete,
                    DevicePermissions.CascadeDelete
                ],
                IsAuthenticated = true
            },
            new RecordingAuditTrailService());

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                command,
                _ =>
                {
                    nextCalled = true;
                    return Task.FromResult(Result.Success(true));
                },
                CancellationToken.None));

        Assert.False(nextCalled);
    }

    [Fact]
    public async Task AdminOnlyBehavior_ShouldRejectNonAdmin()
    {
        var behavior = new AdminOnlyBehavior<AdminOnlyHumanCommand, Result<bool>>(
            new StubCurrentUserDeviceAccessService { IsAdministrator = false },
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "operator-001",
                IsAuthenticated = true
            },
            new RecordingAuditTrailService());

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                new AdminOnlyHumanCommand(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains("管理员", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminOnlyBehavior_ShouldAllowAdmin()
    {
        var nextCalled = false;
        var behavior = new AdminOnlyBehavior<AdminOnlyHumanCommand, Result<bool>>(
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin-001",
                Roles = [SystemRoles.Admin],
                IsAuthenticated = true
            },
            new RecordingAuditTrailService());

        var result = await behavior.Handle(
            new AdminOnlyHumanCommand(),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success(true));
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AuthorizationBehavior_ShouldAllowEdgeReleasePublisherForPublishPermission()
    {
        var nextCalled = false;
        var permissionProvider = new RecordingPermissionProvider();
        var behavior = new AuthorizationBehavior<EdgeReleasePublishCommand, Result<bool>>(
            new TestCurrentUser
            {
                Id = "edge-release-api-key:test",
                UserName = "edge-publisher:test",
                ActorType = IIoTClaimTypes.EdgeReleasePublisherActor,
                Permissions = [ClientReleasePermissions.Publish],
                IsAuthenticated = true
            },
            permissionProvider);

        var result = await behavior.Handle(
            new EdgeReleasePublishCommand(),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success(true));
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(nextCalled);
        Assert.Null(permissionProvider.LastUserId);
    }

    [Fact]
    public async Task AuthorizationBehavior_ShouldRejectEdgeReleasePublisherForManagePermission()
    {
        var behavior = new AuthorizationBehavior<EdgeReleaseManageCommand, Result<bool>>(
            new TestCurrentUser
            {
                Id = "edge-release-api-key:test",
                UserName = "edge-publisher:test",
                ActorType = IIoTClaimTypes.EdgeReleasePublisherActor,
                Permissions = [ClientReleasePermissions.Publish, ClientReleasePermissions.Manage],
                IsAuthenticated = true
            },
            new RecordingPermissionProvider());

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                new EdgeReleaseManageCommand(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains("发布机器凭据只能执行客户端发布读取和上传操作", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizationBehavior_ShouldRejectEdgeReleasePublisherForUnprotectedHumanRequest()
    {
        var behavior = new AuthorizationBehavior<UnprotectedHumanCommand, Result<bool>>(
            new TestCurrentUser
            {
                Id = "edge-release-api-key:test",
                UserName = "edge-publisher:test",
                ActorType = IIoTClaimTypes.EdgeReleasePublisherActor,
                Permissions = [ClientReleasePermissions.Publish],
                IsAuthenticated = true
            },
            new RecordingPermissionProvider());

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                new UnprotectedHumanCommand(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains("不能执行未声明权限点", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizationBehavior_ShouldAllowAdminWhenAdminRoleIsNotFirst()
    {
        var nextCalled = false;
        var permissionProvider = new RecordingPermissionProvider();
        var behavior = new AuthorizationBehavior<EdgeReleaseManageCommand, Result<bool>>(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "multi-role-admin",
                Roles = [SystemRoles.ProductionViewer, SystemRoles.Admin],
                ActorType = IIoTClaimTypes.HumanActor,
                IsAuthenticated = true
            },
            permissionProvider);

        var result = await behavior.Handle(
            new EdgeReleaseManageCommand(),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success(true));
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(nextCalled);
        Assert.Null(permissionProvider.LastUserId);
    }

    [Fact]
    public async Task AuthorizationBehavior_ShouldNotTreatNonAdminMultiRoleAsAdmin()
    {
        var permissionProvider = new RecordingPermissionProvider
        {
            Permissions = []
        };
        var userId = Guid.NewGuid();
        var behavior = new AuthorizationBehavior<EdgeReleaseManageCommand, Result<bool>>(
            new TestCurrentUser
            {
                Id = userId.ToString(),
                UserName = "multi-role-user",
                Roles = [SystemRoles.ProductionViewer, SystemRoles.DeviceAdmin],
                ActorType = IIoTClaimTypes.HumanActor,
                IsAuthenticated = true
            },
            permissionProvider);

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                new EdgeReleaseManageCommand(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains("缺少执行该操作", exception.Message, StringComparison.Ordinal);
        Assert.Equal(userId, permissionProvider.LastUserId);
    }

    [Fact]
    public async Task AuthorizationBehavior_ShouldAllowHumanUserForUnprotectedHumanRequest()
    {
        var nextCalled = false;
        var behavior = new AuthorizationBehavior<UnprotectedHumanCommand, Result<bool>>(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "human-user",
                ActorType = IIoTClaimTypes.HumanActor,
                IsAuthenticated = true
            },
            new RecordingPermissionProvider());

        var result = await behavior.Handle(
            new UnprotectedHumanCommand(),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success(true));
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task DeviceBindingBehavior_ShouldAllowMatchingDeviceId()
    {
        var deviceId = Guid.NewGuid();
        var behavior = new DeviceBindingBehavior<DeviceScopedCommand, Result<bool>>(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "edge-operator",
                Roles = [SystemRoles.Admin],
                DeviceId = deviceId,
                IsAuthenticated = true
            });

        var result = await behavior.Handle(
            new DeviceScopedCommand(deviceId),
            _ => Task.FromResult(Result.Success(true)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeviceBindingBehavior_ShouldRejectMismatchedDeviceId()
    {
        var behavior = new DeviceBindingBehavior<DeviceScopedCommand, Result<bool>>(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "edge-operator",
                Roles = [SystemRoles.Admin],
                DeviceId = Guid.NewGuid(),
                IsAuthenticated = true
            });

        await Assert.ThrowsAsync<IIoT.Services.CrossCutting.Exceptions.ForbiddenException>(() =>
            behavior.Handle(
                new DeviceScopedCommand(Guid.NewGuid()),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));
    }

    [Fact]
    public async Task DistributedLockBehavior_ShouldRejectMissingTemplateProperty()
    {
        var behavior = new DistributedLockBehavior<BrokenLockCommand, Result<bool>>(
            new NoopDistributedLockService(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<
                DistributedLockBehavior<BrokenLockCommand, Result<bool>>>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new BrokenLockCommand(Guid.NewGuid()),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Equal("Distributed lock key could not be resolved.", exception.Message);
    }

    [Fact]
    public async Task RefreshHumanIdentityHandler_ShouldRevokeTokensWhenAccountUnavailable()
    {
        var subjectId = Guid.NewGuid();
        var account = IdentityAccount.Create(subjectId, "E2001");
        account.Disable();

        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById = account
        };
        var refreshTokenService = new StubRefreshTokenService
        {
            NextRotateSubjectId = subjectId
        };
        var handler = new RefreshHumanIdentityHandler(
            identityStore,
            new StubPermissionProvider(),
            new StubJwtTokenGenerator(),
            refreshTokenService);

        var result = await handler.Handle(
            new RefreshHumanIdentityCommand("refresh-human"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Contains(refreshTokenService.Revocations, x =>
            x.ActorType == IIoT.Services.Contracts.Identity.IIoTClaimTypes.HumanActor
            && x.SubjectId == subjectId
            && x.Reason == "identity-unavailable");
    }

    [Fact]
    public async Task RefreshEdgeDeviceIdentityHandler_ShouldRevokeTokensWhenDeviceUnavailable()
    {
        var deviceId = Guid.NewGuid();
        var refreshTokenService = new StubRefreshTokenService
        {
            NextRotateSubjectId = deviceId
        };
        var handler = new RefreshEdgeDeviceIdentityHandler(
            new InMemoryRepository<IIoT.Core.Production.Aggregates.Devices.Device>(),
            new StubJwtTokenGenerator(),
            refreshTokenService);

        var result = await handler.Handle(
            new RefreshEdgeDeviceIdentityCommand("refresh-edge"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Contains(refreshTokenService.Revocations, x =>
            x.ActorType == IIoT.Services.Contracts.Identity.IIoTClaimTypes.EdgeDeviceActor
            && x.SubjectId == deviceId
            && x.Reason == "device-unavailable");
    }

    private static TestCurrentUser CreateAdminUser()
        => new()
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "admin-001",
            Roles = [SystemRoles.Admin],
            IsAuthenticated = true
        };

    private sealed class StubPermissionProvider : IPermissionProvider
    {
        public Task<IList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IList<string>>([]);
        }
    }

    [AuthorizeRequirement(ClientReleasePermissions.Publish)]
    private sealed record EdgeReleasePublishCommand() : IHumanCommand<Result<bool>>;

    [AuthorizeRequirement(ClientReleasePermissions.Manage)]
    private sealed record EdgeReleaseManageCommand() : IHumanCommand<Result<bool>>;

    private sealed record UnprotectedHumanCommand() : IHumanCommand<Result<bool>>;

}
