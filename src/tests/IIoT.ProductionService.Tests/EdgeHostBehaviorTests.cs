using System.Linq.Expressions;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.EdgeHosts;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Contracts.EdgeHosts;
using IIoT.ProductionService.Commands.EdgeHosts;
using IIoT.ProductionService.EdgeHosts;
using IIoT.ProductionService.Queries.EdgeHosts;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
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
    public void HumanEdgeHostRequests_ShouldBeReadOnly()
    {
        var requestTypes = new[]
        {
            typeof(GetEdgeHostPagedListQuery),
            typeof(GetEdgeHostDetailQuery),
            typeof(GetEdgeHostPlcRuntimeStatesQuery)
        };

        foreach (var requestType in requestTypes)
        {
            var permission = requestType
                .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
                .Cast<AuthorizeRequirementAttribute>()
                .Single();

            Assert.Equal(EdgeHostPermissions.Read, permission.Permission);
            Assert.Empty(requestType.GetCustomAttributes(typeof(AdminOnlyAttribute), inherit: false));
        }
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
    public async Task ReportEdgeHostPlcRuntimeStatesHandler_ShouldUpsertClientReportedStatesWithoutEdgeHost()
    {
        var deviceId = Guid.NewGuid();
        var store = new StubEdgeHostPlcRuntimeStateStore();
        var handler = new ReportEdgeHostPlcRuntimeStatesHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-PLCSTATE01")),
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
        Assert.Equal(deviceId, result.Value!.DeviceId);
        Assert.Equal("DEV-PLCSTATE01", result.Value.ClientCode);
        Assert.Equal(2, result.Value.ReceivedCount);
        Assert.True(store.SaveChangesCalled);

        var connected = Assert.Single(store.States, state => state.PlcCode == "PLC-CUT-01");
        Assert.True(connected.IsConnected);
        Assert.Equal(EdgeHostPlcRuntimeStatus.Connected, connected.RuntimeStatus);
        Assert.Equal("ModbusTcp", connected.Protocol);

        var faulted = Assert.Single(store.States, state => state.PlcCode == "PLC-NEW-01");
        Assert.False(faulted.IsConnected);
        Assert.Equal(EdgeHostPlcRuntimeStatus.Faulted, faulted.RuntimeStatus);
        Assert.Equal("连接超时", faulted.LastError);
    }

    [Fact]
    public async Task ReportEdgeHostPlcRuntimeStatesHandler_ShouldRejectMismatchedIdentity()
    {
        var deviceId = Guid.NewGuid();
        var store = new StubEdgeHostPlcRuntimeStateStore();
        var handler = new ReportEdgeHostPlcRuntimeStatesHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-PLCSTATE02")),
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
        var store = new StubEdgeHostPlcRuntimeStateStore();
        var handler = new ReportEdgeHostPlcRuntimeStatesHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-PLCSTATE03")),
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
    public async Task ReportEdgeHostPlcRuntimeStatesHandler_ShouldRemoveStatesMissingFromFullSnapshot()
    {
        var deviceId = Guid.NewGuid();
        var store = new StubEdgeHostPlcRuntimeStateStore();
        store.States.Add(new EdgeHostPlcRuntimeState(deviceId, "DEV-PLCSTATE05", "PLC-KEEP"));
        store.States.Add(new EdgeHostPlcRuntimeState(deviceId, "DEV-PLCSTATE05", "PLC-REMOVED"));
        var handler = new ReportEdgeHostPlcRuntimeStatesHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-PLCSTATE05")),
            store);

        var result = await handler.Handle(
            new ReportEdgeHostPlcRuntimeStatesCommand(
                deviceId,
                "DEV-PLCSTATE05",
                DateTime.UtcNow,
                [new EdgeHostPlcRuntimeStateReportItem("PLC-KEEP", "保留 PLC", true, "Connected")]),
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        var state = Assert.Single(store.States);
        Assert.Equal("PLC-KEEP", state.PlcCode);
        Assert.Equal("保留 PLC", state.ReportedPlcName);
        Assert.True(store.SaveChangesCalled);
    }

    [Fact]
    public async Task ReportEdgeHostPlcRuntimeStatesHandler_ShouldClearStatesForEmptyFullSnapshot()
    {
        var deviceId = Guid.NewGuid();
        var store = new StubEdgeHostPlcRuntimeStateStore();
        store.States.Add(new EdgeHostPlcRuntimeState(deviceId, "DEV-PLCSTATE06", "PLC-REMOVED"));
        var handler = new ReportEdgeHostPlcRuntimeStatesHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-PLCSTATE06")),
            store);

        var result = await handler.Handle(
            new ReportEdgeHostPlcRuntimeStatesCommand(
                deviceId,
                "DEV-PLCSTATE06",
                DateTime.UtcNow,
                []),
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        Assert.Equal(0, result.Value!.ReceivedCount);
        Assert.Empty(store.States);
        Assert.True(store.SaveChangesCalled);
    }

    [Fact]
    public async Task GetEdgeHostPagedListHandler_ShouldUseAccessibleDevicesAsHostList()
    {
        var processId = Guid.NewGuid();
        var device = new Device("开发测试模切设备", "DEV-HOST01", processId);
        var deniedDevice = new Device("无权设备", "DEV-HOST02", processId);
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.ListResult.AddRange([device, deniedDevice]);
        var clientStateStore = new StubDeviceClientStateStore();
        var clientState = new DeviceClientState(device.Id, device.Code);
        clientState.ApplyRuntimeHeartbeat(new EdgeDeviceRuntimeHeartbeat(
            device.Id,
            device.Code,
            "runtime-01",
            "cutting",
            "1.0.25",
            "host-api-1",
            "Running",
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow,
            ["10.0.0.10"]));
        clientStateStore.States.Add(clientState);
        var runtimeStore = new StubEdgeHostPlcRuntimeStateStore();
        var plcState = new EdgeHostPlcRuntimeState(device.Id, device.Code, "PLC-CUT-01");
        plcState.ReplaceReport("现场 PLC", true, "Connected", DateTime.UtcNow, protocol: "ModbusTcp");
        runtimeStore.States.Add(plcState);
        var overviewQueryService = new StubEdgeHostOverviewQueryService();
        overviewQueryService.Devices.AddRange([
            new EdgeHostOverviewDeviceRow(device.Id, device.DeviceName, device.Code),
            new EdgeHostOverviewDeviceRow(deniedDevice.Id, deniedDevice.DeviceName, deniedDevice.Code)
        ]);
        overviewQueryService.PlcStates.Add(plcState);
        var handler = new GetEdgeHostPagedListHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [device.Id] },
            overviewQueryService,
            clientStateStore,
            runtimeStore);

        var result = await handler.Handle(
            new GetEdgeHostPagedListQuery(new Pagination { PageNumber = 1, PageSize = 10 }, "PLC-CUT"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        var item = Assert.Single(result.Value!);
        Assert.Equal(device.Id, item.Id);
        Assert.Equal(device.Code, item.ClientCode);
        Assert.Equal("开发测试模切设备", item.HostName);
        Assert.Equal("Running", item.SoftwareStatus);
        Assert.Equal(1, item.PlcCount);
        Assert.Equal(1, item.ConnectedPlcCount);
        Assert.Equal("PLC-CUT", overviewQueryService.LastKeyword);
        Assert.Equal([device.Id], clientStateStore.LastRequestedDeviceIds);
        Assert.Equal([device.Id], runtimeStore.LastRequestedDeviceIds);
    }

    [Fact]
    public async Task GetEdgeHostPagedListHandler_ShouldLoadClientAndPlcStatesForCurrentPageOnly()
    {
        var processId = Guid.NewGuid();
        var firstDevice = new Device("A 设备", "DEV-PAGE01", processId);
        var secondDevice = new Device("B 设备", "DEV-PAGE02", processId);
        var overviewQueryService = new StubEdgeHostOverviewQueryService();
        overviewQueryService.Devices.AddRange([
            new EdgeHostOverviewDeviceRow(firstDevice.Id, firstDevice.DeviceName, firstDevice.Code),
            new EdgeHostOverviewDeviceRow(secondDevice.Id, secondDevice.DeviceName, secondDevice.Code)
        ]);
        var clientStateStore = new StubDeviceClientStateStore();
        clientStateStore.States.Add(new DeviceClientState(firstDevice.Id, firstDevice.Code));
        clientStateStore.States.Add(new DeviceClientState(secondDevice.Id, secondDevice.Code));
        var runtimeStore = new StubEdgeHostPlcRuntimeStateStore();
        runtimeStore.States.Add(new EdgeHostPlcRuntimeState(firstDevice.Id, firstDevice.Code, "PLC-A"));
        runtimeStore.States.Add(new EdgeHostPlcRuntimeState(secondDevice.Id, secondDevice.Code, "PLC-B"));
        var handler = new GetEdgeHostPagedListHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [firstDevice.Id, secondDevice.Id] },
            overviewQueryService,
            clientStateStore,
            runtimeStore);

        var result = await handler.Handle(
            new GetEdgeHostPagedListQuery(new Pagination { PageNumber = 2, PageSize = 1 }),
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        var item = Assert.Single(result.Value!);
        Assert.Equal(secondDevice.Id, item.DeviceId);
        Assert.Equal(2, result.Value!.MetaData.TotalCount);
        Assert.Equal([secondDevice.Id], clientStateStore.LastRequestedDeviceIds);
        Assert.Equal([secondDevice.Id], runtimeStore.LastRequestedDeviceIds);
    }

    [Fact]
    public void EdgeHostPlcRuntimeState_ShouldInferFaultedWhenDisconnectedReportHasLastError()
    {
        var state = new EdgeHostPlcRuntimeState(Guid.NewGuid(), "DEV-PLCSTATE04", "PLC-CUT-01");

        state.ReplaceReport(
            "现场 PLC",
            isConnected: false,
            runtimeStatus: null,
            observedAtUtc: DateTime.UtcNow,
            lastError: "连接超时");

        Assert.False(state.IsConnected);
        Assert.Equal(EdgeHostPlcRuntimeStatus.Faulted, state.RuntimeStatus);
        Assert.Equal("连接超时", state.LastError);
    }

    [Fact]
    public async Task GetEdgeHostPlcRuntimeStatesHandler_ShouldRejectInaccessibleDevice()
    {
        var device = new Device("无权设备", "DEV-DENIED", Guid.NewGuid());
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.ListResult.Add(device);
        var handler = new GetEdgeHostPlcRuntimeStatesHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [] },
            deviceRepository,
            new StubEdgeHostPlcRuntimeStateStore());

        var result = await handler.Handle(new GetEdgeHostPlcRuntimeStatesQuery(device.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors ?? [], error => error.Contains("无权", StringComparison.Ordinal));
    }

    private sealed class InMemoryRepository<T> : IRepository<T>
        where T : class, IEntity, IAggregateRoot
    {
        public List<T> ListResult { get; } = [];

        public T Add(T entity)
        {
            ListResult.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
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
            return Task.FromResult(ApplySpecification(specification).SingleOrDefault());
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

    private sealed class StubDeviceClientStateStore : IDeviceClientStateStore
    {
        public List<DeviceClientState> States { get; } = [];

        public IReadOnlyList<Guid>? LastRequestedDeviceIds { get; private set; }

        public Task<DeviceClientVersionSnapshot?> GetVersionSnapshotByDeviceAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DeviceClientVersionSnapshot?>(null);
        }

        public Task<IReadOnlyList<DeviceClientVersionSnapshot>> GetVersionSnapshotsByDevicesAsync(
            IReadOnlyCollection<Guid>? deviceIds = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<DeviceClientVersionSnapshot>)[]);
        }

        public Task<EdgeDeviceRuntimeHeartbeat?> GetRuntimeHeartbeatByIdentityAsync(
            Guid deviceId,
            string clientCode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<EdgeDeviceRuntimeHeartbeat?>(null);
        }

        public Task<DeviceClientState?> GetStateByIdentityAsync(
            Guid deviceId,
            string clientCode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(States.SingleOrDefault(state =>
                state.DeviceId == deviceId
                && string.Equals(state.ClientCode, clientCode.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)));
        }

        public Task<IReadOnlyList<DeviceClientState>> GetStatesByDevicesAsync(
            IReadOnlyCollection<Guid>? deviceIds = null,
            CancellationToken cancellationToken = default)
        {
            LastRequestedDeviceIds = deviceIds?.ToList();
            return Task.FromResult((IReadOnlyList<DeviceClientState>)States
                .Where(state => deviceIds == null || deviceIds.Contains(state.DeviceId))
                .ToList());
        }

        public void AddVersionSnapshot(DeviceClientVersionSnapshot snapshot)
        {
        }

        public void AddRuntimeHeartbeat(EdgeDeviceRuntimeHeartbeat heartbeat)
        {
        }

        public void AddState(DeviceClientState state)
        {
            States.Add(state);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }
    }

    private sealed class StubEdgeHostPlcRuntimeStateStore : IEdgeHostPlcRuntimeStateStore
    {
        public List<EdgeHostPlcRuntimeState> States { get; } = [];

        public bool SaveChangesCalled { get; private set; }

        public IReadOnlyList<Guid>? LastRequestedDeviceIds { get; private set; }

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

        public Task<IReadOnlyList<EdgeHostPlcRuntimeState>> GetByDevicesAsync(
            IReadOnlyCollection<Guid>? deviceIds = null,
            CancellationToken cancellationToken = default)
        {
            LastRequestedDeviceIds = deviceIds?.ToList();
            return Task.FromResult((IReadOnlyList<EdgeHostPlcRuntimeState>)States
                .Where(state => deviceIds == null || deviceIds.Contains(state.DeviceId))
                .ToList());
        }

        public void Add(EdgeHostPlcRuntimeState state)
        {
            States.Add(state);
        }

        public void Delete(EdgeHostPlcRuntimeState state)
        {
            States.Remove(state);
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

    private sealed class StubEdgeHostOverviewQueryService : IEdgeHostOverviewQueryService
    {
        public List<EdgeHostOverviewDeviceRow> Devices { get; } = [];

        public List<EdgeHostPlcRuntimeState> PlcStates { get; } = [];

        public string? LastKeyword { get; private set; }

        public Task<EdgeHostOverviewPage> SearchAccessibleDevicesAsync(
            IReadOnlyCollection<Guid>? allowedDeviceIds,
            string? keyword,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            LastKeyword = keyword;
            var normalized = keyword?.Trim();
            var matches = Devices
                .Where(device => allowedDeviceIds == null || allowedDeviceIds.Contains(device.DeviceId))
                .Where(device => string.IsNullOrWhiteSpace(normalized)
                    || Contains(device.DeviceName, normalized)
                    || Contains(device.ClientCode, normalized)
                    || PlcStates.Any(state =>
                        state.DeviceId == device.DeviceId
                        && (Contains(state.PlcCode, normalized)
                            || Contains(state.ReportedPlcName, normalized)
                            || Contains(state.Protocol, normalized)
                            || Contains(state.Address, normalized))))
                .OrderBy(device => device.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(device => device.ClientCode, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var page = matches.Skip(skip).Take(take).ToList();

            return Task.FromResult(new EdgeHostOverviewPage(page, matches.Count));
        }

        private static bool Contains(string? value, string? keyword)
        {
            return !string.IsNullOrWhiteSpace(keyword)
                && value?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}
