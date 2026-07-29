using AutoMapper;
using System.Text;
using System.Text.Json;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Devices.Events;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Aggregates.Recipes.Events;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.MasterDataService.Commands.Processes;
using IIoT.ProductionService.Commands;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Commands.DeviceLogs;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.Commands.Recipes;
using IIoT.ProductionService.Caching;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.PassStations;
using IIoT.ProductionService.Profiles;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.ProductionService.Queries.Capacities;
using IIoT.ProductionService.Queries.Devices;
using IIoT.ProductionService.Queries.DeviceLogs;
using IIoT.ProductionService.Queries.PassStations;
using IIoT.ProductionService.Queries.Recipes;
using IIoT.ProductionService.Security;
using IIoT.ProductionService.Validators;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.CrossCutting.Exceptions;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.Services.Contracts.Events.DeviceLogs;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;
using IIoT.SharedKernel.Specification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.CloudPlatform.ApplicationTests;

public sealed class ApplicationFlowGuardTests
{
    [Fact]
    public async Task CreateProcessHandler_ShouldRejectDuplicateProcessCode()
    {
        var repository = new InMemoryRepository<MfgProcess>();
        var processQueries = new StubProcessReadQueryService { CodeExists = true };
        var cache = new RecordingCacheService();
        var handler = new CreateProcessHandler(repository, processQueries, cache);

        var result = await handler.Handle(
            new CreateProcessCommand(" PROC-001 ", "Injection"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Errors);
        Assert.Single(result.Errors);
        Assert.Null(repository.AddedEntity);
        Assert.Empty(cache.RemovedKeys);
    }

    [Fact]
    public async Task RegisterDeviceHandler_ShouldCreateDeviceAndRaiseRegisteredEvent()
    {
        var repository = new InMemoryRepository<Device>();
        var processId = Guid.NewGuid();
        var processQueries = new StubProcessReadQueryService { Exists = true };
        var deviceQueries = new StubDeviceReadQueryService();
        var auditTrail = new RecordingAuditTrailService();
        var handler = new RegisterDeviceHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin-001",
                Roles = [SystemRoles.Admin],
                ActorType = IIoTClaimTypes.HumanActor,
                IsAuthenticated = true
            },
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            repository,
            processQueries,
            deviceQueries,
            auditTrail);

        var result = await handler.Handle(
            new RegisterDeviceCommand(
                "Injection-01",
                processId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var created = Assert.IsType<CreateDeviceResultDto>(result.Value);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotNull(repository.AddedEntity);
        Assert.Equal(processId, repository.AddedEntity!.ProcessId);
        Assert.StartsWith("DEV-", repository.AddedEntity.Code);
        Assert.Equal(repository.AddedEntity.Code, created.Code);
        Assert.Null(repository.AddedEntity.BootstrapSecretHash);
        Assert.Contains(repository.AddedEntity.DomainEvents, x =>
            x is DeviceRegisteredDomainEvent registered
            && registered.ProcessId == processId
            && registered.Code == repository.AddedEntity.Code);
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "Device.Register"
            && x.TargetType == "Device"
            && x.Succeeded);
    }

    [Fact]
    public async Task RegisterDeviceHandler_ShouldRejectNonAdminBeforeCreatingDevice()
    {
        var repository = new InMemoryRepository<Device>();
        var processQueries = new StubProcessReadQueryService { Exists = true };
        var deviceQueries = new StubDeviceReadQueryService();
        var auditTrail = new RecordingAuditTrailService();
        var handler = new RegisterDeviceHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "operator-001",
                Roles = ["Operator"],
                IsAuthenticated = true
            },
            new StubCurrentUserDeviceAccessService(),
            repository,
            processQueries,
            deviceQueries,
            auditTrail);

        var result = await handler.Handle(
            new RegisterDeviceCommand("Injection-01", Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, error => error.Contains("管理员", StringComparison.Ordinal));
        Assert.Null(repository.AddedEntity);
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "Device.Register"
            && x.TargetType == "Device"
            && !x.Succeeded
            && x.FailureReason == "只有管理员可以注册设备");
    }

    [Fact]
    public async Task RegisterDeviceHandler_ShouldFailWhenUniqueCodeCannotBeAllocated()
    {
        var repository = new InMemoryRepository<Device>();
        var processQueries = new StubProcessReadQueryService { Exists = true };
        var deviceQueries = new StubDeviceReadQueryService { CodeExists = true };
        var auditTrail = new RecordingAuditTrailService();
        var handler = new RegisterDeviceHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin-001",
                Roles = [SystemRoles.Admin],
                ActorType = IIoTClaimTypes.HumanActor,
                IsAuthenticated = true
            },
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            repository,
            processQueries,
            deviceQueries,
            auditTrail);

        var result = await handler.Handle(
            new RegisterDeviceCommand("Injection-01", Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(repository.AddedEntity);
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "Device.Register"
            && !x.Succeeded);
    }

    [Fact]
    public async Task RegisterDeviceHandler_ShouldRejectDuplicateName()
    {
        var repository = new InMemoryRepository<Device>();
        var processQueries = new StubProcessReadQueryService { Exists = true };
        var deviceQueries = new StubDeviceReadQueryService { NameExists = true };
        var auditTrail = new RecordingAuditTrailService();
        var handler = new RegisterDeviceHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin-001",
                Roles = [SystemRoles.Admin],
                ActorType = IIoTClaimTypes.HumanActor,
                IsAuthenticated = true
            },
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            repository,
            processQueries,
            deviceQueries,
            auditTrail);

        var result = await handler.Handle(
            new RegisterDeviceCommand("Injection-01", Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, error => error.Contains("名称已存在", StringComparison.Ordinal));
        Assert.Null(repository.AddedEntity);
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "Device.Register"
            && x.TargetType == "Device"
            && !x.Succeeded);
    }

    [Fact]
    public async Task UpgradeRecipeVersionHandler_ShouldArchiveActiveVersionAndCreateNewRecipe()
    {
        var processId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var source = new Recipe("Injection Recipe", processId, deviceId, "{\"speed\":120}");
        source.ClearDomainEvents();
        var repository = new InMemoryRepository<Recipe>
        {
            SingleOrDefaultResult = source
        };
        repository.ListResult.Add(source);

        var handler = new UpgradeRecipeVersionHandler(
            repository,
            new StubRecipeReadQueryService(),
            new StubCurrentUserDeviceAccessService { IsAdministrator = true });

        var result = await handler.Handle(
            new UpgradeRecipeVersionCommand(source.Id, "V1.1", "{\"speed\":130}"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecipeStatus.Archived, source.Status);
        Assert.Contains(source, repository.UpdatedEntities);
        Assert.NotNull(repository.AddedEntity);
        Assert.Equal("V1.1", repository.AddedEntity!.Version);
        Assert.Equal(processId, repository.AddedEntity.ProcessId);
        Assert.Equal(deviceId, repository.AddedEntity.DeviceId);
        Assert.Contains(source.DomainEvents, x =>
            x is RecipeArchivedDomainEvent archived
            && archived.ProcessId == processId
            && archived.DeviceId == deviceId);
        Assert.Contains(repository.AddedEntity.DomainEvents, x =>
            x is RecipeVersionUpgradedDomainEvent upgraded
            && upgraded.SourceRecipeId == source.Id
            && upgraded.ProcessId == processId
            && upgraded.DeviceId == deviceId);
    }

    [Fact]
    public async Task DeleteRecipeHandler_ShouldRejectActiveRecipe()
    {
        var recipe = new Recipe("Active Recipe", Guid.NewGuid(), Guid.NewGuid(), "{\"speed\":120}");
        var repository = new InMemoryRepository<Recipe>
        {
            SingleOrDefaultResult = recipe
        };
        repository.ListResult.Add(recipe);
        var handler = new DeleteRecipeHandler(
            repository,
            new StubCurrentUserDeviceAccessService { IsAdministrator = true });

        var result = await handler.Handle(new DeleteRecipeCommand(recipe.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(recipe, repository.ListResult);
        Assert.DoesNotContain(recipe.DomainEvents, x => x is RecipeDeletedDomainEvent);
    }

    [Fact]
    public async Task UpdateEmployeeProfileHandler_ShouldOnlyRenameBasicProfile()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E001", "Old Name");
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new UpdateEmployeeProfileHandler(
            repository,
            unitOfWork,
            new StubAdminTargetGuard());

        var result = await handler.Handle(
            new UpdateEmployeeProfileCommand(employeeId, " New Name "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", employee.RealName);
        Assert.True(employee.IsActive);
        Assert.Contains(employee, repository.UpdatedEntities);
        Assert.Equal(1, unitOfWork.ExecuteResilientCalls);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(1, unitOfWork.CommitCalls);
        Assert.Equal(0, unitOfWork.RollbackCalls);
    }

    [Fact]
    public void UpdateEmployeeProfileCommand_ShouldRejectAccessAndStatusFields()
    {
        var employeeId = Guid.NewGuid();
        var json =
            $$"""
              {
                "employeeId": "{{employeeId}}",
                "realName": "Changed Name",
                "isActive": false,
                "roleName": "Supervisor"
              }
              """;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<UpdateEmployeeProfileCommand>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }));
    }

    [Fact]
    public async Task EmployeeWriteHandlers_ShouldRejectAdminTargetBeforeAnyDataMutation()
    {
        var employeeId = Guid.NewGuid();
        var targetGuard = new StubAdminTargetGuard
        {
            GuardResult = Result.Failure(AdminTargetProtectionErrors.AdminTargetProtected)
        };

        var profileRepository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = new Employee(employeeId, "A001", "Admin")
        };
        var profileUnitOfWork = new RecordingUnitOfWork();
        var profileHandler = new UpdateEmployeeProfileHandler(
            profileRepository,
            profileUnitOfWork,
            targetGuard);

        var accessRepository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = new Employee(employeeId, "A001", "Admin")
        };
        var accessUnitOfWork = new RecordingUnitOfWork();
        var accessHandler = new UpdateEmployeeAccessHandler(
            accessRepository,
            targetGuard,
            new StubDeviceReadQueryService(),
            accessUnitOfWork);

        var deactivateRepository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = new Employee(employeeId, "A001", "Admin")
        };
        var deactivateIdentityStore = new RecordingIdentityAccountStore();
        var deactivateUnitOfWork = new RecordingUnitOfWork();
        var deactivateHandler = new DeactivateEmployeeHandler(
            deactivateRepository,
            deactivateIdentityStore,
            deactivateUnitOfWork,
            new StubHumanSessionRevocationService(),
            targetGuard);

        var activateRepository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = new Employee(employeeId, "A001", "Admin")
        };
        var activateIdentityStore = new RecordingIdentityAccountStore();
        var activateUnitOfWork = new RecordingUnitOfWork();
        var activateHandler = new ActivateEmployeeHandler(
            activateRepository,
            activateIdentityStore,
            activateUnitOfWork,
            new StubHumanSessionRevocationService(),
            targetGuard,
            new StubEmployeeMutationObservationReader());

        var terminateRepository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = new Employee(employeeId, "A001", "Admin")
        };
        var terminateIdentityStore = new RecordingIdentityAccountStore();
        var terminateUnitOfWork = new RecordingUnitOfWork();
        var terminateHandler = new TerminateEmployeeHandler(
            terminateRepository,
            terminateIdentityStore,
            terminateUnitOfWork,
            new StubHumanSessionRevocationService(),
            targetGuard);

        var profileResult = await profileHandler.Handle(
            new UpdateEmployeeProfileCommand(employeeId, "Changed"),
            CancellationToken.None);
        var accessResult = await accessHandler.Handle(
            new UpdateEmployeeAccessCommand(employeeId, [Guid.NewGuid()]),
            CancellationToken.None);
        var deactivateResult = await deactivateHandler.Handle(
            new DeactivateEmployeeCommand(employeeId),
            CancellationToken.None);
        var activateResult = await activateHandler.Handle(
            new ActivateEmployeeCommand(employeeId),
            CancellationToken.None);
        var terminateResult = await terminateHandler.Handle(
            new TerminateEmployeeCommand(employeeId),
            CancellationToken.None);

        Assert.False(profileResult.IsSuccess);
        Assert.False(accessResult.IsSuccess);
        Assert.False(deactivateResult.IsSuccess);
        Assert.False(activateResult.IsSuccess);
        Assert.False(terminateResult.IsSuccess);
        Assert.Equal(5, targetGuard.Calls);
        Assert.Equal(0, profileRepository.GetSingleOrDefaultCalls);
        Assert.Equal(0, accessRepository.GetSingleOrDefaultCalls);
        Assert.Equal(0, deactivateRepository.GetSingleOrDefaultCalls);
        Assert.Equal(0, activateRepository.GetSingleOrDefaultCalls);
        Assert.Equal(0, terminateRepository.GetSingleOrDefaultCalls);
        Assert.Empty(profileRepository.UpdatedEntities);
        Assert.Empty(accessRepository.UpdatedEntities);
        Assert.Empty(deactivateRepository.UpdatedEntities);
        Assert.Empty(terminateIdentityStore.DeletedIds);
        Assert.Equal(1, profileUnitOfWork.BeginCalls);
        Assert.Equal(1, deactivateUnitOfWork.BeginCalls);
        Assert.Equal(1, activateUnitOfWork.BeginCalls);
        Assert.Equal(1, terminateUnitOfWork.BeginCalls);
        Assert.Equal(1, profileUnitOfWork.RollbackCalls);
        Assert.Equal(1, deactivateUnitOfWork.RollbackCalls);
        Assert.Equal(1, activateUnitOfWork.RollbackCalls);
        Assert.Equal(1, terminateUnitOfWork.RollbackCalls);
    }

    [Fact]
    public async Task OnboardEmployeeHandler_ShouldRollbackWhenRoleAssignmentFails()
    {
        var repository = new InMemoryRepository<Employee>();
        var identityStore = new RecordingIdentityAccountStore
        {
            AssignRoleResult = Result.Failure("role failed")
        };
        var passwordService = new StubIdentityPasswordService();
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new OnboardEmployeeHandler(
            identityStore,
            passwordService,
            new StubRolePolicyService { RoleExists = true },
            repository,
            unitOfWork,
            new TestCurrentUser { Id = Guid.NewGuid().ToString(), IsAuthenticated = true },
            new RecordingPermissionProvider
            {
                Permissions = ["Employee.UpdateAccess"]
            });

        var result = await handler.Handle(
            new OnboardEmployeeCommand("E1003", "Operator", "Password123!", "Supervisor"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(repository.AddedEntity);
        Assert.Equal(1, unitOfWork.ExecuteResilientCalls);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(0, unitOfWork.CommitCalls);
        Assert.Equal(1, unitOfWork.RollbackCalls);
    }

    [Fact]
    public async Task OnboardEmployeeHandler_ShouldRollbackWhenPasswordPersistenceFails()
    {
        var repository = new InMemoryRepository<Employee>();
        var passwordService = new StubIdentityPasswordService
        {
            SetPasswordResult = Result.Failure("password failed")
        };
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new OnboardEmployeeHandler(
            new RecordingIdentityAccountStore(),
            passwordService,
            new StubRolePolicyService(),
            repository,
            unitOfWork,
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                IsAuthenticated = true
            },
            new RecordingPermissionProvider());

        var result = await handler.Handle(
            new OnboardEmployeeCommand("E1004", "Operator", "Password123!"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(repository.AddedEntity);
        Assert.Equal(1, passwordService.SetPasswordCalls);
        Assert.Equal(1, unitOfWork.ExecuteResilientCalls);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(0, unitOfWork.CommitCalls);
        Assert.Equal(1, unitOfWork.RollbackCalls);
    }

    [Fact]
    public async Task OnboardEmployeeHandler_ShouldRejectRoleAssignmentWithoutUpdateAccess()
    {
        var repository = new InMemoryRepository<Employee>();
        var identityStore = new RecordingIdentityAccountStore();
        var passwordService = new StubIdentityPasswordService();
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new OnboardEmployeeHandler(
            identityStore,
            passwordService,
            new StubRolePolicyService { RoleExists = true },
            repository,
            unitOfWork,
            new TestCurrentUser { Id = Guid.NewGuid().ToString(), IsAuthenticated = true },
            new RecordingPermissionProvider());

        var result = await handler.Handle(
            new OnboardEmployeeCommand("E1005", "Operator", "Password123!", "Supervisor"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(repository.AddedEntity);
        Assert.Equal(0, unitOfWork.BeginCalls);
        Assert.Contains(result.Errors ?? [], error => error.Contains("Employee.UpdateAccess"));
    }

    [Theory]
    [InlineData(" Admin ")]
    [InlineData("ADMIN ")]
    [InlineData("admin")]
    public async Task OnboardEmployeeHandler_ShouldRejectAdminLikeRoleBeforeCreatingAnyData(
        string roleName)
    {
        var repository = new InMemoryRepository<Employee>();
        var identityStore = new RecordingIdentityAccountStore();
        var passwordService = new StubIdentityPasswordService();
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new OnboardEmployeeHandler(
            identityStore,
            passwordService,
            new StubRolePolicyService(),
            repository,
            unitOfWork,
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Roles = [SystemRoles.Admin],
                ActorType = IIoTClaimTypes.HumanActor,
                IsAuthenticated = true
            },
            new RecordingPermissionProvider());

        var result = await handler.Handle(
            new OnboardEmployeeCommand("E1006", "Protected", "Password123!", roleName),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(identityStore.CreatedAccounts);
        Assert.Empty(identityStore.AssignedRoles);
        Assert.Equal(0, passwordService.SetPasswordCalls);
        Assert.Null(repository.AddedEntity);
        Assert.Equal(0, unitOfWork.BeginCalls);
        Assert.Equal(0, unitOfWork.CommitCalls);
        Assert.Equal(0, unitOfWork.RollbackCalls);
    }

    [Fact]
    public async Task OnboardEmployeeHandler_ShouldRejectMissingRoleBeforeCreatingAnyData()
    {
        var repository = new InMemoryRepository<Employee>();
        var identityStore = new RecordingIdentityAccountStore();
        var passwordService = new StubIdentityPasswordService();
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new OnboardEmployeeHandler(
            identityStore,
            passwordService,
            new StubRolePolicyService { RoleExists = false },
            repository,
            unitOfWork,
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Roles = [SystemRoles.HrAdmin],
                ActorType = IIoTClaimTypes.HumanActor,
                IsAuthenticated = true
            },
            new RecordingPermissionProvider
            {
                Permissions = [CloudPermissionCatalog.Employee.UpdateAccess]
            });

        var result = await handler.Handle(
            new OnboardEmployeeCommand(
                "E1007",
                "Missing Role",
                "Password123!",
                "Role-Does-Not-Exist"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("角色未定义", result.Errors ?? []);
        Assert.Empty(identityStore.CreatedAccounts);
        Assert.Equal(0, passwordService.SetPasswordCalls);
        Assert.Null(repository.AddedEntity);
        Assert.Equal(0, unitOfWork.BeginCalls);
    }

    [Theory]
    [InlineData(IIoTClaimTypes.EdgeDeviceActor)]
    [InlineData(IIoTClaimTypes.AiServiceActor)]
    public async Task OnboardEmployeeHandler_ShouldNotGrantAccessBypassToNonHumanAdmin(
        string actorType)
    {
        var repository = new InMemoryRepository<Employee>();
        var identityStore = new RecordingIdentityAccountStore();
        var passwordService = new StubIdentityPasswordService();
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new OnboardEmployeeHandler(
            identityStore,
            passwordService,
            new StubRolePolicyService { RoleExists = true },
            repository,
            unitOfWork,
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Roles = [SystemRoles.Admin],
                ActorType = actorType,
                IsAuthenticated = true
            },
            new RecordingPermissionProvider());

        var result = await handler.Handle(
            new OnboardEmployeeCommand(
                "E1008",
                "Non Human",
                "Password123!",
                "Supervisor"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Errors ?? [],
            error => error.Contains(
                CloudPermissionCatalog.Employee.UpdateAccess,
                StringComparison.Ordinal));
        Assert.Empty(identityStore.CreatedAccounts);
        Assert.Equal(0, passwordService.SetPasswordCalls);
        Assert.Null(repository.AddedEntity);
        Assert.Equal(0, unitOfWork.BeginCalls);
    }

    [Fact]
    public async Task DeactivateEmployeeHandler_ShouldRollbackWhenIdentityDisableFails()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1004", "Rollback User");
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById =
                IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                    employeeId,
                    employee.EmployeeNo),
            SetEnabledResult = Result.Failure("disable failed")
        };
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new DeactivateEmployeeHandler(
            repository,
            identityStore,
            unitOfWork,
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard());

        var result = await handler.Handle(new DeactivateEmployeeCommand(employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(0, unitOfWork.CommitCalls);
        Assert.Equal(1, unitOfWork.RollbackCalls);
    }

    [Fact]
    public async Task DeactivateEmployeeHandler_ShouldRevokeRefreshTokensAfterSuccessfulDeactivation()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1004", "Deactivate User");
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById =
                IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                    employeeId,
                    employee.EmployeeNo)
        };
        var unitOfWork = new RecordingUnitOfWork();
        var sessionRevocationService = new StubHumanSessionRevocationService();
        var handler = new DeactivateEmployeeHandler(
            repository,
            identityStore,
            unitOfWork,
            sessionRevocationService,
            new StubAdminTargetGuard());

        var result = await handler.Handle(new DeactivateEmployeeCommand(employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(sessionRevocationService.Revocations, x =>
            x.SubjectId == employeeId
            && x.Reason == "employee-deactivated");
        Assert.Equal(1, unitOfWork.ExecuteResilientCalls);
        Assert.Equal(1, unitOfWork.CommitCalls);
    }

    [Fact]
    public async Task DeactivateEmployeeHandler_ShouldRemainIdempotentAndRevokeResidualSessions()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1005", "Inactive User");
        employee.Deactivate();
        var sessionRevocationService = new StubHumanSessionRevocationService();
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById =
                IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                    employeeId,
                    employee.EmployeeNo)
        };
        var handler = new DeactivateEmployeeHandler(
            new InMemoryRepository<Employee> { SingleOrDefaultResult = employee },
            identityStore,
            new RecordingUnitOfWork(),
            sessionRevocationService,
            new StubAdminTargetGuard());

        var first = await handler.Handle(
            new DeactivateEmployeeCommand(employeeId),
            CancellationToken.None);
        var second = await handler.Handle(
            new DeactivateEmployeeCommand(employeeId),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.False(employee.IsActive);
        Assert.Equal(2, sessionRevocationService.Revocations.Count);
    }

    [Fact]
    public async Task ActivateEmployeeHandler_ShouldRestoreBothStatesAndRequireRelogin()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1006", "Activate User");
        employee.Deactivate();
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var account =
            IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                employeeId,
                employee.EmployeeNo);
        account.Disable();
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById = account
        };
        var unitOfWork = new RecordingUnitOfWork();
        var sessionRevocationService = new StubHumanSessionRevocationService();
        var handler = new ActivateEmployeeHandler(
            repository,
            identityStore,
            unitOfWork,
            sessionRevocationService,
            new StubAdminTargetGuard(),
            new StubEmployeeMutationObservationReader());

        var result = await handler.Handle(
            new ActivateEmployeeCommand(employeeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(employee.IsActive);
        var activation = Assert.Single(identityStore.StateCompareExchanges);
        Assert.Equal(employeeId, activation.UserId);
        Assert.False(string.IsNullOrWhiteSpace(activation.SecurityStamp));
        Assert.Contains(sessionRevocationService.Revocations, revocation =>
            revocation.SubjectId == employeeId &&
            revocation.Reason == "employee-activated-relogin-required");
        Assert.Equal(1, unitOfWork.ExecuteResilientCalls);
        Assert.Equal(1, unitOfWork.CommitCalls);
    }

    [Fact]
    public async Task ActivateEmployeeHandler_ShouldRollbackWhenIdentityAccountDisappears()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1007", "Missing Identity");
        employee.Deactivate();
        var unitOfWork = new RecordingUnitOfWork();
        var sessionRevocationService = new StubHumanSessionRevocationService();
        var handler = new ActivateEmployeeHandler(
            new InMemoryRepository<Employee> { SingleOrDefaultResult = employee },
            new RecordingIdentityAccountStore
            {
                SetEnabledResult = Result.Success(false)
            },
            unitOfWork,
            sessionRevocationService,
            new StubAdminTargetGuard(),
            new StubEmployeeMutationObservationReader());

        var result = await handler.Handle(
            new ActivateEmployeeCommand(employeeId),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, unitOfWork.RollbackCalls);
        Assert.Equal(0, unitOfWork.CommitCalls);
        Assert.Empty(sessionRevocationService.Revocations);
    }

    [Fact]
    public async Task ActivateEmployeeHandler_ShouldRemainIdempotentAndRevokeAnyResidualSessions()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1008", "Active User");
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var sessionRevocationService = new StubHumanSessionRevocationService();
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById =
                IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                    employeeId,
                    employee.EmployeeNo)
        };
        var handler = new ActivateEmployeeHandler(
            repository,
            identityStore,
            new RecordingUnitOfWork(),
            sessionRevocationService,
            new StubAdminTargetGuard(),
            new StubEmployeeMutationObservationReader());

        var first = await handler.Handle(
            new ActivateEmployeeCommand(employeeId),
            CancellationToken.None);
        var second = await handler.Handle(
            new ActivateEmployeeCommand(employeeId),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(employee.IsActive);
        Assert.Equal(2, sessionRevocationService.Revocations.Count);
        Assert.All(
            identityStore.StateCompareExchanges,
            activation => Assert.Equal(employeeId, activation.UserId));
        Assert.Equal(
            2,
            identityStore.StateCompareExchanges
                .Select(activation => activation.SecurityStamp)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public async Task ActivateEmployeeHandler_ShouldRollbackWhenStatusVersionRotationFails()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1009", "Active User");
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById =
                IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                    employeeId,
                    employee.EmployeeNo),
            CompareExchangeStateResult = Result.Failure("rotation failed")
        };
        var unitOfWork = new RecordingUnitOfWork();
        var sessionRevocationService = new StubHumanSessionRevocationService();
        var handler = new ActivateEmployeeHandler(
            new InMemoryRepository<Employee> { SingleOrDefaultResult = employee },
            identityStore,
            unitOfWork,
            sessionRevocationService,
            new StubAdminTargetGuard(),
            new StubEmployeeMutationObservationReader());

        var result = await handler.Handle(
            new ActivateEmployeeCommand(employeeId),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, unitOfWork.RollbackCalls);
        Assert.Equal(0, unitOfWork.CommitCalls);
        Assert.Empty(sessionRevocationService.Revocations);
    }

    [Fact]
    public async Task ActivateEmployeeHandler_ShouldRecoverOnlyTheExactCommitTarget()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1010", "Commit Recovery");
        employee.Deactivate();
        var account =
            IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                employeeId,
                employee.EmployeeNo);
        account.Disable();
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById = account
        };
        identityStore.SecurityStampsByUserId[employeeId] = "baseline-stamp";
        var observer = new StubEmployeeMutationObservationReader
        {
            ObserveAsyncOverride = (_, _) =>
            {
                identityStore.SecurityStampsByUserId.TryGetValue(
                    employeeId,
                    out var securityStamp);
                return Task.FromResult(new EmployeeMutationObservation(
                    EmployeeExists: true,
                    EmployeeIsActive: employee.IsActive,
                    AccountExists: true,
                    AccountIsEnabled: account.IsEnabled,
                    AccountSecurityStamp: securityStamp,
                    Roles: []));
            }
        };
        var handler = new ActivateEmployeeHandler(
            new InMemoryRepository<Employee> { SingleOrDefaultResult = employee },
            identityStore,
            new RecordingUnitOfWork
            {
                OnCommit = () => throw new InvalidOperationException(
                    "commit acknowledgement lost")
            },
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            observer);

        var result = await handler.Handle(
            new ActivateEmployeeCommand(employeeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, observer.Calls);
        Assert.True(employee.IsActive);
        Assert.True(account.IsEnabled);
    }

    [Fact]
    public async Task ActivateEmployeeHandler_ShouldReturnCommitUnknownWhenOnlyBaselineCanBeObserved()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1011", "Unknown Commit");
        employee.Deactivate();
        var account =
            IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                employeeId,
                employee.EmployeeNo);
        account.Disable();
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById = account
        };
        identityStore.SecurityStampsByUserId[employeeId] = "baseline-stamp";
        var observer = new StubEmployeeMutationObservationReader
        {
            Observation = new EmployeeMutationObservation(
                EmployeeExists: true,
                EmployeeIsActive: false,
                AccountExists: true,
                AccountIsEnabled: false,
                AccountSecurityStamp: "baseline-stamp",
                Roles: [])
        };
        var handler = new ActivateEmployeeHandler(
            new InMemoryRepository<Employee> { SingleOrDefaultResult = employee },
            identityStore,
            new RecordingUnitOfWork
            {
                OnCommit = () => throw new InvalidOperationException(
                    "commit acknowledgement lost")
            },
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            observer);

        await Assert.ThrowsAsync<EmployeeActivationCommitUnknownException>(() =>
            handler.Handle(
                new ActivateEmployeeCommand(employeeId),
                CancellationToken.None));

        Assert.Equal(1, observer.Calls);
    }

    [Fact]
    public async Task ActivateEmployeeHandler_ShouldReturnConflictWhenCommitObservationHasDrifted()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1012", "Concurrent Activation");
        employee.Deactivate();
        var account =
            IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                employeeId,
                employee.EmployeeNo);
        account.Disable();
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById = account
        };
        identityStore.SecurityStampsByUserId[employeeId] = "baseline-stamp";
        var observer = new StubEmployeeMutationObservationReader
        {
            Observation = new EmployeeMutationObservation(
                EmployeeExists: true,
                EmployeeIsActive: true,
                AccountExists: true,
                AccountIsEnabled: false,
                AccountSecurityStamp: "newer-stamp",
                Roles: ["RoleAdmin"])
        };
        var handler = new ActivateEmployeeHandler(
            new InMemoryRepository<Employee> { SingleOrDefaultResult = employee },
            identityStore,
            new RecordingUnitOfWork
            {
                OnCommit = () => throw new InvalidOperationException(
                    "commit acknowledgement lost")
            },
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            observer);

        await Assert.ThrowsAsync<EmployeeActivationConflictException>(() =>
            handler.Handle(
                new ActivateEmployeeCommand(employeeId),
                CancellationToken.None));

        Assert.Equal(1, observer.Calls);
    }

    [Fact]
    public async Task ActivateEmployeeHandler_ShouldFailClosedWhenCommitObservationFails()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1013", "Observation Failure");
        employee.Deactivate();
        var account =
            IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                employeeId,
                employee.EmployeeNo);
        account.Disable();
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById = account
        };
        var observer = new StubEmployeeMutationObservationReader
        {
            ExceptionToThrow = new InvalidOperationException(
                "sensitive observation failure")
        };
        var handler = new ActivateEmployeeHandler(
            new InMemoryRepository<Employee> { SingleOrDefaultResult = employee },
            identityStore,
            new RecordingUnitOfWork
            {
                OnCommit = () => throw new InvalidOperationException(
                    "commit acknowledgement lost")
            },
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            observer);

        await Assert.ThrowsAsync<EmployeeActivationCommitUnknownException>(() =>
            handler.Handle(
                new ActivateEmployeeCommand(employeeId),
                CancellationToken.None));

        Assert.Equal(1, observer.Calls);
    }

    [Fact]
    public async Task ActivateEmployeeHandler_ShouldNotObserveAfterCallerCancellation()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1014", "Canceled Activation");
        employee.Deactivate();
        var account =
            IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                employeeId,
                employee.EmployeeNo);
        account.Disable();
        var observer = new StubEmployeeMutationObservationReader();
        var handler = new ActivateEmployeeHandler(
            new InMemoryRepository<Employee> { SingleOrDefaultResult = employee },
            new RecordingIdentityAccountStore { AccountById = account },
            new RecordingUnitOfWork
            {
                OnCommit = () => throw new OperationCanceledException(
                    new CancellationToken(canceled: true))
            },
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            observer);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.Handle(
                new ActivateEmployeeCommand(employeeId),
                CancellationToken.None));

        Assert.Equal(0, observer.Calls);
    }

    [Fact]
    public async Task ActivateEmployeeHandler_ShouldRejectCompareExchangeConflictBeforeCommit()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1015", "CAS Conflict");
        employee.Deactivate();
        var account =
            IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                employeeId,
                employee.EmployeeNo);
        account.Disable();
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById = account,
            CompareExchangeStateResult =
                Result.Success(IdentityAccountCompareExchangeOutcome.Conflict)
        };
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new ActivateEmployeeHandler(
            new InMemoryRepository<Employee> { SingleOrDefaultResult = employee },
            identityStore,
            unitOfWork,
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard(),
            new StubEmployeeMutationObservationReader());

        await Assert.ThrowsAsync<EmployeeActivationConflictException>(() =>
            handler.Handle(
                new ActivateEmployeeCommand(employeeId),
                CancellationToken.None));

        Assert.Equal(1, unitOfWork.RollbackCalls);
        Assert.Equal(0, unitOfWork.CommitCalls);
    }

    [Fact]
    public async Task TerminateEmployeeHandler_ShouldRollbackWhenIdentityDeleteFails()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1005", "Terminate User");
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById =
                IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                    employeeId,
                    employee.EmployeeNo),
            DeleteResult = Result.Failure("delete failed")
        };
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new TerminateEmployeeHandler(
            repository,
            identityStore,
            unitOfWork,
            new StubHumanSessionRevocationService(),
            new StubAdminTargetGuard());

        var result = await handler.Handle(new TerminateEmployeeCommand(employeeId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(0, unitOfWork.CommitCalls);
        Assert.Equal(1, unitOfWork.RollbackCalls);
    }

    [Fact]
    public async Task TerminateEmployeeHandler_ShouldRevokeRefreshTokensAfterSuccessfulTermination()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E1005", "Terminate User");
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var identityStore = new RecordingIdentityAccountStore
        {
            AccountById =
                IIoT.Core.Identity.Aggregates.IdentityAccounts.IdentityAccount.Create(
                    employeeId,
                    employee.EmployeeNo)
        };
        var unitOfWork = new RecordingUnitOfWork();
        var sessionRevocationService = new StubHumanSessionRevocationService();
        var handler = new TerminateEmployeeHandler(
            repository,
            identityStore,
            unitOfWork,
            sessionRevocationService,
            new StubAdminTargetGuard());

        var result = await handler.Handle(new TerminateEmployeeCommand(employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(sessionRevocationService.Revocations, x =>
            x.SubjectId == employeeId
            && x.Reason == "employee-terminated");
        Assert.Equal(1, unitOfWork.ExecuteResilientCalls);
        Assert.Equal(1, unitOfWork.CommitCalls);
    }

    [Fact]
    public async Task UpdateEmployeeAccessHandler_ShouldPersistCurrentDeviceAssignmentsWithoutCacheDependency()
    {
        var employeeId = Guid.NewGuid();
        var originalDeviceId = Guid.NewGuid();
        var updatedDeviceId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E002", "Access Owner");
        employee.AddDeviceAccess(originalDeviceId);

        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var handler = new UpdateEmployeeAccessHandler(
            repository,
            new StubAdminTargetGuard(),
            new StubDeviceReadQueryService
            {
                ExistingDeviceIds = [updatedDeviceId]
            },
            new RecordingUnitOfWork());

        var result = await handler.Handle(
            new UpdateEmployeeAccessCommand(employeeId, [updatedDeviceId]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(employee.DeviceAccesses, access => access.DeviceId == originalDeviceId);
        Assert.Contains(employee.DeviceAccesses, access => access.DeviceId == updatedDeviceId);
    }

    [Fact]
    public async Task UpdateDeviceProfileHandler_ShouldRaiseDeviceRenamedEvent()
    {
        var processId = Guid.NewGuid();
        var device = new Device("Device-01", "DEV-UPDATE001", processId);
        device.ClearDomainEvents();
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var handler = new UpdateDeviceProfileHandler(
            repository,
            new StubDeviceReadQueryService(),
            new StubCurrentUserDeviceAccessService { IsAdministrator = true });

        var result = await handler.Handle(
            new UpdateDeviceProfileCommand(device.Id, "Device-02"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(device.DomainEvents, x =>
            x is DeviceRenamedDomainEvent renamed
            && renamed.DeviceId == device.Id
            && renamed.Code == device.Code
            && renamed.ProcessId == processId);
    }

    [Fact]
    public async Task DeleteDeviceHandler_ShouldCascadeDelete_AndWriteAudit_WhileOutboxOwnsCacheInvalidation()
    {
        var processId = Guid.NewGuid();
        var device = new Device("Device-Delete", "DEV-DELETE001", processId);
        device.ClearDomainEvents();
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var dependencyQuery = new StubDeviceDeletionDependencyQueryService
        {
            Impact = new DeviceDeletionImpact(
                Recipes: 2,
                Capacities: 3,
                DeviceLogs: 4,
                PassStations: 5,
                ClientStates: 1,
                ClientVersionSnapshots: 1,
                ClientPluginVersions: 2,
                UploadReceiveRegistrations: 6,
                EmployeeDeviceAccesses: 7,
                RefreshTokenSessions: 8,
                RuntimeHeartbeats: 1,
                EdgeHostPlcRuntimeStates: 3)
        };
        var auditTrail = new RecordingAuditTrailService();
        var handler = new DeleteDeviceHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Roles = [SystemRoles.Admin],
                UserName = "admin",
                ActorType = IIoTClaimTypes.HumanActor,
                IsAuthenticated = true
            },
            repository,
            dependencyQuery,
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            auditTrail);

        var result = await handler.Handle(new DeleteDeviceCommand(device.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "Device.Delete"
            && x.TargetIdOrKey == device.Id.ToString()
            && x.Succeeded
            && x.Summary.Contains("\"DeviceCascadeDelete\"", StringComparison.Ordinal)
            && x.Summary.Contains("\"device_logs\":4", StringComparison.Ordinal)
            && !x.Summary.Contains("\"edge_hosts\"", StringComparison.Ordinal)
            && x.Summary.Contains("\"edge_host_plc_runtime_states\":3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeviceCacheInvalidationHandlers_ShouldRouteDomainEventsToCacheService()
    {
        var deviceId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var domainEventId = Guid.NewGuid();
        var cacheInvalidation = new RecordingDeviceCacheInvalidationService();
        var dispatchContext = new StaticDomainEventDispatchContext(domainEventId);

        await new DeviceRegisteredCacheInvalidationHandler(cacheInvalidation, dispatchContext).Handle(
            new DeviceRegisteredDomainEvent(deviceId, "Device-01", "DEV-CACHE001", processId),
            CancellationToken.None);
        await new DeviceRenamedCacheInvalidationHandler(cacheInvalidation, dispatchContext).Handle(
            new DeviceRenamedDomainEvent(deviceId, "Device-02", "DEV-CACHE001", processId),
            CancellationToken.None);
        await new DeviceDeletedCacheInvalidationHandler(cacheInvalidation, dispatchContext).Handle(
            new DeviceDeletedDomainEvent(deviceId, "DEV-CACHE001", processId),
            CancellationToken.None);

        Assert.Contains(processId, cacheInvalidation.RegisteredProcessIds);
        Assert.Contains(cacheInvalidation.RenamedDevices, x =>
            x.DeviceId == deviceId
            && x.ProcessId == processId);
        Assert.Contains(cacheInvalidation.DeletedDevices, x =>
            x.DeviceId == deviceId
            && x.ProcessId == processId);
        Assert.Equal(3, cacheInvalidation.DomainEventIds.Count);
        Assert.All(cacheInvalidation.DomainEventIds, id => Assert.Equal(domainEventId, id));

        var idempotentInvalidation = new RecordingIdempotentCacheInvalidationService();
        var service = new DeviceCacheInvalidationService(idempotentInvalidation);
        using var cancellation = new CancellationTokenSource();
        var descriptor = new IIoT.Services.Contracts.Caching.DeviceCacheDescriptor(deviceId, processId);

        await service.InvalidateListsAfterRegisterOnceAsync(
            domainEventId,
            processId,
            cancellation.Token);
        await service.InvalidateAfterRenameOnceAsync(
            domainEventId,
            descriptor,
            cancellation.Token);
        await service.InvalidateAfterDeleteOnceAsync(
            domainEventId,
            descriptor,
            cancellation.Token);

        Assert.Collection(
            idempotentInvalidation.Operations,
            operation =>
            {
                Assert.Equal("device-register", operation.OperationScope);
                Assert.Equal(
                    [CacheKeys.AllDevices(), CacheKeys.DevicesByProcess(processId)],
                    operation.Keys);
                Assert.Empty(operation.Patterns);
            },
            operation =>
            {
                Assert.Equal("device-rename", operation.OperationScope);
                Assert.Equal(
                    [CacheKeys.AllDevices(), CacheKeys.DevicesByProcess(processId)],
                    operation.Keys);
                Assert.Empty(operation.Patterns);
            },
            operation =>
            {
                Assert.Equal("device-delete", operation.OperationScope);
                Assert.Equal(
                    [
                        CacheKeys.AllDevices(),
                        CacheKeys.DevicesByProcess(processId),
                        CacheKeys.RecipesByDevice(deviceId)
                    ],
                    operation.Keys);
                Assert.Equal(
                    [
                        CacheKeys.CapacityHourlyPattern(deviceId),
                        CacheKeys.CapacitySummaryPattern(deviceId),
                        CacheKeys.CapacityRangePattern(deviceId),
                        CacheKeys.CapacityPagedByDevicePattern(deviceId)
                    ],
                    operation.Patterns);
            });
        Assert.All(idempotentInvalidation.Operations, operation =>
        {
            Assert.Equal(domainEventId, operation.OperationId);
            Assert.Equal(cancellation.Token, operation.CancellationToken);
        });
    }

    [Fact]
    public async Task RecipeCacheInvalidationHandlers_ShouldRouteDomainEventsToCacheService()
    {
        var recipeId = Guid.NewGuid();
        var newRecipeId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var domainEventId = Guid.NewGuid();
        var cacheInvalidation = new RecordingRecipeCacheInvalidationService();
        var dispatchContext = new StaticDomainEventDispatchContext(domainEventId);

        await new RecipeCreatedCacheInvalidationHandler(cacheInvalidation, dispatchContext).Handle(
            new RecipeCreatedDomainEvent(recipeId, "Recipe-A", "V1.0", processId, deviceId),
            CancellationToken.None);
        await new RecipeArchivedCacheInvalidationHandler(cacheInvalidation, dispatchContext).Handle(
            new RecipeArchivedDomainEvent(recipeId, "V1.0", processId, deviceId),
            CancellationToken.None);
        await new RecipeVersionUpgradedCacheInvalidationHandler(cacheInvalidation, dispatchContext).Handle(
            new RecipeVersionUpgradedDomainEvent(recipeId, newRecipeId, "Recipe-A", "V1.1", processId, deviceId),
            CancellationToken.None);
        await new RecipeDeletedCacheInvalidationHandler(cacheInvalidation, dispatchContext).Handle(
            new RecipeDeletedDomainEvent(recipeId, processId, deviceId),
            CancellationToken.None);

        Assert.Equal(4, cacheInvalidation.ChangedRecipes.Count);
        Assert.All(cacheInvalidation.ChangedRecipes, x =>
        {
            Assert.Equal(recipeId, x.RecipeId);
            Assert.Equal(processId, x.ProcessId);
            Assert.Equal(deviceId, x.DeviceId);
        });
        Assert.Equal(4, cacheInvalidation.DomainEventIds.Count);
        Assert.All(cacheInvalidation.DomainEventIds, id => Assert.Equal(domainEventId, id));

        var idempotentInvalidation = new RecordingIdempotentCacheInvalidationService();
        var service = new RecipeCacheInvalidationService(idempotentInvalidation);
        using var cancellation = new CancellationTokenSource();
        await service.InvalidateAfterChangeOnceAsync(
            domainEventId,
            new IIoT.Services.Contracts.Caching.RecipeCacheDescriptor(recipeId, processId, deviceId),
            cancellation.Token);

        var operation = Assert.Single(idempotentInvalidation.Operations);
        Assert.Equal(domainEventId, operation.OperationId);
        Assert.Equal("recipe-change", operation.OperationScope);
        Assert.Equal(
            [
                CacheKeys.Recipe(recipeId),
                CacheKeys.RecipesByProcess(processId),
                CacheKeys.RecipesByDevice(deviceId)
            ],
            operation.Keys);
        Assert.Empty(operation.Patterns);
        Assert.Equal(cancellation.Token, operation.CancellationToken);
    }

    [Fact]
    public void CreateRecipeCommandValidator_ShouldRejectInvalidStructuredParametersJson()
    {
        var validator = new CreateRecipeCommandValidator();
        var command = new CreateRecipeCommand(
            "Recipe-A",
            Guid.NewGuid(),
            Guid.NewGuid(),
            """[{"id":"speed","name":"Speed","unit":"rpm","min":12,"max":5}]""");

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(CreateRecipeCommand.ParametersJsonb));
    }

    [Fact]
    public void UpgradeRecipeVersionCommandValidator_ShouldRejectMissingParameterFields()
    {
        var validator = new UpgradeRecipeVersionCommandValidator();
        var command = new UpgradeRecipeVersionCommand(
            Guid.NewGuid(),
            "V1.1",
            """[{"id":"speed","name":"","unit":"rpm","min":1,"max":2}]""");

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(UpgradeRecipeVersionCommand.ParametersJsonb));
    }

    [Fact]
    public void UploadCommandValidators_ShouldRejectOversizedDeviceLogBatch()
    {
        var validator = new ReceiveDeviceLogCommandValidator();
        var command = new ReceiveDeviceLogCommand(
            Guid.NewGuid(),
            Enumerable.Range(0, UploadValidationLimits.MaxDeviceLogItems + 1)
                .Select(i => new DeviceLogItem
                {
                    Level = "Info",
                    Message = $"Log-{i}",
                    LogTime = DateTime.UtcNow
                })
                .ToList());

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(ReceiveDeviceLogCommand.Logs));
    }

    [Fact]
    public void UploadCommandValidators_ShouldRejectInvalidHourlyCapacityCounts()
    {
        var validator = new ReceiveHourlyCapacityCommandValidator();
        var command = new ReceiveHourlyCapacityCommand(
            Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow),
            "D",
            9,
            30,
            "09:30",
            TotalCount: 10,
            OkCount: 8,
            NgCount: 5,
            PlcName: "PLC-01");

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.ErrorMessage.Contains("OK", StringComparison.Ordinal));
    }

    [Fact]
    public void UploadCommandValidators_ShouldRejectInvalidPassStationItem()
    {
        var validator = new ReceivePassStationBatchCommandValidator(CreatePassStationSchemaProvider());
        var command = new ReceivePassStationBatchCommand(
            "cp",
            Guid.NewGuid(),
            [
                new PassStationItemInput(
                    "",
                    "OK",
                    DateTime.UtcNow.AddMinutes(-10),
                    JsonPayload("""
                    {
                      "plcCode": "P2-CP01",
                      "plcName": "正极模切01",
                      "startTime": "2026-07-24T00:00:00Z",
                      "punchingQuantity": -1,
                      "punchingSpeed": -2
                    }
                    """))
            ]);

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName.Contains(nameof(PassStationItemInput.Barcode), StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.PropertyName.Contains("punchingQuantity", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.PropertyName.Contains("punchingSpeed", StringComparison.Ordinal));
    }

    [Fact]
    public void UploadCommandValidators_ShouldAcceptStandardCellDataTransportMetadata()
    {
        var validator = new ReceivePassStationBatchCommandValidator(CreatePassStationSchemaProvider());
        var command = new ReceivePassStationBatchCommand(
            "cp",
            Guid.NewGuid(),
            [
                new PassStationItemInput(
                    "CP-CLIP-001",
                    "OK",
                    DateTime.UtcNow,
                    JsonPayload("""
                    {
                      "processType": "CP",
                      "displayLabel": "CP-CLIP-001",
                      "deviceName": "正极模切01",
                      "deviceCode": "P2-CP01",
                      "plcDeviceId": 1,
                      "cellResult": true,
                      "completedTime": "2026-07-24T00:10:00Z",
                      "uploadTargets": 3,
                      "clipSlot": "MG1",
                      "clipNo": "CP-CLIP-001",
                      "plcCode": "P2-CP01",
                      "plcName": "正极模切01",
                      "startTime": "2026-07-24T00:00:00Z",
                      "punchingQuantity": 120,
                      "punchingSpeed": 1.25
                    }
                    """))
            ]);

        Assert.True(validator.Validate(command).IsValid);
    }

    [Fact]
    public void UploadCommandValidators_ShouldRejectUnknownPassStationPayloadField()
    {
        var validator = new ReceivePassStationBatchCommandValidator(CreatePassStationSchemaProvider());
        var command = new ReceivePassStationBatchCommand(
            "ap",
            Guid.NewGuid(),
            [
                new PassStationItemInput(
                    "BC-001",
                    "OK",
                    DateTime.UtcNow,
                    JsonPayload("""
                    {
                      "plcCode": "P1-AP01",
                      "plcName": "负极模切01",
                      "startTime": "2026-07-24T00:00:00Z",
                      "punchingQuantity": 120,
                      "punchingSpeed": 1.25,
                      "extraField": "bad"
                    }
                    """))
            ]);

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName.Contains("extraField", StringComparison.Ordinal));
    }

    [Fact]
    public void UploadCommandValidators_ShouldRejectUnsupportedPassStationSchemaVersion()
    {
        var validator = new ReceivePassStationBatchCommandValidator(CreatePassStationSchemaProvider());
        var command = new ReceivePassStationBatchCommand(
            "cp",
            Guid.NewGuid(),
            [
                new PassStationItemInput(
                    "BC-001",
                    "OK",
                    DateTime.UtcNow,
                    JsonPayload("""
                    {
                      "plcCode": "P2-CP01",
                      "plcName": "正极模切01",
                      "startTime": "2026-07-24T00:00:00Z",
                      "punchingQuantity": 120,
                      "punchingSpeed": 1.25
                    }
                    """))
            ],
            SchemaVersion: 2);

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == nameof(ReceivePassStationBatchCommand.SchemaVersion));
    }

    [Fact]
    public async Task PersistHourlyCapacityHandler_ShouldUpsertRecordAndClearCapacityCaches()
    {
        var deviceId = Guid.NewGuid();
        var reportedAt = DateTime.SpecifyKind(DateTime.UtcNow.AddSeconds(-5), DateTimeKind.Utc);
        var repository = new RecordingHourlyCapacityRecordRepository();
        var cache = new RecordingCacheService();
        var handler = new PersistHourlyCapacityHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            repository,
            cache);

        var result = await handler.Handle(
            new PersistHourlyCapacityCommand(
                new HourlyCapacityReceivedEvent
                {
                    DeviceId = deviceId,
                    Date = DateOnly.FromDateTime(DateTime.UtcNow),
                    ShiftCode = "D",
                    Hour = 9,
                    Minute = 30,
                    TimeLabel = "09:30",
                    TotalCount = 16,
                    OkCount = 15,
                    NgCount = 1,
                    PlcName = "PLC-01",
                    ReceivedAtUtc = reportedAt
                }),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.LastUpsert);
        Assert.Equal(deviceId, repository.LastUpsert!.DeviceId);
        Assert.Equal(reportedAt, repository.LastUpsert.ReportedAt);
        Assert.Contains(
            CacheKeys.CapacityHourly(repository.LastUpsert.DeviceId, repository.LastUpsert.Date, repository.LastUpsert.PlcName),
            cache.RemovedKeys);
        Assert.Contains(
            CacheKeys.CapacitySummary(repository.LastUpsert.DeviceId, repository.LastUpsert.Date, repository.LastUpsert.PlcName),
            cache.RemovedKeys);
        Assert.Contains(
            CacheKeys.CapacityRange(repository.LastUpsert.DeviceId, repository.LastUpsert.Date, repository.LastUpsert.Date, repository.LastUpsert.PlcName),
            cache.RemovedKeys);
        Assert.Contains(CacheKeys.CapacityHourlyPattern(deviceId), cache.RemovedPatterns);
        Assert.Contains(CacheKeys.CapacitySummaryPattern(deviceId), cache.RemovedPatterns);
        Assert.Contains(CacheKeys.CapacityRangePattern(deviceId), cache.RemovedPatterns);
        Assert.Contains(CacheKeys.CapacityPagedByDevicePattern(deviceId), cache.RemovedPatterns);
    }

    [Fact]
    public async Task ReceiveHourlyCapacityHandler_ShouldEnqueueBeforeClearingCapacityCaches()
    {
        var deviceId = Guid.NewGuid();
        var callOrder = new List<string>();
        var cache = new RecordingCacheService(callOrder);
        var registry = new RecordingUploadReceiveRegistry(callOrder);
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var handler = new ReceiveHourlyCapacityHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            registry,
            cache);

        var request = new ReceiveHourlyCapacityCommand(
            deviceId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "D",
            9,
            30,
            "09:30",
            16,
            15,
            1,
            "PLC-01");

        var result = await handler.Handle(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("accepted", result.Value!.Code);
        Assert.False(result.Value.DuplicateAccepted);
        Assert.Equal("register", callOrder[0]);
        var enqueued = Assert.IsType<HourlyCapacityReceivedEvent>(registry.LastRegisteredEvent);
        Assert.Equal(deviceId, enqueued.DeviceId);
        Assert.True(enqueued.ReceivedAtUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(DateTimeKind.Utc, enqueued.ReceivedAtUtc.Kind);
        Assert.Equal("hourly-capacity", registry.LastMessageType);
        Assert.StartsWith("legacy:", registry.LastDeduplicationKey, StringComparison.Ordinal);
        Assert.Contains(CacheKeys.CapacityHourly(deviceId, request.Date, request.PlcName), cache.RemovedKeys);
        Assert.Contains(CacheKeys.CapacitySummary(deviceId, request.Date, request.PlcName), cache.RemovedKeys);
        Assert.Contains(CacheKeys.CapacityRange(deviceId, request.Date, request.Date, request.PlcName), cache.RemovedKeys);
        Assert.Contains(CacheKeys.CapacityHourlyPattern(deviceId), cache.RemovedPatterns);
        Assert.Contains(CacheKeys.CapacitySummaryPattern(deviceId), cache.RemovedPatterns);
        Assert.Contains(CacheKeys.CapacityRangePattern(deviceId), cache.RemovedPatterns);
        Assert.Contains(CacheKeys.CapacityPagedByDevicePattern(deviceId), cache.RemovedPatterns);
    }

    [Fact]
    public async Task ReceiveHourlyCapacityHandler_ShouldVaryDeduplicationKeyWhenCountsChange()
    {
        var deviceId = Guid.NewGuid();
        var date = new DateOnly(2026, 6, 6);
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var firstRegistry = new RecordingUploadReceiveRegistry();
        var secondRegistry = new RecordingUploadReceiveRegistry();
        var firstHandler = new ReceiveHourlyCapacityHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            firstRegistry,
            new RecordingCacheService());
        var secondHandler = new ReceiveHourlyCapacityHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            secondRegistry,
            new RecordingCacheService());

        await firstHandler.Handle(
            new ReceiveHourlyCapacityCommand(
                deviceId,
                date,
                "D",
                9,
                30,
                "09:30",
                100,
                98,
                2,
                "PLC-01"),
            CancellationToken.None);
        await secondHandler.Handle(
            new ReceiveHourlyCapacityCommand(
                deviceId,
                date,
                "D",
                9,
                30,
                "09:30",
                150,
                147,
                3,
                "PLC-01"),
            CancellationToken.None);

        Assert.StartsWith("legacy:", firstRegistry.LastDeduplicationKey, StringComparison.Ordinal);
        Assert.StartsWith("legacy:", secondRegistry.LastDeduplicationKey, StringComparison.Ordinal);
        Assert.NotEqual(firstRegistry.LastDeduplicationKey, secondRegistry.LastDeduplicationKey);
    }

    [Fact]
    public async Task ReceiveHourlyCapacityHandler_ShouldNotClearCapacityCachesWhenOutboxEnqueueFails()
    {
        var deviceId = Guid.NewGuid();
        var callOrder = new List<string>();
        var cache = new RecordingCacheService(callOrder);
        var registry = new RecordingUploadReceiveRegistry(
            callOrder,
            new InvalidOperationException("registry failed"));
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var handler = new ReceiveHourlyCapacityHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            registry,
            cache);

        var request = new ReceiveHourlyCapacityCommand(
            deviceId,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "D",
            9,
            30,
            "09:30",
            16,
            15,
            1,
            "PLC-01");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(request, CancellationToken.None));

        Assert.Equal("registry failed", exception.Message);
        Assert.Equal(["register"], callOrder);
        Assert.Empty(cache.RemovedKeys);
        Assert.Empty(cache.RemovedPatterns);
    }

    [Fact]
    public async Task ReceiveHourlyCapacityHandler_ShouldNotClearCapacityCachesForDuplicateUpload()
    {
        var deviceId = Guid.NewGuid();
        var callOrder = new List<string>();
        var cache = new RecordingCacheService(callOrder);
        var registry = new RecordingUploadReceiveRegistry(callOrder)
        {
            NextResult = UploadReceiveRegistrationResult.Duplicate(Guid.NewGuid())
        };
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var handler = new ReceiveHourlyCapacityHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            registry,
            cache);

        var result = await handler.Handle(
            new ReceiveHourlyCapacityCommand(
                deviceId,
                DateOnly.FromDateTime(DateTime.UtcNow),
                "D",
                9,
                30,
                "09:30",
                16,
                15,
                1,
                "PLC-01",
                "retry-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("duplicate_accepted", result.Value!.Code);
        Assert.True(result.Value.DuplicateAccepted);
        Assert.Equal(["register"], callOrder);
        Assert.Empty(cache.RemovedKeys);
        Assert.Empty(cache.RemovedPatterns);
    }

    [Fact]
    public async Task ReceiveDeviceLogHandler_ShouldEnqueueIntegrationEvent()
    {
        var deviceId = Guid.NewGuid();
        var registry = new RecordingUploadReceiveRegistry();
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var handler = new ReceiveDeviceLogHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            registry);

        var result = await handler.Handle(
            new ReceiveDeviceLogCommand(
                deviceId,
                [
                    new DeviceLogItem
                    {
                        Level = "Information",
                        Message = "started",
                        LogTime = DateTime.UtcNow
                    }
                ],
                "request-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("accepted", result.Value!.Code);
        Assert.False(result.Value.DuplicateAccepted);
        var enqueued = Assert.IsType<DeviceLogReceivedEvent>(registry.LastRegisteredEvent);
        Assert.Equal(deviceId, enqueued.DeviceId);
        Assert.Single(enqueued.Logs);
        Assert.Equal("device-log", registry.LastMessageType);
        Assert.Equal("request-1", registry.LastRequestId);
        Assert.Equal("request:request-1", registry.LastDeduplicationKey);
    }

    [Fact]
    public async Task ReceiveDeviceLogHandler_ShouldReturnDuplicateAcceptedForDuplicateUpload()
    {
        var deviceId = Guid.NewGuid();
        var registry = new RecordingUploadReceiveRegistry
        {
            NextResult = UploadReceiveRegistrationResult.Duplicate(Guid.NewGuid())
        };
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var handler = new ReceiveDeviceLogHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            registry);

        var result = await handler.Handle(
            new ReceiveDeviceLogCommand(
                deviceId,
                [new DeviceLogItem { Level = "WARN", Message = "alarm", LogTime = DateTime.UtcNow }],
                "duplicate-log"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("duplicate_accepted", result.Value!.Code);
        Assert.True(result.Value.DuplicateAccepted);
    }

    [Fact]
    public async Task ReceiveDeviceLogHandler_ShouldUseStableLegacyDeduplicationKey()
    {
        var deviceId = Guid.NewGuid();
        var logTime = new DateTime(2026, 4, 29, 9, 30, 0, DateTimeKind.Utc);
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var firstRegistry = new RecordingUploadReceiveRegistry();
        var secondRegistry = new RecordingUploadReceiveRegistry();
        var changedRegistry = new RecordingUploadReceiveRegistry();

        var firstHandler = new ReceiveDeviceLogHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            firstRegistry);
        var secondHandler = new ReceiveDeviceLogHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            secondRegistry);
        var changedHandler = new ReceiveDeviceLogHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            changedRegistry);

        await firstHandler.Handle(
            new ReceiveDeviceLogCommand(
                deviceId,
                [new DeviceLogItem { Level = "Information", Message = "started", LogTime = logTime }]),
            CancellationToken.None);
        await secondHandler.Handle(
            new ReceiveDeviceLogCommand(
                deviceId,
                [new DeviceLogItem { Level = "Information", Message = "started", LogTime = logTime }]),
            CancellationToken.None);
        await changedHandler.Handle(
            new ReceiveDeviceLogCommand(
                deviceId,
                [new DeviceLogItem { Level = "Warning", Message = "started", LogTime = logTime }]),
            CancellationToken.None);

        Assert.StartsWith("legacy:", firstRegistry.LastDeduplicationKey, StringComparison.Ordinal);
        Assert.Equal(firstRegistry.LastDeduplicationKey, secondRegistry.LastDeduplicationKey);
        Assert.NotEqual(firstRegistry.LastDeduplicationKey, changedRegistry.LastDeduplicationKey);
    }

    [Fact]
    public async Task ReceiveDeviceLogHandler_ShouldTreatUnspecifiedLogTimeAsUtcForLegacyDeduplication()
    {
        var deviceId = Guid.NewGuid();
        var utcLogTime = new DateTime(2026, 4, 29, 9, 30, 0, DateTimeKind.Utc);
        var unspecifiedLogTime = DateTime.SpecifyKind(utcLogTime, DateTimeKind.Unspecified);
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var utcRegistry = new RecordingUploadReceiveRegistry();
        var unspecifiedRegistry = new RecordingUploadReceiveRegistry();
        var utcHandler = new ReceiveDeviceLogHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            utcRegistry);
        var unspecifiedHandler = new ReceiveDeviceLogHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            unspecifiedRegistry);

        await utcHandler.Handle(
            new ReceiveDeviceLogCommand(
                deviceId,
                [new DeviceLogItem { Level = "Information", Message = "started", LogTime = utcLogTime }]),
            CancellationToken.None);
        await unspecifiedHandler.Handle(
            new ReceiveDeviceLogCommand(
                deviceId,
                [new DeviceLogItem { Level = "Information", Message = "started", LogTime = unspecifiedLogTime }]),
            CancellationToken.None);

        Assert.StartsWith("legacy:", utcRegistry.LastDeduplicationKey, StringComparison.Ordinal);
        Assert.Equal(utcRegistry.LastDeduplicationKey, unspecifiedRegistry.LastDeduplicationKey);
    }

    [Fact]
    public async Task ReceivePassStationBatchHandler_ShouldRegisterPassStationUpload()
    {
        var deviceId = Guid.NewGuid();
        var registry = new RecordingUploadReceiveRegistry();
        var receiveService = new PassStationReceiveService(
            new StubDeviceIdentityQueryService { Exists = true },
            registry);
        var handler = new ReceivePassStationBatchHandler(receiveService, CreatePassStationSchemaProvider());

        var result = await handler.Handle(
            new ReceivePassStationBatchCommand(
                "cp",
                deviceId,
                [
                    new PassStationItemInput(
                        "BC-001",
                        "OK",
                        new DateTime(2026, 4, 29, 9, 30, 0, DateTimeKind.Utc),
                        JsonPayload("""
                        {
                          "plcCode": "P2-CP01",
                          "plcName": "正极模切01",
                          "startTime": "2026-07-24T00:00:00Z",
                          "punchingQuantity": 120,
                          "punchingSpeed": 1.25
                        }
                        """))
                ],
                "pass-request-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("accepted", result.Value!.Code);
        Assert.False(result.Value.DuplicateAccepted);
        Assert.Equal("pass-station:cp", registry.LastMessageType);
        Assert.Equal("pass-request-1", registry.LastRequestId);
        Assert.Equal("request:pass-request-1", registry.LastDeduplicationKey);
        var registered = Assert.IsType<PassStationBatchReceivedEvent>(registry.LastRegisteredEvent);
        Assert.Equal("cp", registered.TypeKey);
        Assert.Equal("cp", registered.ProcessType);
        Assert.Single(registered.Items);
    }

    [Fact]
    public async Task ReceivePassStationBatchHandler_ShouldReturnDuplicateAcceptedForDuplicateUpload()
    {
        var deviceId = Guid.NewGuid();
        var registry = new RecordingUploadReceiveRegistry
        {
            NextResult = UploadReceiveRegistrationResult.Duplicate(Guid.NewGuid())
        };
        var receiveService = new PassStationReceiveService(
            new StubDeviceIdentityQueryService { Exists = true },
            registry);
        var handler = new ReceivePassStationBatchHandler(receiveService, CreatePassStationSchemaProvider());

        var result = await handler.Handle(
            new ReceivePassStationBatchCommand(
                "cp",
                deviceId,
                [
                    new PassStationItemInput(
                        "BC-001",
                        "OK",
                        new DateTime(2026, 4, 29, 9, 30, 0, DateTimeKind.Utc),
                        JsonPayload("""
                        {
                          "plcCode": "P2-CP01",
                          "plcName": "正极模切01",
                          "startTime": "2026-07-24T00:00:00Z",
                          "punchingQuantity": 120,
                          "punchingSpeed": 1.25
                        }
                        """))
                ],
                "duplicate-pass"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("duplicate_accepted", result.Value!.Code);
        Assert.True(result.Value.DuplicateAccepted);
    }

    [Fact]
    public async Task GetHourlyByDeviceIdHandler_ShouldBypassCacheForFreshReads()
    {
        var deviceId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var queryService = new StubCapacityQueryService
        {
            HourlyResult =
            [
                new HourlyCapacityDto(9, 30, "09:30", "D", 16, 15, 1)
            ]
        };
        var handler = new GetHourlyByDeviceIdHandler(
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            queryService);

        var first = await handler.Handle(new GetHourlyByDeviceIdQuery(deviceId, date, "PLC-01"), CancellationToken.None);
        var second = await handler.Handle(new GetHourlyByDeviceIdQuery(deviceId, date, "PLC-01"), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, queryService.HourlyCalls);
    }

    [Fact]
    public async Task GetHourlyCapacityAggregateHandler_ShouldUseCurrentUserDeviceScope()
    {
        var allowedDeviceId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var queryService = new StubCapacityQueryService
        {
            HourlyAggregateResult =
            [
                new HourlyCapacityAggregateDto(9, 0, "09:00", 20, 18, 2)
            ]
        };
        var handler = new GetHourlyCapacityAggregateHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [allowedDeviceId] },
            queryService);

        var result = await handler.Handle(
            new GetHourlyCapacityAggregateQuery(DateOnly.FromDateTime(DateTime.UtcNow), processId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(processId, queryService.LastAggregateProcessId);
        Assert.Equal(new[] { allowedDeviceId }, queryService.LastAggregateDeviceIds);
    }

    [Fact]
    public async Task GetDeviceStatusSummaryHandler_ShouldUseCurrentUserDeviceScope()
    {
        var allowedDeviceId = Guid.NewGuid();
        var queryService = new StubDeviceOperationalStatusQueryService
        {
            Summary = new DeviceStatusSummaryDto(1, 1, 0, 0, 0, DateTimeOffset.UtcNow)
        };
        var handler = new GetDeviceStatusSummaryHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [allowedDeviceId] },
            queryService);

        var result = await handler.Handle(new GetDeviceStatusSummaryQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { allowedDeviceId }, queryService.LastDeviceIds);
        Assert.NotNull(queryService.LastOfflineCutoff);
        Assert.NotNull(queryService.LastStatusWindowStart);
    }

    [Fact]
    public async Task GetDeviceStatusSummaryHandler_ShouldUseOnlyRequestedAuthorizedDevice()
    {
        var selectedDeviceId = Guid.NewGuid();
        var accessService = new StubCurrentUserDeviceAccessService
        {
            AccessibleDeviceIds = [selectedDeviceId, Guid.NewGuid()]
        };
        var queryService = new StubDeviceOperationalStatusQueryService
        {
            Summary = new DeviceStatusSummaryDto(1, 0, 1, 0, 0, DateTimeOffset.UtcNow)
        };
        var handler = new GetDeviceStatusSummaryHandler(accessService, queryService);

        var result = await handler.Handle(
            new GetDeviceStatusSummaryQuery(selectedDeviceId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(selectedDeviceId, accessService.LastCheckedDeviceId);
        Assert.Equal(0, accessService.GetAccessibleDeviceIdsCalls);
        Assert.Equal(new[] { selectedDeviceId }, queryService.LastDeviceIds);
    }

    [Fact]
    public async Task GetDeviceStatusSummaryHandler_ShouldForbidUnauthorizedRequestedDevice()
    {
        var selectedDeviceId = Guid.NewGuid();
        var queryService = new StubDeviceOperationalStatusQueryService();
        var handler = new GetDeviceStatusSummaryHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [] },
            queryService);

        var result = await handler.Handle(
            new GetDeviceStatusSummaryQuery(selectedDeviceId),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Null(queryService.LastDeviceIds);
    }

    [Fact]
    public async Task GetDeviceSelectListHandler_ShouldReturnAllDevicesForAdmin()
    {
        var repository = new InMemoryRepository<Device>();
        var processAId = Guid.NewGuid();
        var processBId = Guid.NewGuid();
        repository.ListResult.Add(new Device("Device-B", "DEV-B", processBId));
        repository.ListResult.Add(new Device("Device-A", "DEV-A", processAId));
        var processQueries = new StubProcessReadQueryService();
        processQueries.PagedProcesses.AddRange([
            new ProcessReadItem(processAId, "PROC-A", "工序 A"),
            new ProcessReadItem(processBId, "PROC-B", "工序 B")
        ]);
        var handler = new GetDeviceSelectListHandler(
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            repository,
            processQueries);

        var result = await handler.Handle(new GetDeviceSelectListQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(new[] { "Device-A", "Device-B" }, result.Value.Select(x => x.DeviceName).ToArray());
        Assert.Equal(
            new[] { processAId, processBId }.Order(),
            processQueries.LastProcessIds!.Order());
        Assert.Equal("PROC-A", result.Value[0].ProcessCode);
        Assert.Equal("工序 A", result.Value[0].ProcessName);
    }

    [Fact]
    public async Task GetDeviceSelectListHandler_ShouldReturnScopedDevicesForOperator()
    {
        var authorizedDevice = new Device("Authorized", "DEV-AUTH", Guid.NewGuid());
        var forbiddenDevice = new Device("Forbidden", "DEV-FORBID", Guid.NewGuid());
        var repository = new InMemoryRepository<Device>();
        repository.ListResult.Add(authorizedDevice);
        repository.ListResult.Add(forbiddenDevice);
        var processQueries = new StubProcessReadQueryService();
        processQueries.PagedProcesses.Add(
            new ProcessReadItem(authorizedDevice.ProcessId, "PROC-AUTH", "授权工序"));
        var handler = new GetDeviceSelectListHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [authorizedDevice.Id] },
            repository,
            processQueries);

        var result = await handler.Handle(new GetDeviceSelectListQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var device = Assert.Single(result.Value!);
        Assert.Equal(authorizedDevice.Id, device.Id);
        Assert.Equal("DEV-AUTH", device.Code);
        Assert.Equal("PROC-AUTH", device.ProcessCode);
        Assert.Equal("授权工序", device.ProcessName);
        Assert.Equal(new[] { authorizedDevice.ProcessId }, processQueries.LastProcessIds);
    }

    [Fact]
    public async Task GetDeviceSelectListHandler_ShouldReturnEmptyWhenOperatorHasNoDeviceAccess()
    {
        var repository = new InMemoryRepository<Device>();
        repository.ListResult.Add(new Device("Device-A", "DEV-A", Guid.NewGuid()));
        var handler = new GetDeviceSelectListHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [] },
            repository,
            new StubProcessReadQueryService());

        var result = await handler.Handle(new GetDeviceSelectListQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
        Assert.Null(repository.LastGetListSpecification);
    }

    [Fact]
    public async Task GetDeviceSelectListHandler_ShouldFailWhenDeviceProcessIsMissing()
    {
        var repository = new InMemoryRepository<Device>();
        repository.ListResult.Add(new Device("Device-A", "DEV-A", Guid.NewGuid()));
        var handler = new GetDeviceSelectListHandler(
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            repository,
            new StubProcessReadQueryService());

        var result = await handler.Handle(new GetDeviceSelectListQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("设备关联的工序主数据不完整。", result.Errors!);
    }

    [Fact]
    public async Task GetAllDevicesHandler_ShouldRemainAdminOnly()
    {
        var repository = new InMemoryRepository<Device>();
        var cache = new RecordingCacheService();
        var handler = new GetAllDevicesHandler(
            new StubCurrentUserDeviceAccessService(),
            repository,
            cache);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(new GetAllDevicesQuery(), CancellationToken.None));

        Assert.Equal(0, repository.GetListCalls);
        Assert.Equal(0, cache.GetOrSetCalls);
    }

    [Fact]
    public async Task GetRecentDeviceLogsHandler_ShouldCapLimitAndNormalizeWarnLevel()
    {
        var allowedDeviceId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var queryService = new StubDeviceLogQueryService();
        var handler = new GetRecentDeviceLogsHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [allowedDeviceId] },
            queryService);

        var result = await handler.Handle(
            new GetRecentDeviceLogsQuery(250, "Warning", processId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, queryService.LastRecentLimit);
        Assert.Equal(processId, queryService.LastRecentProcessId);
        Assert.Equal(new[] { allowedDeviceId }, queryService.LastRecentDeviceIds);
        Assert.Equal(new[] { "WARN", "WARNING", "ERROR", "ERR" }, queryService.LastRecentLevels);
    }

    [Fact]
    public async Task GetRecentDeviceLogsHandler_ShouldUseOnlyRequestedAuthorizedDevice()
    {
        var selectedDeviceId = Guid.NewGuid();
        var accessService = new StubCurrentUserDeviceAccessService
        {
            AccessibleDeviceIds = [selectedDeviceId, Guid.NewGuid()]
        };
        var queryService = new StubDeviceLogQueryService();
        var handler = new GetRecentDeviceLogsHandler(accessService, queryService);

        var result = await handler.Handle(
            new GetRecentDeviceLogsQuery(DeviceId: selectedDeviceId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(selectedDeviceId, accessService.LastCheckedDeviceId);
        Assert.Equal(0, accessService.GetAccessibleDeviceIdsCalls);
        Assert.Null(queryService.LastRecentProcessId);
        Assert.Equal(new[] { selectedDeviceId }, queryService.LastRecentDeviceIds);
    }

    [Fact]
    public async Task GetRecentDeviceLogsHandler_ShouldRejectAmbiguousProcessAndDeviceScope()
    {
        var queryService = new StubDeviceLogQueryService();
        var handler = new GetRecentDeviceLogsHandler(
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            queryService);

        var result = await handler.Handle(
            new GetRecentDeviceLogsQuery(
                ProcessId: Guid.NewGuid(),
                DeviceId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Null(queryService.LastRecentDeviceIds);
    }

    [Fact]
    public async Task GetRecentDeviceLogsHandler_ShouldForbidUnauthorizedRequestedDevice()
    {
        var queryService = new StubDeviceLogQueryService();
        var handler = new GetRecentDeviceLogsHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [] },
            queryService);

        var result = await handler.Handle(
            new GetRecentDeviceLogsQuery(DeviceId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Null(queryService.LastRecentDeviceIds);
    }

    [Fact]
    public async Task GetRecentAlertCountHandler_ShouldUseDefaultWindowAndCurrentUserScope()
    {
        var allowedDeviceId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var queryService = new StubDeviceLogQueryService { RecentAlertCount = 3 };
        var handler = new GetRecentAlertCountHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [allowedDeviceId] },
            queryService);

        var before = DateTimeOffset.UtcNow.AddHours(-24).AddMinutes(-1);
        var result = await handler.Handle(new GetRecentAlertCountQuery(processId), CancellationToken.None);
        var after = DateTimeOffset.UtcNow.AddHours(-24).AddMinutes(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Count);
        Assert.Equal(24, result.Value.SinceHours);
        Assert.Equal("WARN", result.Value.MinLevel);
        Assert.True(queryService.LastAlertWindowStart >= before);
        Assert.True(queryService.LastAlertWindowStart <= after);
        Assert.Equal(processId, queryService.LastAlertProcessId);
        Assert.Equal(new[] { allowedDeviceId }, queryService.LastAlertDeviceIds);
        Assert.Equal(new[] { "WARN", "WARNING", "ERROR", "ERR" }, queryService.LastAlertLevels);
    }

    [Fact]
    public async Task GetRecentAlertCountHandler_ShouldUseOnlyRequestedAuthorizedDevice()
    {
        var selectedDeviceId = Guid.NewGuid();
        var accessService = new StubCurrentUserDeviceAccessService
        {
            AccessibleDeviceIds = [selectedDeviceId, Guid.NewGuid()]
        };
        var queryService = new StubDeviceLogQueryService { RecentAlertCount = 2 };
        var handler = new GetRecentAlertCountHandler(accessService, queryService);

        var result = await handler.Handle(
            new GetRecentAlertCountQuery(DeviceId: selectedDeviceId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.Equal(selectedDeviceId, accessService.LastCheckedDeviceId);
        Assert.Equal(0, accessService.GetAccessibleDeviceIdsCalls);
        Assert.Null(queryService.LastAlertProcessId);
        Assert.Equal(new[] { selectedDeviceId }, queryService.LastAlertDeviceIds);
    }

    [Fact]
    public async Task GetRecentAlertCountHandler_ShouldRejectAmbiguousProcessAndDeviceScope()
    {
        var queryService = new StubDeviceLogQueryService();
        var handler = new GetRecentAlertCountHandler(
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            queryService);

        var result = await handler.Handle(
            new GetRecentAlertCountQuery(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Null(queryService.LastAlertDeviceIds);
    }

    [Fact]
    public async Task GetRecentAlertCountHandler_ShouldForbidUnauthorizedRequestedDevice()
    {
        var queryService = new StubDeviceLogQueryService();
        var handler = new GetRecentAlertCountHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [] },
            queryService);

        var result = await handler.Handle(
            new GetRecentAlertCountQuery(DeviceId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Null(queryService.LastAlertDeviceIds);
    }

    [Fact]
    public async Task GetMyDevicesPagedHandler_ShouldReturnScopedDevicesForOperator()
    {
        var authorizedDevice = new Device("Authorized", "DEV-AUTH-PAGED", Guid.NewGuid());
        var forbiddenDevice = new Device("Forbidden", "DEV-FORBID-PAGED", Guid.NewGuid());
        var repository = new InMemoryRepository<Device>();
        repository.ListResult.Add(authorizedDevice);
        repository.ListResult.Add(forbiddenDevice);
        var handler = new GetMyDevicesPagedHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [authorizedDevice.Id] },
            repository);

        var result = await handler.Handle(
            new GetMyDevicesPagedQuery(Page()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var device = Assert.Single(result.Value!);
        Assert.Equal(authorizedDevice.Id, device.Id);
        Assert.Equal(1, result.Value!.MetaData.TotalCount);
        Assert.NotNull(repository.LastCountSpecification);
        Assert.NotNull(repository.LastGetListSpecification);
    }

    [Fact]
    public async Task GetMyDevicesPagedHandler_ShouldFailBeforeQueryWhenCurrentUserScopeFails()
    {
        var repository = new InMemoryRepository<Device>();
        repository.ListResult.Add(new Device("Device-A", "DEV-SCOPE-FAIL", Guid.NewGuid()));
        var handler = new GetMyDevicesPagedHandler(
            new StubCurrentUserDeviceAccessService { FailureMessage = "用户凭证异常" },
            repository);

        var result = await handler.Handle(
            new GetMyDevicesPagedQuery(Page()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("用户凭证异常", result.Errors!);
        Assert.Null(repository.LastCountSpecification);
        Assert.Null(repository.LastGetListSpecification);
    }

    [Fact]
    public async Task GetMyRecipesPagedHandler_ShouldReturnScopedRecipesForOperator()
    {
        var processId = Guid.NewGuid();
        var authorizedDeviceId = Guid.NewGuid();
        var forbiddenDeviceId = Guid.NewGuid();
        var authorizedRecipe = new Recipe("Recipe-A", processId, authorizedDeviceId, "{}");
        var forbiddenRecipe = new Recipe("Recipe-B", processId, forbiddenDeviceId, "{}");
        var repository = new InMemoryRepository<Recipe>();
        repository.ListResult.Add(authorizedRecipe);
        repository.ListResult.Add(forbiddenRecipe);
        var handler = new GetMyRecipesPagedHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [authorizedDeviceId] },
            repository);

        var result = await handler.Handle(
            new GetMyRecipesPagedQuery(Page()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var recipe = Assert.Single(result.Value!);
        Assert.Equal(authorizedRecipe.Id, recipe.Id);
        Assert.Equal(1, result.Value!.MetaData.TotalCount);
    }

    [Fact]
    public async Task GetRecipeByIdHandler_ShouldRejectRecipeOutsideCurrentUserScope()
    {
        var recipe = new Recipe("Recipe-Forbidden", Guid.NewGuid(), Guid.NewGuid(), "{}");
        var repository = new InMemoryRepository<Recipe>
        {
            SingleOrDefaultResult = recipe
        };
        var accessService = new StubCurrentUserDeviceAccessService
        {
            AccessibleDeviceIds = [Guid.NewGuid()]
        };
        var cache = new RecordingCacheService();
        cache.Values[CacheKeys.Recipe(recipe.Id)] = new RecipeDetailDto(
            recipe.Id,
            recipe.RecipeName,
            recipe.Version,
            recipe.ProcessId,
            recipe.DeviceId,
            recipe.ParametersJsonb,
            recipe.Status.ToString());
        var handler = new GetRecipeByIdHandler(repository, cache, accessService);

        var result = await handler.Handle(new GetRecipeByIdQuery(recipe.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(recipe.DeviceId, accessService.LastCheckedDeviceId);
        Assert.Equal(1, cache.GetOrSetCalls);
        Assert.Equal(0, cache.FactoryCalls);
        Assert.Equal(0, repository.GetSingleOrDefaultCalls);
    }

    [Fact]
    public async Task GetDeviceLogsHandler_ShouldQueryLogsAfterDeviceAccessSucceeds()
    {
        var deviceId = Guid.NewGuid();
        var queryService = new StubDeviceLogQueryService
        {
            Items =
            [
                new DeviceLogListItemDto(
                    Guid.NewGuid(),
                    deviceId,
                    "Device-A",
                    "WARN",
                    "temperature high",
                    DateTime.UtcNow,
                    DateTime.UtcNow)
            ],
            TotalCount = 1
        };
        var handler = new GetDeviceLogsHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [deviceId] },
            queryService);

        var result = await handler.Handle(
            new GetDeviceLogsQuery(Page(), deviceId, "WARN", "temperature"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, queryService.LogsByConditionCalls);
        Assert.Equal(deviceId, queryService.LastLogsDeviceId);
        Assert.Equal("WARN", queryService.LastLogsLevel);
        Assert.Equal("temperature", queryService.LastLogsKeyword);
        Assert.Equal(1, result.Value!.MetaData.TotalCount);
    }

    [Fact]
    public async Task GetDeviceLogsHandler_ShouldRejectUnauthorizedDeviceBeforeQuery()
    {
        var deviceId = Guid.NewGuid();
        var accessService = new StubCurrentUserDeviceAccessService
        {
            AccessibleDeviceIds = [Guid.NewGuid()]
        };
        var queryService = new StubDeviceLogQueryService();
        var handler = new GetDeviceLogsHandler(accessService, queryService);

        var result = await handler.Handle(
            new GetDeviceLogsQuery(Page(), deviceId),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(deviceId, accessService.LastCheckedDeviceId);
        Assert.Equal(0, queryService.LogsByConditionCalls);
    }

    [Fact]
    public async Task GetDailyCapacityPagedHandler_ShouldReturnEmptyWhenOperatorHasNoDeviceAccess()
    {
        var (handler, queryService, cache) = CreateDailyCapacityHandler();

        var result = await handler.Handle(
            new GetDailyCapacityPagedQuery(Page()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
        Assert.Equal(0, queryService.DailyPagedCalls);
        Assert.Equal(0, cache.GetOrSetCalls);
    }

    [Fact]
    public async Task GetDailyCapacityPagedHandler_ShouldPassCurrentUserScopeForAggregateRequest()
    {
        var allowedDeviceId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var queryService = new StubCapacityQueryService
        {
            DailyPagedResult =
            [
                new DailyCapacityPagedItemDto(
                    allowedDeviceId,
                    "Device-A",
                    date,
                    20,
                    18,
                    2,
                    90m,
                    DateTime.UtcNow)
            ],
            DailyPagedTotalCount = 1
        };
        var cache = new RecordingCacheService();
        var handler = new GetDailyCapacityPagedHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [allowedDeviceId] },
            queryService,
            cache);

        var result = await handler.Handle(
            new GetDailyCapacityPagedQuery(Page(), date),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, queryService.DailyPagedCalls);
        Assert.Equal(date, queryService.LastDailyDate);
        Assert.Null(queryService.LastDailyDeviceId);
        Assert.Equal(new[] { allowedDeviceId }, queryService.LastDailyDeviceIds);
        Assert.Null(cache.LastSetKey);
    }

    [Fact]
    public async Task GetDailyCapacityPagedHandler_ShouldRejectSpecificDeviceOutsideScopeBeforeQuery()
    {
        var (handler, queryService, cache) = CreateDailyCapacityHandler(Guid.NewGuid());

        var result = await handler.Handle(
            new GetDailyCapacityPagedQuery(Page(), DeviceId: Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, queryService.DailyPagedCalls);
        Assert.Equal(0, cache.GetOrSetCalls);
    }

    [Fact]
    public async Task GetPassStationListByTypeHandler_ShouldIntersectProcessDevicesWithCurrentUserScope()
    {
        var processId = Guid.NewGuid();
        var allowedDeviceId = Guid.NewGuid();
        var processOnlyDeviceId = Guid.NewGuid();
        var queryService = new StubPassStationRecordQueryService();
        var handler = new GetPassStationListByTypeHandler(
            CreatePassStationSchemaProvider(),
            queryService,
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [allowedDeviceId, Guid.NewGuid()] },
            new StubProcessReadQueryService { DeviceIds = [allowedDeviceId, processOnlyDeviceId] });

        var result = await handler.Handle(
            new GetPassStationListByTypeQuery(new PassStationQueryRequest(
                " cp ",
                PassStationQueryModes.TimeProcess,
                Page(),
                processId,
                StartTime: DateTime.UtcNow.AddHours(-1),
                EndTime: DateTime.UtcNow)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, queryService.GetByConditionCalls);
        Assert.Equal("cp", queryService.LastRequest!.TypeKey);
        Assert.Equal(new[] { allowedDeviceId }, queryService.LastAllowedDeviceIds);
    }

    [Fact]
    public async Task GetPassStationListByTypeHandler_ShouldPreserveClipSlotAndClipNumber()
    {
        var deviceId = Guid.NewGuid();
        var item = new PassStationListItemDto(
            Guid.NewGuid(),
            deviceId,
            "CP-CLIP-001",
            "OK",
            DateTime.UtcNow,
            DateTime.UtcNow,
            new Dictionary<string, object?>
            {
                ["plcName"] = "正极模切01",
                ["clipSlot"] = "MG1"
            });
        var queryService = new StubPassStationRecordQueryService
        {
            Items = [item],
            TotalCount = 1
        };
        var handler = new GetPassStationListByTypeHandler(
            CreatePassStationSchemaProvider(),
            queryService,
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [deviceId] },
            new StubProcessReadQueryService());

        var result = await handler.Handle(
            new GetPassStationListByTypeQuery(new PassStationQueryRequest(
                "cp",
                PassStationQueryModes.DeviceLatest,
                Page(),
                DeviceId: deviceId)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var returned = Assert.Single(result.Value!);
        Assert.Equal("CP-CLIP-001", returned.Barcode);
        Assert.Equal("MG1", returned.Fields["clipSlot"]);
        Assert.Equal("正极模切01", returned.Fields["plcName"]);
    }

    [Fact]
    public async Task GetPassStationListByTypeHandler_ShouldReturnEmptyWhenProcessHasNoAccessibleDevices()
    {
        var queryService = new StubPassStationRecordQueryService();
        var handler = new GetPassStationListByTypeHandler(
            CreatePassStationSchemaProvider(),
            queryService,
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [Guid.NewGuid()] },
            new StubProcessReadQueryService { DeviceIds = [Guid.NewGuid()] });

        var result = await handler.Handle(
            new GetPassStationListByTypeQuery(new PassStationQueryRequest(
                "cp",
                PassStationQueryModes.TimeProcess,
                Page(),
                Guid.NewGuid(),
                StartTime: DateTime.UtcNow.AddHours(-1),
                EndTime: DateTime.UtcNow)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!);
        Assert.Equal(0, queryService.GetByConditionCalls);
    }

    [Fact]
    public async Task GetPassStationDetailByTypeHandler_ShouldRejectDetailOutsideCurrentUserScope()
    {
        var detail = new PassStationDetailDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "BC-001",
            "OK",
            DateTime.UtcNow,
            DateTime.UtcNow,
            []);
        var queryService = new StubPassStationRecordQueryService
        {
            Detail = detail
        };
        var handler = new GetPassStationDetailByTypeHandler(
            CreatePassStationSchemaProvider(),
            queryService,
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [Guid.NewGuid()] });

        var result = await handler.Handle(
            new GetPassStationDetailByTypeQuery("cp", detail.Id),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(1, queryService.GetDetailCalls);
    }

    [Fact]
    public async Task GetEdgeHourlyByDeviceIdHandler_ShouldBypassCacheForFreshReads()
    {
        var deviceId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var queryService = new StubCapacityQueryService
        {
            HourlyResult =
            [
                new HourlyCapacityDto(9, 30, "09:30", "D", 16, 15, 1)
            ]
        };
        var handler = new GetEdgeHourlyByDeviceIdHandler(queryService);

        var first = await handler.Handle(new GetEdgeHourlyByDeviceIdQuery(deviceId, date, "PLC-01"), CancellationToken.None);
        var second = await handler.Handle(new GetEdgeHourlyByDeviceIdQuery(deviceId, date, "PLC-01"), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, queryService.HourlyCalls);
    }

    [Fact]
    public async Task GetDeviceByInstanceHandler_ShouldNormalizeIncomingCode_AndRequireBootstrapSecret()
    {
        var bootstrapSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("Device-Bootstrap", "DEV-BOOTSTRAP1", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(bootstrapSecret));
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var refreshTokenService = new StubRefreshTokenService();
        var handler = new GetDeviceByInstanceHandler(
            repository,
            new StubJwtTokenGenerator(),
            refreshTokenService);

        var result = await handler.Handle(
            new GetDeviceByInstanceQuery($"  {device.Code.ToLowerInvariant()}  ", bootstrapSecret),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var session = Assert.IsType<BootstrapDeviceSessionResult>(result.Value);
        Assert.Equal(device.Id, session.DeviceIdentity.Id);
        Assert.StartsWith("refresh-", session.RefreshToken);
        Assert.Contains(refreshTokenService.Issues, x =>
            x.ActorType == IIoT.Services.Contracts.Identity.IIoTClaimTypes.EdgeDeviceActor
            && x.SubjectId == device.Id);

        var specification = Assert.IsAssignableFrom<ISpecification<Device>>(repository.LastGetSingleOrDefaultSpecification);
        Assert.NotNull(specification.FilterCondition);
        Assert.True(specification.FilterCondition!.Compile()(device));
    }

    [Fact]
    public async Task GetDeviceByInstanceHandler_ShouldAlwaysRequireBootstrapSecret()
    {
        var bootstrapSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("Device-Bootstrap", "DEV-SECRET01", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(bootstrapSecret));
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var handler = new GetDeviceByInstanceHandler(
            repository,
            new StubJwtTokenGenerator(),
            new StubRefreshTokenService());

        var missingSecret = await handler.Handle(
            new GetDeviceByInstanceQuery(device.Code),
            CancellationToken.None);
        var wrongSecret = await handler.Handle(
            new GetDeviceByInstanceQuery(device.Code, "wrong-secret"),
            CancellationToken.None);
        var validSecret = await handler.Handle(
            new GetDeviceByInstanceQuery(device.Code, bootstrapSecret),
            CancellationToken.None);

        Assert.False(missingSecret.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, missingSecret.Status);
        Assert.False(wrongSecret.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, wrongSecret.Status);
        Assert.True(validSecret.IsSuccess);
        Assert.Equal(device.Id, validSecret.Value!.DeviceIdentity.Id);
    }

    private static JsonElement JsonPayload(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static Pagination Page(int pageNumber = 1, int pageSize = 10)
    {
        return new Pagination
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }

    private static (
        GetDailyCapacityPagedHandler Handler,
        StubCapacityQueryService QueryService,
        RecordingCacheService Cache) CreateDailyCapacityHandler(params Guid[] accessibleDeviceIds)
    {
        var queryService = new StubCapacityQueryService();
        var cache = new RecordingCacheService();
        var handler = new GetDailyCapacityPagedHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [.. accessibleDeviceIds] },
            queryService,
            cache);
        return (handler, queryService, cache);
    }

    private static IPassStationSchemaProvider CreatePassStationSchemaProvider()
    {
        var options = new PassStationTypesOptions
        {
            Types =
            [
                new PassStationTypeDefinitionDto
                {
                    TypeKey = "cp",
                    DisplayName = "正极模切",
                    Description = "test",
                    SupportedModes = [..PassStationQueryModes.All],
                    Fields =
                    [
                        new PassStationFieldDefinitionDto { Key = "plcCode", Label = "PLC 编码", Type = PassStationFieldTypes.String, Required = true, MaxLength = 64 },
                        new PassStationFieldDefinitionDto { Key = "plcName", Label = "PLC 名称", Type = PassStationFieldTypes.String, Required = true, MaxLength = 128 },
                        new PassStationFieldDefinitionDto { Key = "clipSlot", Label = "弹夹位", Type = PassStationFieldTypes.Enum, Required = false, Options = ["MG1", "MG2"] },
                        new PassStationFieldDefinitionDto { Key = "startTime", Label = "开始时间", Type = PassStationFieldTypes.DateTime, Required = true },
                        new PassStationFieldDefinitionDto { Key = "punchingQuantity", Label = "冲切数量", Type = PassStationFieldTypes.Integer, Required = true, Min = 0 },
                        new PassStationFieldDefinitionDto { Key = "punchingSpeed", Label = "冲切速度", Type = PassStationFieldTypes.Number, Required = true, Min = 0, Precision = 5 }
                    ],
                    ListColumns = ["plcName", "clipSlot", "barcode", "cellResult", "punchingQuantity", "punchingSpeed", "completedTime"],
                    DetailSections =
                    [
                        new PassStationDetailSectionDto
                        {
                            Title = "正极模切数据",
                            Fields = ["barcode", "deviceId", "cellResult", "completedTime", "receivedAt", "plcCode", "plcName", "clipSlot", "startTime", "punchingQuantity", "punchingSpeed"]
                        }
                    ]
                },
                new PassStationTypeDefinitionDto
                {
                    TypeKey = "ap",
                    DisplayName = "负极模切",
                    Description = "test",
                    SupportedModes = [..PassStationQueryModes.All],
                    Fields =
                    [
                        new PassStationFieldDefinitionDto { Key = "plcCode", Label = "PLC 编码", Type = PassStationFieldTypes.String, Required = true, MaxLength = 64 },
                        new PassStationFieldDefinitionDto { Key = "plcName", Label = "PLC 名称", Type = PassStationFieldTypes.String, Required = true, MaxLength = 128 },
                        new PassStationFieldDefinitionDto { Key = "clipSlot", Label = "弹夹位", Type = PassStationFieldTypes.Enum, Required = false, Options = ["MG1", "MG2"] },
                        new PassStationFieldDefinitionDto { Key = "startTime", Label = "开始时间", Type = PassStationFieldTypes.DateTime, Required = true },
                        new PassStationFieldDefinitionDto { Key = "punchingQuantity", Label = "冲切数量", Type = PassStationFieldTypes.Integer, Required = true, Min = 0 },
                        new PassStationFieldDefinitionDto { Key = "punchingSpeed", Label = "冲切速度", Type = PassStationFieldTypes.Number, Required = true, Min = 0, Precision = 5 }
                    ],
                    ListColumns = ["plcName", "clipSlot", "barcode", "cellResult", "punchingQuantity", "punchingSpeed", "completedTime"],
                    DetailSections =
                    [
                        new PassStationDetailSectionDto
                        {
                            Title = "负极模切数据",
                            Fields = ["barcode", "deviceId", "cellResult", "completedTime", "receivedAt", "plcCode", "plcName", "clipSlot", "startTime", "punchingQuantity", "punchingSpeed"]
                        }
                    ]
                }
            ]
        };

        return new PassStationSchemaProvider(Options.Create(options));
    }
}
