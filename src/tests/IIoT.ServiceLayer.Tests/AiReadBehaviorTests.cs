using System.Security.Claims;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.ProductionService.AiRead;
using IIoT.ProductionService.PassStations;
using IIoT.ProductionService.Queries.AiRead;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.AiRead;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.Exceptions;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.ServiceLayer.Tests;

public sealed class AiReadBehaviorTests
{
    [Fact]
    public async Task RequestKindGuard_ShouldAllowAiReadRequestWithAiReadAuthorization()
    {
        var behavior = new RequestKindGuardBehavior<ValidAiReadQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var result = await behavior.Handle(
            new ValidAiReadQuery(),
            _ => Task.FromResult(Result.Success(true)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RequestKindGuard_ShouldRejectAiReadRequestWithHumanAuthorization()
    {
        var behavior = new RequestKindGuardBehavior<AiReadWithHumanAuthorizationQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new AiReadWithHumanAuthorizationQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains(nameof(AuthorizeRequirementAttribute), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestKindGuard_ShouldRejectHumanRequestWithAiReadAuthorization()
    {
        var behavior = new RequestKindGuardBehavior<HumanWithAiReadAuthorizationQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new HumanWithAiReadAuthorizationQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains(nameof(AuthorizeAiReadAttribute), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestKindGuard_ShouldRejectAiReadRequestWithAdminOnly()
    {
        var behavior = new RequestKindGuardBehavior<AiReadWithAdminOnlyQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new AiReadWithAdminOnlyQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains(nameof(AdminOnlyAttribute), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestKindGuard_ShouldRejectAiReadRequestWithoutAiReadAuthorization()
    {
        var behavior = new RequestKindGuardBehavior<UnprotectedAiReadQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new UnprotectedAiReadQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains(nameof(AuthorizeAiReadAttribute), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestKindGuard_ShouldAllowPublicRequestWithoutAuthorization()
    {
        var behavior = new RequestKindGuardBehavior<PublicDownloadQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var result = await behavior.Handle(
            new PublicDownloadQuery(),
            _ => Task.FromResult(Result.Success(true)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RequestKindGuard_ShouldRejectPublicRequestWithHumanAuthorization()
    {
        var behavior = new RequestKindGuardBehavior<PublicWithHumanAuthorizationQuery, Result<bool>>(
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new PublicWithHumanAuthorizationQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));

        Assert.Contains(nameof(AuthorizeRequirementAttribute), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiReadAuthorization_ShouldAllowAiServiceAccountWithRequiredPermission()
    {
        var behavior = new AiReadAuthorizationBehavior<ValidAiReadQuery, Result<bool>>(
            CreateAccessor(IIoTClaimTypes.AiServiceActor, [AiReadPermissions.Device]));

        var result = await behavior.Handle(
            new ValidAiReadQuery(),
            _ => Task.FromResult(Result.Success(true)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AiReadAuthorization_ShouldRejectHumanActorEvenWithAiReadPermission()
    {
        var behavior = new AiReadAuthorizationBehavior<ValidAiReadQuery, Result<bool>>(
            CreateAccessor(IIoTClaimTypes.HumanActor, [AiReadPermissions.Device]));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                new ValidAiReadQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));
    }

    [Fact]
    public async Task AiReadAuthorization_ShouldRejectMissingAiReadPermission()
    {
        var behavior = new AiReadAuthorizationBehavior<ValidAiReadQuery, Result<bool>>(
            CreateAccessor(IIoTClaimTypes.AiServiceActor, []));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            behavior.Handle(
                new ValidAiReadQuery(),
                _ => Task.FromResult(Result.Success(true)),
                CancellationToken.None));
    }

    [Fact]
    public async Task AiReadAudit_ShouldWriteMetadataWithoutPromptPayload()
    {
        var auditTrail = new RecordingAuditTrailService();
        var behavior = new AiReadAuditBehavior<AuditedAiReadQuery, Result<AiReadListResponse<int>>>(
            CreateAccessor(IIoTClaimTypes.AiServiceActor, [AiReadPermissions.Device]),
            auditTrail);

        var response = new AiReadListResponse<int>(
            [1, 2],
            DateTimeOffset.UtcNow,
            "devices",
            "deviceId=abc;keyword=present",
            2,
            Truncated: true);

        await behavior.Handle(
            new AuditedAiReadQuery(),
            _ => Task.FromResult(Result.Success(response)),
            CancellationToken.None);

        var entry = Assert.Single(auditTrail.Entries);
        Assert.Equal("AiRead.Query", entry.OperationType);
        Assert.Contains("source=devices", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("rowCount=2", entry.Summary, StringComparison.Ordinal);
        Assert.Contains("truncated=True", entry.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt", entry.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AiReadDeviceLogs_ShouldRejectMissingTimeRange()
    {
        var handler = new GetAiReadDeviceLogsHandler(
            new StubDeviceLogQueryService(),
            new TestAiReadScopeAccessor(),
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadDeviceLogsQuery(Guid.NewGuid(), null, null, Keyword: "error"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task AiReadCapacitySummary_ShouldMarkTruncatedWhenRowsExceedMaxRows()
    {
        var deviceId = Guid.NewGuid();
        var handler = new GetAiReadCapacitySummaryHandler(
            new StubCapacityQueryService
            {
                SummaryRangeResult =
                [
                    new DailyRangeSummaryDto(DateOnly.FromDateTime(DateTime.UtcNow), 10, 9, 1, 10, 9, 1, 0, 0, 0),
                    new DailyRangeSummaryDto(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), 20, 19, 1, 20, 19, 1, 0, 0, 0)
                ]
            },
            new TestAiReadScopeAccessor(),
            Options.Create(new AiReadOptions { MaxRows = 1 }));

        var result = await handler.Handle(
            new GetAiReadCapacitySummaryQuery(
                deviceId,
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Truncated);
        Assert.Equal(1, result.Value.RowCount);
    }

    [Fact]
    public async Task AiReadDevices_ShouldReturnDeviceCode()
    {
        var processId = Guid.NewGuid();
        var repository = new InMemoryRepository<Device>();
        repository.ListResult.Add(new Device("Injection Device", "DEV-AIREAD-001", processId));
        var handler = new GetAiReadDevicesHandler(
            repository,
            new TestAiReadScopeAccessor(),
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadDevicesQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("DEV-AIREAD-001", item.DeviceCode);
        Assert.Equal("Injection Device", item.DeviceName);
        Assert.Equal(processId, item.ProcessId);
    }

    [Fact]
    public async Task AiReadProcesses_ShouldReturnProcessCodeAndName()
    {
        var processReadService = new StubProcessReadQueryService();
        processReadService.PagedProcesses.Add(new ProcessReadItem(
            Guid.NewGuid(),
            "DieCuttingAnode",
            "负极模切"));
        var handler = new GetAiReadProcessesHandler(
            processReadService,
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadProcessesQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("DieCuttingAnode", item.ProcessCode);
        Assert.Equal("负极模切", item.ProcessName);
    }

    [Fact]
    public async Task AiReadClientReleaseVersions_ShouldReturnPublishedVersionsFromAggregate()
    {
        var repository = new InMemoryRepository<ClientReleaseComponent>();
        var component = ClientReleaseComponent.CreatePlugin(
            "Homogenization",
            "匀浆",
            null,
            null,
            null,
            "stable",
            "win-x64");
        component.UpsertPluginVersion(
            "1.0.0",
            "1.0.0",
            "1.0.0",
            "9.9.9",
            "net10.0",
            "/edge-updates/plugins/stable/Homogenization/1.0.0/Homogenization.zip",
            new string('a', 64),
            1024,
            "release notes",
            "[]",
            ClientReleaseStatus.Published,
            null,
            "IIoT");
        repository.ListResult.Add(component);
        var handler = new GetAiReadClientReleaseVersionsHandler(
            repository,
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadClientReleaseVersionsQuery(Channel: "stable", TargetRuntime: "win-x64"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Plugin", item.ComponentKind);
        Assert.Equal("Homogenization", item.ComponentKey);
        Assert.Equal("1.0.0", item.Version);
        Assert.Equal("release notes", item.ReleaseNotes);
    }

    [Fact]
    public async Task AiReadDeviceClientStates_ShouldReturnProjectedState()
    {
        var processId = Guid.NewGuid();
        var device = new Device("State Device", "DEV-STATE-001", processId);
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.ListResult.Add(device);
        var snapshot = new DeviceClientVersionSnapshot(
            device.Id,
            device.Code,
            "1.0.20",
            "1.0.0",
            "stable",
            DateTime.UtcNow,
            [],
            ["10.0.0.8"]);
        var state = new DeviceClientState(device.Id, device.Code);
        state.ApplyVersionReport(snapshot);
        var clientStateStore = new InMemoryDeviceClientStateStore();
        clientStateStore.States.Add(state);
        var handler = new GetAiReadDeviceClientStatesHandler(
            deviceRepository,
            clientStateStore,
            new TestAiReadScopeAccessor(),
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadDeviceClientStatesQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(device.Id, item.DeviceId);
        Assert.Equal("DEV-STATE-001", item.ClientCode);
        Assert.Equal("10.0.0.8", item.PrimaryIp);
        Assert.Equal("1.0.20", item.HostVersion);
    }

    [Fact]
    public async Task AiReadProductionRecords_ShouldReturnCommonColumnsFieldsAndSchema()
    {
        var deviceId = Guid.NewGuid();
        var queryService = new StubAiProductionRecordQueryService
        {
            Items =
            [
                new AiProductionRecordQueryItem(
                    Guid.NewGuid(),
                    "homogenization",
                    deviceId,
                    "匀浆1号机",
                    "BC-001",
                    "OK",
                    DateTime.UtcNow.AddMinutes(-5),
                    DateTime.UtcNow,
                    new Dictionary<string, object?>
                    {
                        ["viscosity"] = 123.45m,
                        ["solidContent"] = 56.7m,
                        ["mixerSpeed"] = 300m
                    })
            ],
            TotalCount = 1
        };
        var handler = new GetAiReadProductionRecordsHandler(
            CreatePassStationSchemaProvider(),
            queryService,
            new TestAiReadScopeAccessor { DelegatedDeviceIds = [deviceId] },
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadProductionRecordsQuery(
                TypeKey: "homogenization",
                DeviceId: deviceId,
                StartTime: DateTime.UtcNow.AddHours(-1),
                EndTime: DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("homogenization", item.TypeKey);
        Assert.Equal("匀浆", item.TypeName);
        Assert.Equal("BC-001", item.Barcode);
        Assert.True(item.Fields.ContainsKey("viscosity"));
        Assert.True(item.Fields.ContainsKey("solidContent"));
        Assert.False(item.Fields.ContainsKey("mixerSpeed"));
        Assert.Contains(item.FieldSchema, field => field.Key == "viscosity" && field.Label == "粘度");
    }

    [Fact]
    public async Task AiReadProductionRecords_ShouldSupportFieldModeFull()
    {
        var deviceId = Guid.NewGuid();
        var queryService = new StubAiProductionRecordQueryService
        {
            Items =
            [
                new AiProductionRecordQueryItem(
                    Guid.NewGuid(),
                    "homogenization",
                    deviceId,
                    "匀浆1号机",
                    "BC-002",
                    "OK",
                    DateTime.UtcNow.AddMinutes(-5),
                    DateTime.UtcNow,
                    new Dictionary<string, object?>
                    {
                        ["mixerSpeed"] = 300m
                    })
            ],
            TotalCount = 1
        };
        var handler = new GetAiReadProductionRecordsHandler(
            CreatePassStationSchemaProvider(),
            queryService,
            new TestAiReadScopeAccessor { DelegatedDeviceIds = [deviceId] },
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadProductionRecordsQuery(
                TypeKey: "homogenization",
                DeviceId: deviceId,
                StartTime: DateTime.UtcNow.AddHours(-1),
                EndTime: DateTime.UtcNow,
                FieldMode: "full"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.True(item.Fields.ContainsKey("mixerSpeed"));
        Assert.Contains(item.FieldSchema, field => field.Key == "mixerSpeed");
    }

    [Fact]
    public async Task AiReadProductionRecords_ShouldRejectGlobalQueryWithoutDeviceOrProcessOrType()
    {
        var handler = new GetAiReadProductionRecordsHandler(
            CreatePassStationSchemaProvider(),
            new StubAiProductionRecordQueryService(),
            new TestAiReadScopeAccessor(),
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadProductionRecordsQuery(Preset: "last_24h"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task AiReadProductionRecords_ShouldRejectPresetAndExplicitTimeConflict()
    {
        var deviceId = Guid.NewGuid();
        var handler = new GetAiReadProductionRecordsHandler(
            CreatePassStationSchemaProvider(),
            new StubAiProductionRecordQueryService(),
            new TestAiReadScopeAccessor { DelegatedDeviceIds = [deviceId] },
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadProductionRecordsQuery(
                TypeKey: "homogenization",
                DeviceId: deviceId,
                StartTime: DateTime.UtcNow.AddHours(-1),
                EndTime: DateTime.UtcNow,
                Preset: "last_24h"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task AiReadProductionRecords_ShouldFilterByProcessAndDelegatedScope()
    {
        var processId = Guid.NewGuid();
        var allowedDeviceId = Guid.NewGuid();
        var queryService = new StubAiProductionRecordQueryService();
        var handler = new GetAiReadProductionRecordsHandler(
            CreatePassStationSchemaProvider(),
            queryService,
            new TestAiReadScopeAccessor { DelegatedDeviceIds = [allowedDeviceId] },
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadProductionRecordsQuery(
                ProcessId: processId,
                Preset: "last_24h"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(processId, queryService.LastRequest!.ProcessId);
        Assert.Contains(allowedDeviceId, queryService.LastAllowedDeviceIds!);
    }

    [Fact]
    public async Task AiReadDeviceLogs_ShouldSupportPresetAndMinLevel()
    {
        var deviceId = Guid.NewGuid();
        var queryService = new StubDeviceLogQueryService();
        var handler = new GetAiReadDeviceLogsHandler(
            queryService,
            new TestAiReadScopeAccessor { DelegatedDeviceIds = [deviceId] },
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadDeviceLogsQuery(deviceId, null, null, Preset: "last_24h", MinLevel: "warn"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(queryService.LastLogsStartTime);
        Assert.NotNull(queryService.LastLogsEndTime);
        Assert.Contains("WARN", queryService.LastLogsNormalizedLevels!);
        Assert.Contains("ERROR", queryService.LastLogsNormalizedLevels!);
    }

    [Fact]
    public async Task AiReadDeviceLogs_ShouldRejectLevelAndMinLevelConflict()
    {
        var handler = new GetAiReadDeviceLogsHandler(
            new StubDeviceLogQueryService(),
            new TestAiReadScopeAccessor(),
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadDeviceLogsQuery(Guid.NewGuid(), null, null, Level: "ERROR", MinLevel: "warn", Preset: "last_24h"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task AiReadCapacityHourly_ShouldReturnLast24HoursAcrossDates()
    {
        var deviceId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var queryService = new StubCapacityQueryService
        {
            HourlyRangeResult =
            [
                new HourlyCapacityPointDto(
                    now.AddHours(-23),
                    DateOnly.FromDateTime(now.AddDays(-1)),
                    23,
                    0,
                    "23:00",
                    "N",
                    10,
                    9,
                    1),
                new HourlyCapacityPointDto(
                    now.AddHours(-1),
                    DateOnly.FromDateTime(now),
                    now.AddHours(-1).Hour,
                    0,
                    $"{now.AddHours(-1).Hour:00}:00",
                    "D",
                    20,
                    19,
                    1)
            ]
        };
        var handler = new GetAiReadCapacityHourlyHandler(
            queryService,
            new TestAiReadScopeAccessor { DelegatedDeviceIds = [deviceId] },
            Options.Create(new AiReadOptions()));

        var result = await handler.Handle(
            new GetAiReadCapacityHourlyQuery(deviceId, Preset: "last_24h"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal(90m, result.Value.Items[0].OkRate);
        Assert.Equal(deviceId, queryService.LastHourlyDeviceId);
        Assert.NotNull(queryService.LastHourlyRangeStart);
        Assert.NotNull(queryService.LastHourlyRangeEnd);
    }

    private static HttpContextAccessor CreateAccessor(string actorType, IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, "ai-read-test"),
            new(IIoTClaimTypes.ActorType, actorType)
        };
        claims.AddRange(permissions.Select(permission => new Claim(IIoTClaimTypes.Permission, permission)));

        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };
    }

    private static PassStationSchemaProvider CreatePassStationSchemaProvider()
    {
        return new PassStationSchemaProvider(Options.Create(new PassStationTypesOptions
        {
            Types =
            [
                new PassStationTypeDefinitionDto
                {
                    TypeKey = "homogenization",
                    DisplayName = "匀浆",
                    Description = "匀浆生产数据",
                    SupportedModes =
                    [
                        PassStationQueryModes.DeviceTime,
                        PassStationQueryModes.TimeProcess
                    ],
                    Fields =
                    [
                        new PassStationFieldDefinitionDto
                        {
                            Key = "viscosity",
                            Label = "粘度",
                            Type = PassStationFieldTypes.Number,
                            Required = true,
                            Unit = "mPa·s",
                            Precision = 2
                        },
                        new PassStationFieldDefinitionDto
                        {
                            Key = "solidContent",
                            Label = "固含量",
                            Type = PassStationFieldTypes.Number,
                            Required = true,
                            Unit = "%",
                            Precision = 2
                        },
                        new PassStationFieldDefinitionDto
                        {
                            Key = "mixerSpeed",
                            Label = "转速",
                            Type = PassStationFieldTypes.Number,
                            Unit = "rpm",
                            Precision = 1
                        }
                    ],
                    ListColumns =
                    [
                        "barcode",
                        "cellResult",
                        "viscosity",
                        "solidContent",
                        "completedTime"
                    ],
                    DetailSections =
                    [
                        new PassStationDetailSectionDto
                        {
                            Title = "匀浆数据",
                            Fields =
                            [
                                "barcode",
                                "viscosity",
                                "solidContent",
                                "mixerSpeed",
                                "completedTime"
                            ]
                        }
                    ]
                }
            ]
        }));
    }

    private sealed class InMemoryDeviceClientStateStore : IDeviceClientStateStore
    {
        public List<DeviceClientVersionSnapshot> VersionSnapshots { get; } = [];

        public List<EdgeDeviceRuntimeHeartbeat> RuntimeHeartbeats { get; } = [];

        public List<DeviceClientState> States { get; } = [];

        public Task<DeviceClientVersionSnapshot?> GetVersionSnapshotByDeviceAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(VersionSnapshots.SingleOrDefault(snapshot => snapshot.DeviceId == deviceId));
        }

        public Task<IReadOnlyList<DeviceClientVersionSnapshot>> GetVersionSnapshotsByDevicesAsync(
            IReadOnlyCollection<Guid>? deviceIds = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<DeviceClientVersionSnapshot>>(
                VersionSnapshots
                    .Where(snapshot => deviceIds == null || deviceIds.Contains(snapshot.DeviceId))
                    .OrderBy(snapshot => snapshot.ClientCode)
                    .ToList());
        }

        public Task<EdgeDeviceRuntimeHeartbeat?> GetRuntimeHeartbeatByIdentityAsync(
            Guid deviceId,
            string clientCode,
            CancellationToken cancellationToken = default)
        {
            var normalizedClientCode = clientCode.Trim().ToUpperInvariant();
            return Task.FromResult(RuntimeHeartbeats.SingleOrDefault(heartbeat =>
                heartbeat.DeviceId == deviceId && heartbeat.ClientCode == normalizedClientCode));
        }

        public Task<DeviceClientState?> GetStateByIdentityAsync(
            Guid deviceId,
            string clientCode,
            CancellationToken cancellationToken = default)
        {
            var normalizedClientCode = clientCode.Trim().ToUpperInvariant();
            return Task.FromResult(States.SingleOrDefault(state =>
                state.DeviceId == deviceId && state.ClientCode == normalizedClientCode));
        }

        public Task<IReadOnlyList<DeviceClientState>> GetStatesByDevicesAsync(
            IReadOnlyCollection<Guid>? deviceIds = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<DeviceClientState>>(
                States
                    .Where(state => deviceIds == null || deviceIds.Contains(state.DeviceId))
                    .OrderBy(state => state.ClientCode)
                    .ToList());
        }

        public void AddVersionSnapshot(DeviceClientVersionSnapshot snapshot)
        {
            VersionSnapshots.Add(snapshot);
        }

        public void AddRuntimeHeartbeat(EdgeDeviceRuntimeHeartbeat heartbeat)
        {
            RuntimeHeartbeats.Add(heartbeat);
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

    [AuthorizeAiRead(AiReadPermissions.Device)]
    private sealed record ValidAiReadQuery() : IAiReadQuery<Result<bool>>;

    [AuthorizeRequirement("Device.Read")]
    private sealed record AiReadWithHumanAuthorizationQuery() : IAiReadQuery<Result<bool>>;

    [AuthorizeAiRead(AiReadPermissions.Device)]
    private sealed record HumanWithAiReadAuthorizationQuery() : IHumanQuery<Result<bool>>;

    [AdminOnly]
    [AuthorizeAiRead(AiReadPermissions.Device)]
    private sealed record AiReadWithAdminOnlyQuery() : IAiReadQuery<Result<bool>>;

    private sealed record UnprotectedAiReadQuery() : IAiReadQuery<Result<bool>>;

    private sealed record PublicDownloadQuery() : IPublicQuery<Result<bool>>;

    [AuthorizeRequirement("Device.Read")]
    private sealed record PublicWithHumanAuthorizationQuery() : IPublicQuery<Result<bool>>;

    [AuthorizeAiRead(AiReadPermissions.Device)]
    private sealed record AuditedAiReadQuery() : IAiReadQuery<Result<AiReadListResponse<int>>>;
}
