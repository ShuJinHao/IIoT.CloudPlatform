using AutoMapper;
using System.Text.Json;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.MasterDataService.Commands.Processes;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Commands.DeviceLogs;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Commands.Recipes;
using IIoT.ProductionService.Profiles;
using IIoT.ProductionService.Queries.Capacities;
using IIoT.ProductionService.Queries.Devices;
using IIoT.ProductionService.Validators;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.Services.Contracts.Events.DeviceLogs;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.SharedKernel.Result;
using IIoT.SharedKernel.Specification;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task RegisterDeviceHandler_ShouldCreateDeviceAndClearCaches()
    {
        var repository = new InMemoryRepository<Device>();
        var processId = Guid.NewGuid();
        var processQueries = new StubProcessReadQueryService { Exists = true };
        var deviceQueries = new StubDeviceReadQueryService();
        var cacheInvalidation = new RecordingDeviceCacheInvalidationService();
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
            cacheInvalidation,
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
        Assert.Contains(processId, cacheInvalidation.RegisteredProcessIds);
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
        var cacheInvalidation = new RecordingDeviceCacheInvalidationService();
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
            cacheInvalidation,
            auditTrail);

        var result = await handler.Handle(
            new RegisterDeviceCommand("Injection-01", Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(repository.AddedEntity);
        Assert.Empty(cacheInvalidation.RegisteredProcessIds);
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
        var repository = new InMemoryRepository<Recipe>
        {
            SingleOrDefaultResult = source
        };
        repository.ListResult.Add(source);

        var cache = new RecordingCacheService();
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
            cache,
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
        Assert.Contains(CacheKeys.Recipe(source.Id), cache.RemovedKeys);
        Assert.Contains(CacheKeys.RecipesByProcess(processId), cache.RemovedKeys);
        Assert.Contains(CacheKeys.RecipesByDevice(deviceId), cache.RemovedKeys);
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
    public async Task UpdateDeviceProfileHandler_ShouldClearDeviceIdentityCache()
    {
        var processId = Guid.NewGuid();
        var device = new Device("Device-01", "DEV-UPDATE001", processId);
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var cache = new RecordingCacheService();
        var handler = new UpdateDeviceProfileHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Role = SystemRoles.Admin,
                UserName = "admin",
                IsAuthenticated = true
            },
            repository,
            cache,
            new StubDevicePermissionService());

        var result = await handler.Handle(
            new UpdateDeviceProfileCommand(device.Id, "Device-02"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(CacheKeys.DeviceIdentity(device.Id), cache.RemovedKeys);
    }

    [Fact]
    public async Task DeleteDeviceHandler_ShouldClearCaches_AndRevokeRefreshTokens()
    {
        var processId = Guid.NewGuid();
        var device = new Device("Device-Delete", "DEV-DELETE001", processId);
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var dependencyQuery = new StubDeviceDeletionDependencyQueryService();
        var refreshTokenService = new StubRefreshTokenService();
        var cacheInvalidation = new RecordingDeviceCacheInvalidationService();
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
            cacheInvalidation,
            refreshTokenService,
            new StubDevicePermissionService(),
            auditTrail);

        var result = await handler.Handle(new DeleteDeviceCommand(device.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(cacheInvalidation.DeletedDevices, x =>
            x.DeviceId == device.Id
            && x.ProcessId == processId
            && x.DeviceCode == device.Code);
        Assert.Contains(refreshTokenService.Revocations, x =>
            x.ActorType == IIoT.Services.Contracts.Identity.IIoTClaimTypes.EdgeDeviceActor
            && x.SubjectId == device.Id
            && x.Reason == "device-deleted");
        Assert.Contains(auditTrail.Entries, x =>
            x.OperationType == "Device.Delete"
            && x.TargetIdOrKey == device.Id.ToString()
            && x.Succeeded);
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
        var outbox = new RecordingIntegrationEventOutbox(callOrder);
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var handler = new ReceiveHourlyCapacityHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            outbox,
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
        Assert.Equal("enqueue", callOrder[0]);
        var enqueued = Assert.IsType<HourlyCapacityReceivedEvent>(outbox.LastEnqueuedEvent);
        Assert.Equal(deviceId, enqueued.DeviceId);
        Assert.True(enqueued.ReceivedAtUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(DateTimeKind.Utc, enqueued.ReceivedAtUtc.Kind);
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
        var outbox = new RecordingIntegrationEventOutbox(
            callOrder,
            new InvalidOperationException("outbox failed"));
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var handler = new ReceiveHourlyCapacityHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            outbox,
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

        Assert.Equal("outbox failed", exception.Message);
        Assert.Equal(["enqueue"], callOrder);
        Assert.Empty(cache.RemovedKeys);
        Assert.Empty(cache.RemovedPatterns);
    }

    [Fact]
    public async Task ReceiveDeviceLogHandler_ShouldEnqueueIntegrationEvent()
    {
        var deviceId = Guid.NewGuid();
        var outbox = new RecordingIntegrationEventOutbox();
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var handler = new ReceiveDeviceLogHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            outbox);

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
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var enqueued = Assert.IsType<DeviceLogReceivedEvent>(outbox.LastEnqueuedEvent);
        Assert.Equal(deviceId, enqueued.DeviceId);
        Assert.Single(enqueued.Logs);
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
            refreshTokenService);

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
}
