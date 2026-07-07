using System.Linq.Expressions;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Contracts.EdgeHosts;
using IIoT.ProductionService.EdgeHosts;
using IIoT.ProductionService.Commands.EdgeHosts;
using IIoT.ProductionService.Queries.EdgeHosts;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using IIoT.SharedKernel.Specification;
using Xunit;

namespace IIoT.ProductionService.Tests;

public sealed class EdgeHostBehaviorTests
{
    [Fact]
    public void HumanEdgeHostRequests_ShouldUseDedicatedPermissionsWithoutAdminOnly()
    {
        var expectedPermissions = new Dictionary<Type, string>
        {
            [typeof(GetEdgeHostPagedListQuery)] = EdgeHostPermissions.Read,
            [typeof(GetEdgeHostDetailQuery)] = EdgeHostPermissions.Read,
            [typeof(GetEdgeHostPlcRuntimeStatesQuery)] = EdgeHostPermissions.Read,
            [typeof(CreateEdgeHostCommand)] = EdgeHostPermissions.Manage,
            [typeof(UpdateEdgeHostCommand)] = EdgeHostPermissions.Manage,
            [typeof(EnableEdgeHostCommand)] = EdgeHostPermissions.Manage,
            [typeof(DisableEdgeHostCommand)] = EdgeHostPermissions.Manage,
            [typeof(DeleteEdgeHostCommand)] = EdgeHostPermissions.Manage,
            [typeof(AddEdgeHostPlcBindingCommand)] = EdgeHostPermissions.Manage,
            [typeof(UpdateEdgeHostPlcBindingCommand)] = EdgeHostPermissions.Manage,
            [typeof(EnableEdgeHostPlcBindingCommand)] = EdgeHostPermissions.Manage,
            [typeof(DisableEdgeHostPlcBindingCommand)] = EdgeHostPermissions.Manage,
            [typeof(RemoveEdgeHostPlcBindingCommand)] = EdgeHostPermissions.Manage
        };

        foreach (var (requestType, expectedPermission) in expectedPermissions)
        {
            var permission = requestType
                .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
                .Cast<AuthorizeRequirementAttribute>()
                .Single();

            Assert.Equal(expectedPermission, permission.Permission);
            Assert.Empty(requestType.GetCustomAttributes(typeof(AdminOnlyAttribute), inherit: false));
        }

        var capacityPermissions = typeof(GetEdgeHostPlcCapacitySummaryQuery)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
            .Cast<AuthorizeRequirementAttribute>()
            .Select(attribute => attribute.Permission)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains(EdgeHostPermissions.Read, capacityPermissions);
        Assert.Contains(DevicePermissions.Read, capacityPermissions);
        Assert.Empty(typeof(GetEdgeHostPlcCapacitySummaryQuery)
            .GetCustomAttributes(typeof(AdminOnlyAttribute), inherit: false));
    }

    [Fact]
    public void EdgeHostPlcRuntimeStateReport_ShouldStayOnEdgeRequestSurface()
    {
        Assert.True(typeof(IDeviceRequest<Result<EdgeHostPlcRuntimeStateReportResultDto>>)
            .IsAssignableFrom(typeof(ReportEdgeHostPlcRuntimeStatesCommand)));
        Assert.Empty(typeof(ReportEdgeHostPlcRuntimeStatesCommand)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false));
        Assert.Empty(typeof(ReportEdgeHostPlcRuntimeStatesCommand)
            .GetCustomAttributes(typeof(AdminOnlyAttribute), inherit: false));
    }

    [Fact]
    public async Task CreateEdgeHostHandler_ShouldCreateHostWhenDeviceAndClientCodeMatch()
    {
        var device = new Device("上位机设备", "DEV-EDGEHOST01", Guid.NewGuid());
        var edgeHostRepository = new InMemoryRepository<EdgeHost>();
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.ListResult.Add(device);
        var audit = new RecordingAuditTrailService();
        var handler = new CreateEdgeHostHandler(
            edgeHostRepository,
            deviceRepository,
            new StubCurrentUser(),
            audit);

        var result = await handler.Handle(
            new CreateEdgeHostCommand(device.Id, " dev-edgehost01 ", " 模切上位机 ", "现场 A 线"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        Assert.NotNull(edgeHostRepository.AddedEntity);
        Assert.Equal(device.Id, edgeHostRepository.AddedEntity!.DeviceId);
        Assert.Equal(device.Code, edgeHostRepository.AddedEntity.ClientCode);
        Assert.Equal("模切上位机", edgeHostRepository.AddedEntity.HostName);
        Assert.Contains(audit.Entries, entry =>
            entry.OperationType == "EdgeHost.Create"
            && entry.TargetType == "EdgeHost"
            && entry.Succeeded);
    }

    [Fact]
    public async Task CreateEdgeHostHandler_ShouldRejectClientCodeThatDoesNotMatchDeviceCode()
    {
        var device = new Device("上位机设备", "DEV-EDGEHOST02", Guid.NewGuid());
        var edgeHostRepository = new InMemoryRepository<EdgeHost>();
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.ListResult.Add(device);
        var audit = new RecordingAuditTrailService();
        var handler = new CreateEdgeHostHandler(
            edgeHostRepository,
            deviceRepository,
            new StubCurrentUser(),
            audit);

        var result = await handler.Handle(
            new CreateEdgeHostCommand(device.Id, "DEV-OTHER", "模切上位机"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Null(edgeHostRepository.AddedEntity);
        Assert.Contains(audit.Entries, entry =>
            entry.OperationType == "EdgeHost.Create"
            && !entry.Succeeded
            && entry.FailureReason == "ClientCode 与绑定设备的寻址码不一致。");
    }

    [Fact]
    public async Task AddEdgeHostPlcBindingHandler_ShouldValidateRelationsAndUpdateAggregate()
    {
        var host = new EdgeHost(Guid.NewGuid(), "DEV-EDGEHOST03", "模切上位机");
        var processId = Guid.NewGuid();
        var businessDeviceId = Guid.NewGuid();
        var repository = new InMemoryRepository<EdgeHost> { SingleOrDefaultResult = host };
        var audit = new RecordingAuditTrailService();
        var handler = new AddEdgeHostPlcBindingHandler(
            repository,
            new StubDeviceReadQueryService { Exists = true, ExistsInProcess = true },
            new StubProcessReadQueryService { Exists = true },
            new StubCurrentUser(),
            audit);

        var result = await handler.Handle(
            new AddEdgeHostPlcBindingCommand(
                host.Id,
                " plc-cut-01 ",
                " 模切 PLC 01 ",
                processId,
                businessDeviceId,
                StationCode: "S01",
                Protocol: "ModbusTcp",
                Address: "192.168.1.10:502",
                DisplayOrder: 10,
                Remark: "主 PLC"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        var binding = Assert.Single(host.PlcBindings);
        Assert.Equal("PLC-CUT-01", binding.PlcCode);
        Assert.Equal("模切 PLC 01", binding.PlcName);
        Assert.Equal(processId, binding.ProcessId);
        Assert.Equal(businessDeviceId, binding.BusinessDeviceId);
        Assert.Contains(host, repository.UpdatedEntities);
        Assert.Contains(audit.Entries, entry =>
            entry.OperationType == "EdgeHost.PlcBinding.Add"
            && entry.TargetType == "EdgeHost"
            && entry.Succeeded);
    }

    [Fact]
    public async Task AddEdgeHostPlcBindingHandler_ShouldRejectBusinessDeviceOutsideProcess()
    {
        var host = new EdgeHost(Guid.NewGuid(), "DEV-EDGEHOST04", "模切上位机");
        var repository = new InMemoryRepository<EdgeHost> { SingleOrDefaultResult = host };
        var audit = new RecordingAuditTrailService();
        var handler = new AddEdgeHostPlcBindingHandler(
            repository,
            new StubDeviceReadQueryService { Exists = true, ExistsInProcess = false },
            new StubProcessReadQueryService { Exists = true },
            new StubCurrentUser(),
            audit);

        var result = await handler.Handle(
            new AddEdgeHostPlcBindingCommand(
                host.Id,
                "PLC-CUT-02",
                "模切 PLC 02",
                Guid.NewGuid(),
                Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Empty(host.PlcBindings);
        Assert.Empty(repository.UpdatedEntities);
        Assert.Contains(audit.Entries, entry =>
            entry.OperationType == "EdgeHost.PlcBinding.Add"
            && !entry.Succeeded
            && entry.FailureReason == "PLC 绑定业务设备不属于指定工序。");
    }

    [Fact]
    public async Task AddEdgeHostPlcBindingHandler_ShouldRejectDuplicatePlcCodeInAggregate()
    {
        var host = new EdgeHost(Guid.NewGuid(), "DEV-EDGEHOST05", "模切上位机");
        host.AddPlcBinding("PLC-CUT-03", "模切 PLC 03");
        var repository = new InMemoryRepository<EdgeHost> { SingleOrDefaultResult = host };
        var audit = new RecordingAuditTrailService();
        var handler = new AddEdgeHostPlcBindingHandler(
            repository,
            new StubDeviceReadQueryService(),
            new StubProcessReadQueryService(),
            new StubCurrentUser(),
            audit);

        var result = await handler.Handle(
            new AddEdgeHostPlcBindingCommand(host.Id, "plc-cut-03", "重复 PLC"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Single(host.PlcBindings);
        Assert.Empty(repository.UpdatedEntities);
        Assert.Contains(audit.Entries, entry =>
            entry.OperationType == "EdgeHost.PlcBinding.Add"
            && !entry.Succeeded
            && entry.FailureReason == "PLC 编码已绑定到该上位机。");
    }

    [Fact]
    public async Task ReportEdgeHostPlcRuntimeStatesHandler_ShouldUpsertConfiguredAndUnconfiguredStates()
    {
        var deviceId = Guid.NewGuid();
        var host = new EdgeHost(deviceId, "DEV-PLCSTATE01", "模切上位机");
        var binding = host.AddPlcBinding("PLC-CUT-01", "模切 PLC 01");
        var repository = new InMemoryRepository<EdgeHost> { SingleOrDefaultResult = host };
        var store = new StubEdgeHostPlcRuntimeStateStore();
        var handler = new ReportEdgeHostPlcRuntimeStatesHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-PLCSTATE01")),
            repository,
            store);
        var reportedAtUtc = new DateTime(2026, 7, 3, 9, 0, 0, DateTimeKind.Utc);

        var result = await handler.Handle(
            new ReportEdgeHostPlcRuntimeStatesCommand(
                deviceId,
                " dev-plcstate01 ",
                reportedAtUtc,
                [
                    new EdgeHostPlcRuntimeStateReportItem(
                        " plc-cut-01 ",
                        "现场 PLC 01",
                        IsConnected: true,
                        RuntimeStatus: "connected",
                        ObservedAtUtc: reportedAtUtc.AddSeconds(-3),
                        Protocol: "ModbusTcp",
                        Address: "192.168.1.10:502"),
                    new EdgeHostPlcRuntimeStateReportItem(
                        "plc-new-01",
                        "临时 PLC",
                        IsConnected: false,
                        RuntimeStatus: "faulted",
                        ObservedAtUtc: reportedAtUtc,
                        LastError: "连接超时")
                ]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        Assert.Equal(2, result.Value!.ReceivedCount);
        Assert.Equal(1, result.Value.ConfiguredCount);
        Assert.Equal(1, result.Value.UnconfiguredCount);
        Assert.True(store.SaveChangesCalled);

        var configured = Assert.Single(store.States, state => state.PlcCode == "PLC-CUT-01");
        Assert.Equal(binding.Id, configured.PlcBindingId);
        Assert.True(configured.IsConnected);
        Assert.Equal(EdgeHostPlcRuntimeStatus.Connected, configured.RuntimeStatus);
        Assert.Equal("ModbusTcp", configured.Protocol);

        var unconfigured = Assert.Single(store.States, state => state.PlcCode == "PLC-NEW-01");
        Assert.Null(unconfigured.PlcBindingId);
        Assert.False(unconfigured.IsConnected);
        Assert.Equal(EdgeHostPlcRuntimeStatus.Faulted, unconfigured.RuntimeStatus);
        Assert.Equal("连接超时", unconfigured.LastError);
    }

    [Fact]
    public async Task ReportEdgeHostPlcRuntimeStatesHandler_ShouldRejectMismatchedIdentity()
    {
        var deviceId = Guid.NewGuid();
        var store = new StubEdgeHostPlcRuntimeStateStore();
        var handler = new ReportEdgeHostPlcRuntimeStatesHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-PLCSTATE02")),
            new InMemoryRepository<EdgeHost>(),
            store);

        var result = await handler.Handle(
            new ReportEdgeHostPlcRuntimeStatesCommand(
                deviceId,
                "DEV-OTHER",
                DateTime.UtcNow,
                [new EdgeHostPlcRuntimeStateReportItem("PLC-CUT-01", null, true)]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors ?? [], error => error.Contains("ClientCode 与 DeviceId 不匹配", StringComparison.Ordinal));
        Assert.Empty(store.States);
        Assert.False(store.SaveChangesCalled);
    }

    [Fact]
    public async Task ReportEdgeHostPlcRuntimeStatesHandler_ShouldRejectDuplicatePlcCodesInOneReport()
    {
        var deviceId = Guid.NewGuid();
        var host = new EdgeHost(deviceId, "DEV-PLCSTATE03", "模切上位机");
        var store = new StubEdgeHostPlcRuntimeStateStore();
        var handler = new ReportEdgeHostPlcRuntimeStatesHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-PLCSTATE03")),
            new InMemoryRepository<EdgeHost> { SingleOrDefaultResult = host },
            store);

        var result = await handler.Handle(
            new ReportEdgeHostPlcRuntimeStatesCommand(
                deviceId,
                "DEV-PLCSTATE03",
                DateTime.UtcNow,
                [
                    new EdgeHostPlcRuntimeStateReportItem("PLC-CUT-01", null, true),
                    new EdgeHostPlcRuntimeStateReportItem("plc-cut-01", null, false)
                ]),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Contains("同一次 PLC 状态上报不能包含重复 PLC 编码。", result.Errors ?? []);
        Assert.Empty(store.States);
        Assert.False(store.SaveChangesCalled);
    }

    [Fact]
    public async Task GetEdgeHostPlcRuntimeStatesHandler_ShouldMergeRuntimeStatesWithBindingConfiguration()
    {
        var deviceId = Guid.NewGuid();
        var processId = Guid.NewGuid();
        var businessDeviceId = Guid.NewGuid();
        var host = new EdgeHost(deviceId, "DEV-PLCSTATE04", "模切上位机");
        var binding = host.AddPlcBinding(
            "PLC-CUT-01",
            "模切 PLC 01",
            processId,
            businessDeviceId,
            stationCode: "S01",
            protocol: "ModbusTcp",
            address: "192.168.1.10:502");
        var configuredState = new EdgeHostPlcRuntimeState(host.Id, deviceId, "DEV-PLCSTATE04", "PLC-CUT-01");
        configuredState.ReplaceReport(host.Id, binding.Id, "现场 PLC 01", true, "Connected", DateTime.UtcNow);
        var unconfiguredState = new EdgeHostPlcRuntimeState(host.Id, deviceId, "DEV-PLCSTATE04", "PLC-NEW-01");
        unconfiguredState.ReplaceReport(host.Id, null, "临时 PLC", false, "Disconnected", DateTime.UtcNow.AddSeconds(-1));
        var handler = new GetEdgeHostPlcRuntimeStatesHandler(
            new InMemoryRepository<EdgeHost> { SingleOrDefaultResult = host },
            new StubEdgeHostPlcRuntimeStateStore
            {
                States = { configuredState, unconfiguredState }
            });

        var result = await handler.Handle(new GetEdgeHostPlcRuntimeStatesQuery(host.Id), CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        Assert.Equal(2, result.Value!.Count);

        var configured = Assert.Single(result.Value, item => item.PlcCode == "PLC-CUT-01");
        Assert.True(configured.IsConfigured);
        Assert.True(configured.ConfigEnabled);
        Assert.Equal(binding.Id, configured.PlcBindingId);
        Assert.Equal(processId, configured.ProcessId);
        Assert.Equal(businessDeviceId, configured.BusinessDeviceId);
        Assert.Equal("S01", configured.ConfiguredStationCode);

        var unconfigured = Assert.Single(result.Value, item => item.PlcCode == "PLC-NEW-01");
        Assert.False(unconfigured.IsConfigured);
        Assert.Null(unconfigured.PlcBindingId);
        Assert.Null(unconfigured.ConfigEnabled);
    }

    [Fact]
    public async Task GetEdgeHostPlcCapacitySummaryHandler_ShouldReadCapacityOnlyForMappedAccessibleEnabledBindings()
    {
        var host = new EdgeHost(Guid.NewGuid(), "DEV-CAPACITY01", "模切上位机");
        var processId = Guid.NewGuid();
        var allowedDeviceId = Guid.NewGuid();
        var deniedDeviceId = Guid.NewGuid();
        var readyBinding = host.AddPlcBinding(
            "PLC-CUT-01",
            "模切 PLC 01",
            processId,
            allowedDeviceId,
            displayOrder: 10);
        host.AddPlcBinding("PLC-CUT-02", "未映射 PLC", displayOrder: 20);
        var disabledBinding = host.AddPlcBinding(
            "PLC-CUT-03",
            "禁用 PLC",
            processId,
            allowedDeviceId,
            displayOrder: 30,
            enabled: false);
        var deniedBinding = host.AddPlcBinding(
            "PLC-CUT-04",
            "无权 PLC",
            processId,
            deniedDeviceId,
            displayOrder: 40);
        var summary = new DailySummaryDto(
            TotalCount: 120,
            OkCount: 118,
            NgCount: 2,
            DayShiftTotal: 70,
            DayShiftOk: 69,
            DayShiftNg: 1,
            NightShiftTotal: 50,
            NightShiftOk: 49,
            NightShiftNg: 1);
        var capacityQuery = new StubCapacityQueryService { Summary = summary };
        var handler = new GetEdgeHostPlcCapacitySummaryHandler(
            new InMemoryRepository<EdgeHost> { SingleOrDefaultResult = host },
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [allowedDeviceId] },
            capacityQuery);

        var result = await handler.Handle(
            new GetEdgeHostPlcCapacitySummaryQuery(host.Id, new DateOnly(2026, 7, 3)),
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        Assert.Equal(4, result.Value!.Count);

        var ready = Assert.Single(result.Value, item => item.PlcBindingId == readyBinding.Id);
        Assert.True(ready.CanReadCapacity);
        Assert.Equal("Ready", ready.CapacityStatus);
        Assert.Equal(summary, ready.Summary);
        Assert.Equal(processId, ready.ProcessId);
        Assert.Equal(allowedDeviceId, ready.BusinessDeviceId);

        var noBusinessDevice = Assert.Single(result.Value, item => item.PlcCode == "PLC-CUT-02");
        Assert.False(noBusinessDevice.CanReadCapacity);
        Assert.Equal("NoBusinessDevice", noBusinessDevice.CapacityStatus);
        Assert.Null(noBusinessDevice.Summary);

        var disabled = Assert.Single(result.Value, item => item.PlcBindingId == disabledBinding.Id);
        Assert.False(disabled.CanReadCapacity);
        Assert.Equal("BindingDisabled", disabled.CapacityStatus);

        var denied = Assert.Single(result.Value, item => item.PlcBindingId == deniedBinding.Id);
        Assert.False(denied.CanReadCapacity);
        Assert.Equal("NoDeviceAccess", denied.CapacityStatus);

        var call = Assert.Single(capacityQuery.SummaryCalls);
        Assert.Equal(allowedDeviceId, call.DeviceId);
        Assert.Equal("模切 PLC 01", call.PlcName);
    }

    private sealed class InMemoryRepository<T> : IRepository<T>
        where T : class, IEntity, IAggregateRoot
    {
        public T? SingleOrDefaultResult { get; init; }

        public List<T> ListResult { get; } = [];

        public T? AddedEntity { get; private set; }

        public List<T> UpdatedEntities { get; } = [];

        public T Add(T entity)
        {
            AddedEntity = entity;
            ListResult.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
            UpdatedEntities.Add(entity);
            if (!ListResult.Contains(entity))
            {
                ListResult.Add(entity);
            }
        }

        public void Delete(T entity)
        {
            ListResult.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<List<T>> GetListAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApplySpecification(specification).ToList());
        }

        public Task<T?> GetSingleOrDefaultAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SingleOrDefaultResult ?? ApplySpecification(specification).SingleOrDefault());
        }

        public Task<int> CountAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApplySpecification(specification).Count());
        }

        public Task<bool> AnyAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApplySpecification(specification).Any());
        }

        public Task<bool> AnyAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ListResult.AsQueryable().Any(predicate));
        }

        public Task<int> CountAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ListResult.AsQueryable().Count(predicate));
        }

        private IEnumerable<T> ApplySpecification(ISpecification<T>? specification)
        {
            IEnumerable<T> query = ListResult;

            if (specification?.FilterCondition is not null)
            {
                query = query.Where(specification.FilterCondition.Compile());
            }

            if (specification?.OrderBy is not null)
            {
                query = query.OrderBy(specification.OrderBy.Compile());
            }

            if (specification?.IsPagingEnabled == true)
            {
                query = query.Skip(specification.Skip).Take(specification.Take);
            }

            return query;
        }
    }

    private sealed class RecordingAuditTrailService : IAuditTrailService
    {
        public List<AuditTrailEntry> Entries { get; } = [];

        public Task TryWriteAsync(
            AuditTrailEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public string? Id { get; } = Guid.NewGuid().ToString();

        public string? UserName { get; } = "E0001";

        public IReadOnlyCollection<string> Roles { get; } = ["DeviceAdmin"];

        public string? ActorType { get; } = "Human";

        public IReadOnlyCollection<string> Permissions { get; } =
            [EdgeHostPermissions.Read, EdgeHostPermissions.Manage];

        public Guid? DeviceId { get; } = null;

        public bool IsAuthenticated { get; } = true;
    }

    private sealed class StubDeviceReadQueryService : IDeviceReadQueryService
    {
        public bool Exists { get; init; } = true;

        public bool ExistsInProcess { get; init; } = true;

        public Task<bool> ExistsAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Exists);
        }

        public Task<bool> ExistsInProcessAsync(
            Guid deviceId,
            Guid processId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistsInProcess);
        }

        public Task<bool> CodeExistsAsync(
            string code,
            Guid? excludingDeviceId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> NameExistsAsync(
            string name,
            Guid? excludingDeviceId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class StubProcessReadQueryService : IProcessReadQueryService
    {
        public bool Exists { get; init; } = true;

        public Task<(IReadOnlyList<ProcessReadItem> Items, int TotalCount)> GetPagedAsync(
            string? keyword,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(((IReadOnlyList<ProcessReadItem>)[], 0));
        }

        public Task<bool> ExistsAsync(Guid processId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Exists);
        }

        public Task<bool> CodeExistsAsync(
            string processCode,
            Guid? excludingProcessId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<IReadOnlyList<Guid>> GetDeviceIdsAsync(
            Guid processId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<Guid>)[]);
        }

        public Task<bool> HasDevicesAsync(Guid processId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<bool> HasRecipesAsync(Guid processId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }

    private sealed class StubDeviceIdentityQueryService(DeviceIdentitySnapshot? snapshot) : IDeviceIdentityQueryService
    {
        public Task<DeviceIdentitySnapshot?> GetByDeviceIdAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot?.DeviceId == deviceId ? snapshot : null);
        }

        public Task<bool> ExistsAsync(Guid deviceId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot?.DeviceId == deviceId);
        }
    }

    private sealed class StubEdgeHostPlcRuntimeStateStore : IEdgeHostPlcRuntimeStateStore
    {
        public List<EdgeHostPlcRuntimeState> States { get; } = [];

        public bool SaveChangesCalled { get; private set; }

        public Task<IReadOnlyList<EdgeHostPlcRuntimeState>> GetByIdentityAsync(
            Guid deviceId,
            string clientCode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<EdgeHostPlcRuntimeState>)States
                .Where(state => state.DeviceId == deviceId
                    && string.Equals(state.ClientCode, clientCode.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
                .ToList());
        }

        public Task<IReadOnlyList<EdgeHostPlcRuntimeState>> GetByEdgeHostAsync(
            Guid edgeHostId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<EdgeHostPlcRuntimeState>)States
                .Where(state => state.EdgeHostId == edgeHostId)
                .ToList());
        }

        public void Add(EdgeHostPlcRuntimeState state)
        {
            States.Add(state);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }
    }

    private sealed class StubCurrentUserDeviceAccessService : ICurrentUserDeviceAccessService
    {
        public bool IsAdministrator { get; init; }

        public IReadOnlyList<Guid>? AccessibleDeviceIds { get; init; }

        public Task<Result<IReadOnlyList<Guid>?>> GetAccessibleDeviceIdsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(AccessibleDeviceIds));
        }

        public Task<Result> EnsureCanAccessDeviceAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            if (AccessibleDeviceIds is null || AccessibleDeviceIds.Contains(deviceId))
            {
                return Task.FromResult(Result.Success());
            }

            return Task.FromResult(Result.Failure("无权查看该设备"));
        }
    }

    private sealed class StubCapacityQueryService : ICapacityQueryService
    {
        public DailySummaryDto? Summary { get; init; }

        public List<(Guid DeviceId, DateOnly Date, string? PlcName)> SummaryCalls { get; } = [];

        public Task<List<HourlyCapacityDto>> GetHourlyByDeviceIdAsync(
            Guid deviceId,
            DateOnly date,
            string? plcName = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<HourlyCapacityDto>());
        }

        public Task<List<HourlyCapacityPointDto>> GetHourlyRangeByDeviceIdAsync(
            Guid deviceId,
            DateTime startTime,
            DateTime endTime,
            string? plcName = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<HourlyCapacityPointDto>());
        }

        public Task<List<HourlyCapacityAggregateDto>> GetHourlyAggregateAsync(
            DateOnly date,
            Guid? processId = null,
            IReadOnlyCollection<Guid>? deviceIds = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<HourlyCapacityAggregateDto>());
        }

        public Task<DailySummaryDto?> GetSummaryByDeviceIdAsync(
            Guid deviceId,
            DateOnly date,
            string? plcName = null,
            CancellationToken cancellationToken = default)
        {
            SummaryCalls.Add((deviceId, date, plcName));
            return Task.FromResult(Summary);
        }

        public Task<List<DailyRangeSummaryDto>> GetSummaryRangeAsync(
            Guid deviceId,
            DateOnly startDate,
            DateOnly endDate,
            string? plcName = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new List<DailyRangeSummaryDto>());
        }

        public Task<(List<DailyCapacityPagedItemDto> Items, int TotalCount)> GetDailyPagedAsync(
            Pagination pagination,
            DateOnly? date = null,
            Guid? deviceId = null,
            IReadOnlyCollection<Guid>? deviceIds = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((new List<DailyCapacityPagedItemDto>(), 0));
        }
    }
}
