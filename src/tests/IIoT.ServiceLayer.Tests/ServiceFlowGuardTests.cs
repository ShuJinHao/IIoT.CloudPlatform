using AutoMapper;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Profiles;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.MasterDataService.Commands.Processes;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Commands.Recipes;
using IIoT.ProductionService.Queries.Capacities;
using IIoT.ProductionService.Queries.Devices;
using IIoT.Services.Common.Events.Capacities;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Specification;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class ServiceFlowGuardTests
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
        var cache = new RecordingCacheService();
        var handler = new RegisterDeviceHandler(repository, processQueries, deviceQueries, cache);

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
        Assert.Contains(CacheKeys.AllDevices(), cache.RemovedKeys);
        Assert.Contains(CacheKeys.DevicesByProcess(processId), cache.RemovedKeys);
    }

    [Fact]
    public async Task RegisterDeviceHandler_ShouldFailWhenUniqueCodeCannotBeAllocated()
    {
        var repository = new InMemoryRepository<Device>();
        var processQueries = new StubProcessReadQueryService { Exists = true };
        var deviceQueries = new StubDeviceReadQueryService { CodeExists = true };
        var cache = new RecordingCacheService();
        var handler = new RegisterDeviceHandler(repository, processQueries, deviceQueries, cache);

        var result = await handler.Handle(
            new RegisterDeviceCommand("Injection-01", Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(repository.AddedEntity);
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
                Role = "Admin",
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
    public async Task UpdateEmployeeProfileHandler_ShouldDeactivateEmployeeAndSyncIdentityState()
    {
        var employeeId = Guid.NewGuid();
        var employee = new Employee(employeeId, "E001", "Old Name");
        var repository = new InMemoryRepository<Employee>
        {
            SingleOrDefaultResult = employee
        };
        var identityStore = new RecordingIdentityAccountStore();
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new UpdateEmployeeProfileHandler(repository, identityStore, unitOfWork);

        var result = await handler.Handle(
            new UpdateEmployeeProfileCommand(employeeId, " New Name ", false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", employee.RealName);
        Assert.False(employee.IsActive);
        Assert.Contains(employee, repository.UpdatedEntities);
        Assert.Equal(employeeId, identityStore.LastSetEnabledId);
        Assert.False(identityStore.LastSetEnabledValue);
        Assert.Equal(1, unitOfWork.BeginCalls);
        Assert.Equal(1, unitOfWork.CommitCalls);
        Assert.Equal(0, unitOfWork.RollbackCalls);
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
                Role = "Admin",
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
    public async Task DeleteDeviceHandler_ShouldClearDeviceIdentityCacheAndCapacityPatterns()
    {
        var processId = Guid.NewGuid();
        var device = new Device("Device-Delete", "DEV-DELETE001", processId);
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var cache = new RecordingCacheService();
        var dependencyQuery = new StubDeviceDeletionDependencyQueryService();
        var handler = new DeleteDeviceHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                Role = "Admin",
                UserName = "admin",
                IsAuthenticated = true
            },
            repository,
            dependencyQuery,
            cache,
            new StubDevicePermissionService());

        var result = await handler.Handle(new DeleteDeviceCommand(device.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(CacheKeys.DeviceIdentity(device.Id), cache.RemovedKeys);
        Assert.Contains(CacheKeys.CapacityHourlyPattern(device.Id), cache.RemovedPatterns);
        Assert.Contains(CacheKeys.CapacitySummaryPattern(device.Id), cache.RemovedPatterns);
        Assert.Contains(CacheKeys.CapacityRangePattern(device.Id), cache.RemovedPatterns);
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
    public async Task ReceiveHourlyCapacityHandler_ShouldClearCapacityCachesBeforePublishing()
    {
        var deviceId = Guid.NewGuid();
        var cache = new RecordingCacheService();
        var publisher = new RecordingEventPublisher();
        var mapperServices = new ServiceCollection();
        mapperServices.AddLogging();
        mapperServices.AddAutoMapper(cfg => { cfg.AddProfile<ProductionProfile>(); });
        var mapper = mapperServices.BuildServiceProvider().GetRequiredService<IMapper>();
        var handler = new ReceiveHourlyCapacityHandler(
            new StubDeviceIdentityQueryService { Exists = true },
            mapper,
            publisher,
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
        var published = Assert.IsType<HourlyCapacityReceivedEvent>(publisher.LastPublishedEvent);
        Assert.Equal(deviceId, published.DeviceId);
        Assert.True(published.ReceivedAtUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(DateTimeKind.Utc, published.ReceivedAtUtc.Kind);
        Assert.Contains(CacheKeys.CapacityHourly(deviceId, request.Date, request.PlcName), cache.RemovedKeys);
        Assert.Contains(CacheKeys.CapacitySummary(deviceId, request.Date, request.PlcName), cache.RemovedKeys);
        Assert.Contains(CacheKeys.CapacityRange(deviceId, request.Date, request.Date, request.PlcName), cache.RemovedKeys);
        Assert.Contains(CacheKeys.CapacityHourlyPattern(deviceId), cache.RemovedPatterns);
        Assert.Contains(CacheKeys.CapacitySummaryPattern(deviceId), cache.RemovedPatterns);
        Assert.Contains(CacheKeys.CapacityRangePattern(deviceId), cache.RemovedPatterns);
        Assert.Contains(CacheKeys.CapacityPagedByDevicePattern(deviceId), cache.RemovedPatterns);
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
                Role = "Admin",
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
    public async Task GetDeviceByInstanceHandler_ShouldNormalizeIncomingCodeAndCacheByNormalizedCode()
    {
        var device = new Device("Device-Bootstrap", "DEV-BOOTSTRAP1", Guid.NewGuid());
        var repository = new InMemoryRepository<Device>
        {
            SingleOrDefaultResult = device
        };
        var cache = new RecordingCacheService();
        var handler = new GetDeviceByInstanceHandler(repository, cache);

        var result = await handler.Handle(
            new GetDeviceByInstanceQuery($"  {device.Code.ToLowerInvariant()}  "),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var identity = Assert.IsType<DeviceIdentityDto>(result.Value);
        Assert.Equal(device.Id, identity.Id);
        Assert.Equal(CacheKeys.DeviceCode(device.Code), cache.LastSetKey);

        var specification = Assert.IsAssignableFrom<ISpecification<Device>>(repository.LastGetSingleOrDefaultSpecification);
        Assert.NotNull(specification.FilterCondition);
        Assert.True(specification.FilterCondition!.Compile()(device));
    }
}
