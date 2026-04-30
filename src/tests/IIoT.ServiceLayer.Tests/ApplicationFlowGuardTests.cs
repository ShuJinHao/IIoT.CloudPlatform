using AutoMapper;
using System.Text.Json;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
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
using IIoT.ProductionService.Profiles;
using IIoT.ProductionService.Queries.Capacities;
using IIoT.ProductionService.Queries.Devices;
using IIoT.ProductionService.Security;
using IIoT.ProductionService.Validators;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.Services.Contracts.Events.DeviceLogs;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.SharedKernel.Result;
using IIoT.SharedKernel.Specification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

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
                Role = SystemRoles.Admin,
                IsAuthenticated = true
            },
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
        Assert.NotNull(repository.AddedEntity.BootstrapSecretHash);
        Assert.NotEqual(repository.AddedEntity.BootstrapSecretHash, created.BootstrapSecret);
        Assert.True(BootstrapSecretHasher.Verify(
            created.BootstrapSecret,
            repository.AddedEntity.BootstrapSecretHash));
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
                Role = SystemRoles.Admin,
                IsAuthenticated = true
            },
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
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Role = SystemRoles.Admin,
                UserName = "admin",
                IsAuthenticated = true
            },
            repository,
            new StubRecipeReadQueryService(),
            new StubDevicePermissionService());

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
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Role = SystemRoles.Admin,
                UserName = "admin",
                IsAuthenticated = true
            },
            repository,
            new StubDevicePermissionService());

        var result = await handler.Handle(new DeleteRecipeCommand(recipe.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(recipe, repository.ListResult);
        Assert.DoesNotContain(recipe.DomainEvents, x => x is RecipeDeletedDomainEvent);
    }

    [Fact]
    public async Task UpdateEmployeeProfileHandler_ShouldDeactivateEmployee_AndRevokeRefreshTokens()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E001", "Old Name");
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var identityStore = new RecordingIdentityAccountStore();
        var unitOfWork = new RecordingUnitOfWork();
        var refreshTokenService = new StubRefreshTokenService();
        var handler = new UpdateEmployeeProfileHandler(repository, identityStore, unitOfWork, refreshTokenService);

        var result = await handler.Handle(
            new UpdateEmployeeProfileCommand(employeeId, " New Name ", false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", employee.RealName);
        Assert.False(employee.IsActive);
        Assert.Contains(employee, repository.UpdatedEntities);
        Assert.Equal(employeeId, identityStore.LastSetEnabledId);
        Assert.False(identityStore.LastSetEnabledValue);
        Assert.Contains(refreshTokenService.Revocations, x =>
            x.ActorType == IIoT.Services.Contracts.Identity.IIoTClaimTypes.HumanActor
            && x.SubjectId == employeeId
            && x.Reason == "employee-deactivated");
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(1, unitOfWork.CommitCalls);
        Assert.Equal(0, unitOfWork.RollbackCalls);
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
            repository,
            unitOfWork);

        var result = await handler.Handle(
            new OnboardEmployeeCommand("E1003", "Operator", "Password123!", "Supervisor"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(repository.AddedEntity);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(0, unitOfWork.CommitCalls);
        Assert.Equal(1, unitOfWork.RollbackCalls);
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
            SetEnabledResult = Result.Failure("disable failed")
        };
        var unitOfWork = new RecordingUnitOfWork();
        var cache = new RecordingCacheService();
        var handler = new DeactivateEmployeeHandler(
            repository,
            identityStore,
            unitOfWork,
            cache,
            new StubRefreshTokenService());

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
        var identityStore = new RecordingIdentityAccountStore();
        var unitOfWork = new RecordingUnitOfWork();
        var cache = new RecordingCacheService();
        var refreshTokenService = new StubRefreshTokenService();
        var handler = new DeactivateEmployeeHandler(
            repository,
            identityStore,
            unitOfWork,
            cache,
            refreshTokenService);

        var result = await handler.Handle(new DeactivateEmployeeCommand(employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(refreshTokenService.Revocations, x =>
            x.ActorType == IIoT.Services.Contracts.Identity.IIoTClaimTypes.HumanActor
            && x.SubjectId == employeeId
            && x.Reason == "employee-deactivated");
        Assert.Equal(1, unitOfWork.CommitCalls);
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
            DeleteResult = Result.Failure("delete failed")
        };
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new TerminateEmployeeHandler(
            repository,
            identityStore,
            unitOfWork,
            new StubRefreshTokenService());

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
        var identityStore = new RecordingIdentityAccountStore();
        var unitOfWork = new RecordingUnitOfWork();
        var refreshTokenService = new StubRefreshTokenService();
        var handler = new TerminateEmployeeHandler(
            repository,
            identityStore,
            unitOfWork,
            refreshTokenService);

        var result = await handler.Handle(new TerminateEmployeeCommand(employeeId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(refreshTokenService.Revocations, x =>
            x.ActorType == IIoT.Services.Contracts.Identity.IIoTClaimTypes.HumanActor
            && x.SubjectId == employeeId
            && x.Reason == "employee-terminated");
        Assert.Equal(1, unitOfWork.CommitCalls);
    }

    [Fact]
    public async Task UpdateEmployeeAccessHandler_ShouldClearDeviceAccessCacheWhenAccessChanges()
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
        var cache = new RecordingCacheService();
        var handler = new UpdateEmployeeAccessHandler(repository, cache);

        var result = await handler.Handle(
            new UpdateEmployeeAccessCommand(employeeId, [updatedDeviceId]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(CacheKeys.DeviceAccessesByUser(employeeId), cache.RemovedKeys);
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
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Role = SystemRoles.Admin,
                UserName = "admin",
                IsAuthenticated = true
            },
            repository,
            new StubDevicePermissionService());

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
    public async Task DeleteDeviceHandler_ShouldRaiseDeletedEvent_AndRevokeRefreshTokens()
    {
        var processId = Guid.NewGuid();
        var device = new Device("Device-Delete", "DEV-DELETE001", processId);
        device.ClearDomainEvents();
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var dependencyQuery = new StubDeviceDeletionDependencyQueryService();
        var refreshTokenService = new StubRefreshTokenService();
        var cache = new RecordingCacheService();
        var auditTrail = new RecordingAuditTrailService();
        var handler = new DeleteDeviceHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Role = SystemRoles.Admin,
                UserName = "admin",
                IsAuthenticated = true
            },
            repository,
            dependencyQuery,
            refreshTokenService,
            new StubDevicePermissionService(),
            cache,
            auditTrail);

        var result = await handler.Handle(new DeleteDeviceCommand(device.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(device.DomainEvents, x =>
            x is DeviceDeletedDomainEvent deleted
            && deleted.DeviceId == device.Id
            && deleted.ProcessId == processId
            && deleted.Code == device.Code);
        Assert.Contains(refreshTokenService.Revocations, x =>
            x.ActorType == IIoT.Services.Contracts.Identity.IIoTClaimTypes.EdgeDeviceActor
            && x.SubjectId == device.Id
            && x.Reason == "device-deleted");
        Assert.Contains(CacheKeys.DeviceCode(device.Code), cache.RemovedKeys);
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "Device.Delete"
            && x.TargetIdOrKey == device.Id.ToString()
            && x.Succeeded);
    }

    [Fact]
    public async Task DeviceCacheInvalidationHandlers_ShouldRouteDomainEventsToCacheService()
    {
        var deviceId = Guid.NewGuid();
        var oldProcessId = Guid.NewGuid();
        var newProcessId = Guid.NewGuid();
        var cacheInvalidation = new RecordingDeviceCacheInvalidationService();

        await new DeviceRegisteredCacheInvalidationHandler(cacheInvalidation).Handle(
            new DeviceRegisteredDomainEvent(deviceId, "Device-01", "DEV-CACHE001", oldProcessId),
            CancellationToken.None);
        await new DeviceRenamedCacheInvalidationHandler(cacheInvalidation).Handle(
            new DeviceRenamedDomainEvent(deviceId, "Device-02", "DEV-CACHE001", oldProcessId),
            CancellationToken.None);
        await new DeviceProcessChangedCacheInvalidationHandler(cacheInvalidation).Handle(
            new DeviceProcessChangedDomainEvent(deviceId, "DEV-CACHE001", oldProcessId, newProcessId),
            CancellationToken.None);
        await new DeviceDeletedCacheInvalidationHandler(cacheInvalidation).Handle(
            new DeviceDeletedDomainEvent(deviceId, "DEV-CACHE001", newProcessId),
            CancellationToken.None);

        Assert.Contains(oldProcessId, cacheInvalidation.RegisteredProcessIds);
        Assert.Contains(cacheInvalidation.RenamedDevices, x =>
            x.DeviceId == deviceId
            && x.ProcessId == oldProcessId
            && x.DeviceCode == "DEV-CACHE001");
        Assert.Contains(cacheInvalidation.ProcessChangedDevices, x =>
            x.DeviceId == deviceId
            && x.OldProcessId == oldProcessId
            && x.NewProcessId == newProcessId);
        Assert.Contains(cacheInvalidation.DeletedDevices, x =>
            x.DeviceId == deviceId
            && x.ProcessId == newProcessId
            && x.DeviceCode == "DEV-CACHE001");
    }

    [Fact]
    public async Task RecipeCacheInvalidationHandlers_ShouldRouteDomainEventsToCacheService()
    {
        var recipeId = Guid.NewGuid();
        var newRecipeId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var cacheInvalidation = new RecordingRecipeCacheInvalidationService();

        await new RecipeCreatedCacheInvalidationHandler(cacheInvalidation).Handle(
            new RecipeCreatedDomainEvent(recipeId, "Recipe-A", "V1.0", processId, deviceId),
            CancellationToken.None);
        await new RecipeArchivedCacheInvalidationHandler(cacheInvalidation).Handle(
            new RecipeArchivedDomainEvent(recipeId, "V1.0", processId, deviceId),
            CancellationToken.None);
        await new RecipeVersionUpgradedCacheInvalidationHandler(cacheInvalidation).Handle(
            new RecipeVersionUpgradedDomainEvent(recipeId, newRecipeId, "Recipe-A", "V1.1", processId, deviceId),
            CancellationToken.None);
        await new RecipeDeletedCacheInvalidationHandler(cacheInvalidation).Handle(
            new RecipeDeletedDomainEvent(recipeId, processId, deviceId),
            CancellationToken.None);

        Assert.Equal(4, cacheInvalidation.ChangedRecipes.Count);
        Assert.All(cacheInvalidation.ChangedRecipes, x =>
        {
            Assert.Equal(recipeId, x.RecipeId);
            Assert.Equal(processId, x.ProcessId);
            Assert.Equal(deviceId, x.DeviceId);
        });
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
    public void UploadCommandValidators_ShouldRejectInvalidInjectionPassItem()
    {
        var validator = new ReceiveInjectionPassCommandValidator();
        var command = new ReceiveInjectionPassCommand(
            Guid.NewGuid(),
            [
                new InjectionPassItemInput(
                    "",
                    "OK",
                    DateTime.UtcNow.AddMinutes(-10),
                    DateTime.UtcNow,
                    -1,
                    DateTime.UtcNow.AddMinutes(-5),
                    12.4m,
                    -2)
            ]);

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName.Contains(nameof(InjectionPassItemInput.Barcode), StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.PropertyName.Contains(nameof(InjectionPassItemInput.InjectionVolume), StringComparison.Ordinal));
    }

    [Fact]
    public void UploadCommandValidators_ShouldRejectInvalidStackingPassItem()
    {
        var validator = new ReceiveStackingPassCommandValidator();
        var command = new ReceiveStackingPassCommand(
            Guid.NewGuid(),
            new StackingPassItemInput(
                Barcode: "",
                TrayCode: "",
                LayerCount: 0,
                SequenceNo: 0,
                CellResult: "OK",
                CompletedTime: DateTime.UtcNow));

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName.Contains(nameof(StackingPassItemInput.Barcode), StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.PropertyName.Contains(nameof(StackingPassItemInput.LayerCount), StringComparison.Ordinal));
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
        var enqueued = Assert.IsType<DeviceLogReceivedEvent>(registry.LastRegisteredEvent);
        Assert.Equal(deviceId, enqueued.DeviceId);
        Assert.Single(enqueued.Logs);
        Assert.Equal("device-log", registry.LastMessageType);
        Assert.Equal("request-1", registry.LastRequestId);
        Assert.Equal("request:request-1", registry.LastDeduplicationKey);
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
    public async Task ReceiveInjectionPassHandler_ShouldRegisterPassStationUpload()
    {
        var deviceId = Guid.NewGuid();
        var registry = new RecordingUploadReceiveRegistry();
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var receiveService = new PassStationReceiveService(
            new StubDeviceIdentityQueryService { Exists = true },
            registry);
        var handler = new ReceiveInjectionPassHandler(receiveService, mapper);

        var result = await handler.Handle(
            new ReceiveInjectionPassCommand(
                deviceId,
                [
                    new InjectionPassItemInput(
                        "BC-001",
                        "OK",
                        new DateTime(2026, 4, 29, 9, 30, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 29, 9, 20, 0, DateTimeKind.Utc),
                        10.2m,
                        new DateTime(2026, 4, 29, 9, 25, 0, DateTimeKind.Utc),
                        12.4m,
                        2.2m)
                ],
                "pass-request-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("pass-station-injection", registry.LastMessageType);
        Assert.Equal("pass-request-1", registry.LastRequestId);
        Assert.Equal("request:pass-request-1", registry.LastDeduplicationKey);
        Assert.IsType<PassDataInjectionReceivedEvent>(registry.LastRegisteredEvent);
    }

    [Fact]
    public void EventContracts_ShouldDefaultMissingSchemaVersionToV1()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        Assert.Equal(1, new HourlyCapacityReceivedEvent().SchemaVersion);
        Assert.Equal(1, new DeviceLogReceivedEvent().SchemaVersion);
        Assert.Equal(1, new PassDataInjectionReceivedEvent().SchemaVersion);
        Assert.Equal(1, new PassDataStackingReceivedEvent().SchemaVersion);
        Assert.Equal(1, JsonSerializer.Deserialize<HourlyCapacityReceivedEvent>("{}", options)!.SchemaVersion);
        Assert.Equal(1, JsonSerializer.Deserialize<DeviceLogReceivedEvent>("{}", options)!.SchemaVersion);
        Assert.Equal(1, JsonSerializer.Deserialize<PassDataInjectionReceivedEvent>("{}", options)!.SchemaVersion);
        Assert.Equal(1, JsonSerializer.Deserialize<PassDataStackingReceivedEvent>("{}", options)!.SchemaVersion);
    }

    [Fact]
    public void EventContracts_ShouldExposeIntegrationEventBoundary()
    {
        var eventTypes = new[]
        {
            typeof(HourlyCapacityReceivedEvent),
            typeof(DeviceLogReceivedEvent),
            typeof(PassDataInjectionReceivedEvent),
            typeof(PassDataStackingReceivedEvent)
        };

        foreach (var eventType in eventTypes)
        {
            Assert.True(typeof(IIntegrationEvent).IsAssignableFrom(eventType), eventType.FullName);
        }

        Assert.True(typeof(IIntegrationEvent).IsAssignableFrom(typeof(IPassStationEvent)));

        var publishMethod = typeof(IEventPublisher)
            .GetMethods()
            .Single(method =>
                string.Equals(method.Name, nameof(IEventPublisher.PublishAsync), StringComparison.Ordinal)
                && method.IsGenericMethodDefinition);
        var genericParameter = Assert.Single(publishMethod.GetGenericArguments());
        Assert.Contains(
            typeof(IIntegrationEvent),
            genericParameter.GetGenericParameterConstraints());

        IIntegrationEvent[] events =
        [
            new HourlyCapacityReceivedEvent(),
            new DeviceLogReceivedEvent(),
            new PassDataInjectionReceivedEvent(),
            new PassDataStackingReceivedEvent()
        ];

        foreach (var @event in events)
        {
            Assert.NotEqual(Guid.Empty, @event.EventId);
            Assert.True(@event.OccurredAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
            Assert.Equal(1, @event.SchemaVersion);
        }
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
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Role = SystemRoles.Admin,
                UserName = "admin",
                IsAuthenticated = true
            },
            new StubDevicePermissionService(),
            queryService);

        var first = await handler.Handle(new GetHourlyByDeviceIdQuery(deviceId, date, "PLC-01"), CancellationToken.None);
        var second = await handler.Handle(new GetHourlyByDeviceIdQuery(deviceId, date, "PLC-01"), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, queryService.HourlyCalls);
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
    public async Task GetDeviceByInstanceHandler_ShouldNormalizeIncomingCode_AndCacheByNormalizedCode()
    {
        var device = new Device("Device-Bootstrap", "DEV-BOOTSTRAP1", Guid.NewGuid());
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var cache = new RecordingCacheService();
        var refreshTokenService = new StubRefreshTokenService();
        var handler = new GetDeviceByInstanceHandler(
            repository,
            cache,
            new StubJwtTokenGenerator(),
            refreshTokenService,
            Options.Create(new BootstrapAuthOptions()));

        var result = await handler.Handle(
            new GetDeviceByInstanceQuery($"  {device.Code.ToLowerInvariant()}  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var session = Assert.IsType<BootstrapDeviceSessionResult>(result.Value);
        Assert.Equal(device.Id, session.DeviceIdentity.Id);
        Assert.StartsWith("refresh-", session.RefreshToken);
        Assert.Equal(CacheKeys.DeviceCode(device.Code), cache.LastSetKey);
        Assert.Contains(refreshTokenService.Issues, x =>
            x.ActorType == IIoT.Services.Contracts.Identity.IIoTClaimTypes.EdgeDeviceActor
            && x.SubjectId == device.Id);

        var specification = Assert.IsAssignableFrom<ISpecification<Device>>(repository.LastGetSingleOrDefaultSpecification);
        Assert.NotNull(specification.FilterCondition);
        Assert.True(specification.FilterCondition!.Compile()(device));
    }

    [Fact]
    public async Task GetDeviceByInstanceHandler_ShouldRequireBootstrapSecret_WhenEnabled()
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
            new RecordingCacheService(),
            new StubJwtTokenGenerator(),
            new StubRefreshTokenService(),
            Options.Create(new BootstrapAuthOptions { RequireSecret = true }));

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

    [Fact]
    public async Task RotateDeviceBootstrapSecretHandler_ShouldReplaceHashAndClearBootstrapCache()
    {
        var device = new Device("Device-Rotate", "DEV-ROTATE01", Guid.NewGuid());
        var oldSecret = BootstrapSecretGenerator.Generate();
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var cache = new RecordingCacheService();
        var auditTrail = new RecordingAuditTrailService();
        var handler = new RotateDeviceBootstrapSecretHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin-001",
                Role = SystemRoles.Admin,
                IsAuthenticated = true
            },
            repository,
            cache,
            auditTrail);

        var result = await handler.Handle(
            new RotateDeviceBootstrapSecretCommand(device.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rotated = Assert.IsType<RotateDeviceBootstrapSecretResultDto>(result.Value);
        Assert.Equal(device.Id, rotated.Id);
        Assert.Equal(device.Code, rotated.Code);
        Assert.NotEqual(oldHash, device.BootstrapSecretHash);
        Assert.False(BootstrapSecretHasher.Verify(oldSecret, device.BootstrapSecretHash));
        Assert.True(BootstrapSecretHasher.Verify(rotated.BootstrapSecret, device.BootstrapSecretHash));
        Assert.Contains(CacheKeys.DeviceCode(device.Code), cache.RemovedKeys);
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "Device.RotateBootstrapSecret"
            && x.Succeeded
            && x.FailureReason is null
            && !x.Summary.Contains(rotated.BootstrapSecret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RotateDeviceBootstrapSecretHandler_ShouldRejectNonAdmin()
    {
        var device = new Device("Device-Rotate-Forbidden", "DEV-ROTATE02", Guid.NewGuid());
        var oldSecret = BootstrapSecretGenerator.Generate();
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var cache = new RecordingCacheService();
        var auditTrail = new RecordingAuditTrailService();
        var handler = new RotateDeviceBootstrapSecretHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "operator-001",
                Role = "Operator",
                IsAuthenticated = true
            },
            repository,
            cache,
            auditTrail);

        var result = await handler.Handle(
            new RotateDeviceBootstrapSecretCommand(device.Id),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(oldHash, device.BootstrapSecretHash);
        Assert.Empty(cache.RemovedKeys);
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "Device.RotateBootstrapSecret"
            && !x.Succeeded
            && x.FailureReason is not null);
    }
}
