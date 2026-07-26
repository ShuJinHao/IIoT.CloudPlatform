using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.ProductionService.Queries.DeviceClientOverviews;
using IIoT.ProductionService.Queries.EdgeHosts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;
using Xunit;

namespace IIoT.CloudPlatform.ApplicationTests;

public sealed class DeviceClientOverviewBehaviorTests
{
    [Fact]
    public void OverviewAndDetailQueries_ShouldUseIndependentNarrowPermissions()
    {
        AssertPermission<GetDeviceClientOverviewQuery>(DeviceClientOverviewPermissions.Read);
        AssertPermission<GetDeviceClientReleaseDetailQuery>(ClientReleasePermissions.Read);
        AssertPermission<GetEdgeHostPlcRuntimeStatesQuery>(EdgeHostPermissions.Read);
    }

    [Fact]
    public async Task Overview_ShouldApplyDeviceScopeAndLoadStateForCurrentPageOnly()
    {
        var first = NewOverviewRow("A 设备", "DEV-OVERVIEW-A");
        var second = NewOverviewRow("B 设备", "DEV-OVERVIEW-B");
        var denied = NewOverviewRow("无权设备", "DEV-OVERVIEW-X");
        var overviewQueryService = new StubDeviceClientOverviewQueryService();
        overviewQueryService.Devices.AddRange([first, second, denied]);
        var stateQueryService = new StubDeviceClientStateQueryService();
        stateQueryService.States.Add(CreateRunningState(first, "10.10.0.1", "1.0.0"));
        stateQueryService.States.Add(CreateRunningState(second, "10.10.0.2", "2.0.0"));
        stateQueryService.States.Add(CreateRunningState(denied, "10.10.0.99", "9.9.9"));
        var handler = new GetDeviceClientOverviewHandler(
            new StubCurrentUserDeviceAccessService
            {
                AccessibleDeviceIds = [first.DeviceId, second.DeviceId]
            },
            overviewQueryService,
            stateQueryService);

        var result = await handler.Handle(
            new GetDeviceClientOverviewQuery(
                new Pagination { PageNumber = 2, PageSize = 1 },
                SortBy: "deviceName",
                SortDirection: "asc"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        var item = Assert.Single(result.Value!);
        Assert.Equal(second.DeviceId, item.DeviceId);
        Assert.Equal("B 设备", item.DeviceName);
        Assert.Equal("10.10.0.2", item.PrimaryIpAddress);
        Assert.Equal("Running", item.SoftwareStatus);
        Assert.Equal("2.0.0", item.CurrentVersion);
        Assert.Null(item.Issue);
        Assert.Equal([first.DeviceId, second.DeviceId], overviewQueryService.LastRequest!.AllowedDeviceIds);
        Assert.Equal(1, overviewQueryService.LastRequest.Skip);
        Assert.Equal(1, overviewQueryService.LastRequest.Take);
        Assert.Equal([second.DeviceId], stateQueryService.LastRequestedDeviceIds);
    }

    [Fact]
    public async Task Overview_ShouldNotTreatVersionReportAsRuntimeHeartbeat()
    {
        var device = NewOverviewRow("已上报版本设备", "DEV-OVERVIEW-VERSION");
        var snapshot = new DeviceClientVersionSnapshot(
            device.DeviceId,
            device.ClientCode,
            "3.2.1",
            "1.0.0",
            "stable",
            DateTime.UtcNow,
            [],
            ["10.20.0.8"]);
        var state = new DeviceClientState(device.DeviceId, device.ClientCode);
        state.ApplyVersionReport(snapshot);
        var overviewQueryService = new StubDeviceClientOverviewQueryService();
        overviewQueryService.Devices.Add(device);
        var stateQueryService = new StubDeviceClientStateQueryService();
        stateQueryService.States.Add(state);
        var handler = new GetDeviceClientOverviewHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [device.DeviceId] },
            overviewQueryService,
            stateQueryService);

        var result = await handler.Handle(
            new GetDeviceClientOverviewQuery(new Pagination()),
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        var item = Assert.Single(result.Value!);
        Assert.Equal("MissingRuntimeHeartbeat", item.SoftwareStatus);
        Assert.Equal("客户端尚未上报运行心跳。", item.Issue);
        Assert.Equal("3.2.1", item.CurrentVersion);
        Assert.Equal("10.20.0.8", item.PrimaryIpAddress);
    }

    [Theory]
    [InlineData("unsupported", "asc")]
    [InlineData("deviceName", "sideways")]
    public async Task Overview_ShouldRejectUnsupportedDatabaseSort(
        string sortBy,
        string sortDirection)
    {
        var overviewQueryService = new StubDeviceClientOverviewQueryService();
        var handler = new GetDeviceClientOverviewHandler(
            new StubCurrentUserDeviceAccessService(),
            overviewQueryService,
            new StubDeviceClientStateQueryService());

        var result = await handler.Handle(
            new GetDeviceClientOverviewQuery(new Pagination(), SortBy: sortBy, SortDirection: sortDirection),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Null(overviewQueryService.LastRequest);
    }

    [Fact]
    public async Task ReleaseDetail_ShouldEnforceDeviceScopeAndUseOfficialStateProjection()
    {
        var processId = Guid.NewGuid();
        var device = new Device("版本详情设备", "DEV-OVERVIEW-DETAIL", processId);
        var snapshot = new DeviceClientVersionSnapshot(
            device.Id,
            device.Code,
            "4.0.0",
            "1.0.0",
            "stable",
            DateTime.UtcNow,
            [],
            ["10.30.0.4"]);
        var state = new DeviceClientState(device.Id, device.Code);
        state.ApplyVersionReport(snapshot);
        state.ApplyRuntimeHeartbeat(new EdgeDeviceRuntimeHeartbeat(
            device.Id,
            device.Code,
            "runtime-detail",
            "cutting",
            "4.0.0",
            "1.0.0",
            "Running",
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow,
            ["10.30.0.4"]));
        var deviceRepository = new InMemoryRepository<Device> { SingleOrDefaultResult = device };
        var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
        var stateQueryService = new StubDeviceClientStateQueryService();
        stateQueryService.States.Add(state);
        stateQueryService.Snapshots.Add(snapshot);
        var access = new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [device.Id] };
        var handler = new GetDeviceClientReleaseDetailHandler(
            access,
            deviceRepository,
            stateQueryService,
            componentRepository);

        var result = await handler.Handle(
            new GetDeviceClientReleaseDetailQuery(device.Id, "stable", "win-x64"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        Assert.Equal(device.Id, result.Value!.DeviceId);
        Assert.Equal("Running", result.Value.SoftwareStatus);
        Assert.Equal("宿主 4.0.0", result.Value.CurrentVersion);
        Assert.Equal("4.0.0", result.Value.RuntimeHostVersion);
        Assert.Equal("1.0.0", result.Value.RuntimeHostApiVersion);
        Assert.Equal("4.0.0", result.Value.ReportedHostVersion);
        Assert.Equal("1.0.0", result.Value.ReportedHostApiVersion);
        Assert.Null(result.Value.LatestPublishedHostVersion);
        Assert.Equal((device.Id, device.Code), stateQueryService.LastRequestedIdentity);
        Assert.Equal(device.Id, access.LastEnsuredDeviceId);
    }

    [Fact]
    public async Task ReleaseDetail_ShouldSeparateRuntimeReportedAndLatestPublishedFacts()
    {
        var device = new Device("版本事实设备", "DEV-VERSION-FACTS", Guid.NewGuid());
        var hostPublishedAt = new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc);
        var pluginPublishedAt = hostPublishedAt.AddHours(1);
        var hostHash = new string('a', 64);
        var pluginHash = new string('b', 64);
        var snapshot = new DeviceClientVersionSnapshot(
            device.Id,
            device.Code,
            "2.0.5",
            "2.0.0",
            "stable",
            hostPublishedAt.AddDays(1),
            [
                new DeviceClientPluginVersion(
                    "CP",
                    "正极模切",
                    "2.0.5",
                    "2.0.0",
                    enabled: true)
            ]);
        var state = new DeviceClientState(device.Id, device.Code);
        state.ApplyVersionReport(snapshot);
        state.ApplyRuntimeHeartbeat(new EdgeDeviceRuntimeHeartbeat(
            device.Id,
            device.Code,
            "runtime-version-facts",
            "cutting",
            "2.0.6",
            "2.0.1",
            "Running",
            hostPublishedAt,
            hostPublishedAt.AddDays(1),
            []));

        var host = ClientReleaseComponent.CreateHost("stable", "win-x64");
        host.UpsertHostVersion(
            "2.0.9",
            "2.0.0",
            "net10.0",
            "/edge-updates/host/2.0.9.zip",
            new string('c', 64),
            1024,
            null,
            ClientReleaseStatus.Published,
            null,
            "IIoT",
            hostPublishedAt.AddDays(-1));
        host.UpsertHostVersion(
            "2.0.10",
            "2.0.0",
            "net10.0",
            "/edge-updates/host/2.0.10.zip",
            hostHash,
            2048,
            null,
            ClientReleaseStatus.Published,
            null,
            "IIoT",
            hostPublishedAt);
        host.UpsertHostVersion(
            "9.0.0",
            "9.0.0",
            "net10.0",
            "/edge-updates/host/9.0.0.zip",
            new string('d', 64),
            4096,
            null,
            ClientReleaseStatus.Draft,
            null,
            "IIoT");

        var otherChannelHost = ClientReleaseComponent.CreateHost("beta", "win-x64");
        otherChannelHost.UpsertHostVersion(
            "99.0.0",
            "99.0.0",
            "net10.0",
            "/edge-updates/host/99.0.0.zip",
            new string('e', 64),
            4096,
            null,
            ClientReleaseStatus.Published,
            null,
            "IIoT",
            hostPublishedAt.AddDays(1));

        var plugin = ClientReleaseComponent.CreatePlugin(
            "CP",
            "正极模切",
            null,
            null,
            null,
            "stable",
            "win-x64");
        plugin.UpsertPluginVersion(
            "2.0.10",
            "2.0.0",
            "2.0.0",
            "3.0.0",
            "net10.0",
            "/edge-updates/plugins/stable/CP/2.0.10/CP.zip",
            pluginHash,
            1024,
            null,
            "[]",
            ClientReleaseStatus.Published,
            null,
            "IIoT",
            pluginPublishedAt);

        var deviceRepository = new InMemoryRepository<Device> { SingleOrDefaultResult = device };
        var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
        componentRepository.ListResult.AddRange([host, otherChannelHost, plugin]);
        var stateQueryService = new StubDeviceClientStateQueryService();
        stateQueryService.States.Add(state);
        stateQueryService.Snapshots.Add(snapshot);
        var handler = new GetDeviceClientReleaseDetailHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [device.Id] },
            deviceRepository,
            stateQueryService,
            componentRepository);

        var result = await handler.Handle(
            new GetDeviceClientReleaseDetailQuery(device.Id, "stable", "win-x64"),
            CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
        var details = result.Value!;
        Assert.Equal("2.0.6", details.RuntimeHostVersion);
        Assert.Equal("2.0.1", details.RuntimeHostApiVersion);
        Assert.Equal("2.0.5", details.ReportedHostVersion);
        Assert.Equal("2.0.0", details.ReportedHostApiVersion);
        Assert.Equal("2.0.10", details.LatestPublishedHostVersion);
        Assert.Equal("2.0.0", details.LatestPublishedHostApiVersion);
        Assert.Equal(hostPublishedAt, details.LatestPublishedAtUtc);
        Assert.Equal(hostHash, details.LatestPublishedHostPackageSha256);
        Assert.Equal("2.0.5", details.HostVersion);
        Assert.Equal("宿主 2.0.5 / 插件 1 个", details.CurrentVersion);

        var pluginDetails = Assert.Single(details.Plugins);
        Assert.Equal("2.0.5", pluginDetails.Version);
        Assert.True(pluginDetails.Enabled);
        Assert.Equal("2.0.10", pluginDetails.LatestPublishedVersion);
        Assert.Equal(pluginPublishedAt, pluginDetails.LatestPublishedAtUtc);
        Assert.Equal(pluginHash, pluginDetails.LatestPublishedPackageSha256);
        Assert.Null(pluginDetails.GetType().GetProperty("Running"));
    }

    [Fact]
    public async Task ReleaseDetail_ShouldNotFallbackBetweenRuntimeAndVersionReportSources()
    {
        var reportedDevice = new Device("仅版本上报设备", "DEV-REPORTED-ONLY", Guid.NewGuid());
        var reportedSnapshot = new DeviceClientVersionSnapshot(
            reportedDevice.Id,
            reportedDevice.Code,
            "3.0.0",
            "2.0.0",
            "stable",
            DateTime.UtcNow,
            []);
        var reportedState = new DeviceClientState(reportedDevice.Id, reportedDevice.Code);
        reportedState.ApplyVersionReport(reportedSnapshot);
        var reportedStateQuery = new StubDeviceClientStateQueryService();
        reportedStateQuery.States.Add(reportedState);
        reportedStateQuery.Snapshots.Add(reportedSnapshot);
        var reportedHandler = new GetDeviceClientReleaseDetailHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [reportedDevice.Id] },
            new InMemoryRepository<Device> { SingleOrDefaultResult = reportedDevice },
            reportedStateQuery,
            new InMemoryRepository<ClientReleaseComponent>());

        var reportedResult = await reportedHandler.Handle(
            new GetDeviceClientReleaseDetailQuery(reportedDevice.Id),
            CancellationToken.None);

        Assert.True(reportedResult.IsSuccess, string.Join("; ", reportedResult.Errors ?? []));
        Assert.Equal("3.0.0", reportedResult.Value!.ReportedHostVersion);
        Assert.Equal("2.0.0", reportedResult.Value.ReportedHostApiVersion);
        Assert.Null(reportedResult.Value.RuntimeHostVersion);
        Assert.Null(reportedResult.Value.RuntimeHostApiVersion);

        var runtimeDevice = new Device("仅运行心跳设备", "DEV-RUNTIME-ONLY", Guid.NewGuid());
        var runtimeState = new DeviceClientState(runtimeDevice.Id, runtimeDevice.Code);
        runtimeState.ApplyRuntimeHeartbeat(new EdgeDeviceRuntimeHeartbeat(
            runtimeDevice.Id,
            runtimeDevice.Code,
            "runtime-only",
            "cutting",
            "3.1.0",
            "2.1.0",
            "Running",
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow,
            []));
        var runtimeStateQuery = new StubDeviceClientStateQueryService();
        runtimeStateQuery.States.Add(runtimeState);
        var runtimeHandler = new GetDeviceClientReleaseDetailHandler(
            new StubCurrentUserDeviceAccessService { AccessibleDeviceIds = [runtimeDevice.Id] },
            new InMemoryRepository<Device> { SingleOrDefaultResult = runtimeDevice },
            runtimeStateQuery,
            new InMemoryRepository<ClientReleaseComponent>());

        var runtimeResult = await runtimeHandler.Handle(
            new GetDeviceClientReleaseDetailQuery(runtimeDevice.Id),
            CancellationToken.None);

        Assert.True(runtimeResult.IsSuccess, string.Join("; ", runtimeResult.Errors ?? []));
        Assert.Equal("3.1.0", runtimeResult.Value!.RuntimeHostVersion);
        Assert.Equal("2.1.0", runtimeResult.Value.RuntimeHostApiVersion);
        Assert.Null(runtimeResult.Value.ReportedHostVersion);
        Assert.Null(runtimeResult.Value.ReportedHostApiVersion);
    }

    private static void AssertPermission<TRequest>(string expectedPermission)
    {
        var attribute = typeof(TRequest)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
            .Cast<AuthorizeRequirementAttribute>()
            .Single();
        Assert.Equal(expectedPermission, attribute.Permission);
        Assert.Empty(typeof(TRequest).GetCustomAttributes(typeof(AdminOnlyAttribute), inherit: false));
    }

    private static DeviceClientOverviewDeviceRow NewOverviewRow(string name, string clientCode)
        => new(Guid.NewGuid(), name, clientCode);

    private static DeviceClientState CreateRunningState(
        DeviceClientOverviewDeviceRow device,
        string ipAddress,
        string version)
    {
        var state = new DeviceClientState(device.DeviceId, device.ClientCode);
        state.ApplyRuntimeHeartbeat(new EdgeDeviceRuntimeHeartbeat(
            device.DeviceId,
            device.ClientCode,
            $"runtime-{device.DeviceId:N}",
            "cutting",
            version,
            "1.0.0",
            "Running",
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow,
            [ipAddress]));
        return state;
    }

    private sealed class StubDeviceClientOverviewQueryService : IDeviceClientOverviewQueryService
    {
        public List<DeviceClientOverviewDeviceRow> Devices { get; } = [];

        public DeviceClientOverviewQueryRequest? LastRequest { get; private set; }

        public Task<DeviceClientOverviewPage> SearchAsync(
            DeviceClientOverviewQueryRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var allowed = request.AllowedDeviceIds;
            var matching = Devices
                .Where(device => allowed == null || allowed.Contains(device.DeviceId))
                .OrderBy(device => device.DeviceName, StringComparer.Ordinal)
                .ThenBy(device => device.ClientCode, StringComparer.Ordinal)
                .ToList();
            var page = matching.Skip(request.Skip).Take(request.Take).ToList();
            return Task.FromResult(new DeviceClientOverviewPage(page, matching.Count));
        }
    }

    private sealed class StubDeviceClientStateQueryService : IDeviceClientStateQueryService
    {
        public List<DeviceClientState> States { get; } = [];

        public List<DeviceClientVersionSnapshot> Snapshots { get; } = [];

        public IReadOnlyCollection<Guid>? LastRequestedDeviceIds { get; private set; }

        public (Guid DeviceId, string ClientCode)? LastRequestedIdentity { get; private set; }

        public Task<DeviceClientVersionSnapshot?> GetVersionSnapshotByDeviceAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Snapshots.SingleOrDefault(snapshot => snapshot.DeviceId == deviceId));

        public Task<IReadOnlyList<DeviceClientVersionSnapshot>> GetVersionSnapshotsByDevicesAsync(
            IReadOnlyCollection<Guid>? deviceIds = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeviceClientVersionSnapshot>>(Snapshots
                .Where(snapshot => deviceIds == null || deviceIds.Contains(snapshot.DeviceId))
                .ToList());

        public Task<EdgeDeviceRuntimeHeartbeat?> GetRuntimeHeartbeatByIdentityAsync(
            Guid deviceId,
            string clientCode,
            CancellationToken cancellationToken = default)
            => Task.FromResult<EdgeDeviceRuntimeHeartbeat?>(null);

        public Task<DeviceClientState?> GetStateByIdentityAsync(
            Guid deviceId,
            string clientCode,
            CancellationToken cancellationToken = default)
        {
            LastRequestedIdentity = (deviceId, clientCode);
            return Task.FromResult(States.SingleOrDefault(state =>
                state.DeviceId == deviceId
                && string.Equals(state.ClientCode, clientCode, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<IReadOnlyList<DeviceClientState>> GetStatesByDevicesAsync(
            IReadOnlyCollection<Guid>? deviceIds = null,
            CancellationToken cancellationToken = default)
        {
            LastRequestedDeviceIds = deviceIds;
            return Task.FromResult<IReadOnlyList<DeviceClientState>>(States
                .Where(state => deviceIds == null || deviceIds.Contains(state.DeviceId))
                .ToList());
        }
    }

    private sealed class StubCurrentUserDeviceAccessService : ICurrentUserDeviceAccessService
    {
        public bool IsAdministrator { get; init; }

        public IReadOnlyList<Guid>? AccessibleDeviceIds { get; init; } = [];

        public Guid? LastEnsuredDeviceId { get; private set; }

        public Task<Result<IReadOnlyList<Guid>?>> GetAccessibleDeviceIdsAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success(AccessibleDeviceIds));

        public Task<Result> EnsureCanAccessDeviceAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            LastEnsuredDeviceId = deviceId;
            return Task.FromResult(
                AccessibleDeviceIds is null || AccessibleDeviceIds.Contains(deviceId)
                    ? Result.Success()
                    : Result.Failure("越权: 未授权访问该设备"));
        }
    }
}
