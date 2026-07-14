using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Configuration;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace IIoT.CloudPlatform.EndToEndTests;

public sealed partial class CloudProductionFlowTests
{
    [Fact]
    public async Task AiReadDevices_ShouldRequireAiServiceAccountAndPermission()
    {
        await AuthenticateAsAdminAsync();
        await CreateTestDeviceRegistrationAsync("ai-read-auth");

        _fixture.ClearAuthToken();
        using (var anonymous = await _fixture.HttpClient.GetAsync("/api/v1/ai/read/devices"))
        {
            anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            var problem = await anonymous.Content.ReadFromJsonAsync<ProblemCodeEnvelope>(JsonOptions);
            problem!.Code.Should().Be("invalid_token");
        }

        await AuthenticateAsAdminAsync();
        using (var human = await _fixture.HttpClient.GetAsync("/api/v1/ai/read/devices"))
        {
            human.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var problem = await human.Content.ReadFromJsonAsync<ProblemCodeEnvelope>(JsonOptions);
            problem!.Code.Should().Be("forbidden_device_scope");
        }

        _fixture.SetAuthToken(CreateAiReadToken([]));
        using (var missingPermission = await _fixture.HttpClient.GetAsync("/api/v1/ai/read/devices"))
        {
            missingPermission.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.Device]));
        var devices = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceDto>>("/api/v1/ai/read/devices");

        devices.Source.Should().Be("devices");
        devices.RowCount.Should().Be(devices.Items.Count);
        devices.Items.Should().NotBeEmpty();

        var connectionString = await _fixture.GetConnectionStringAsync(ConnectionResourceNames.IiotDatabase);
        var audit = await EventuallyAsync(
            async () => await GetLatestAiReadAuditAsync(connectionString, "GetAiReadDevicesQuery"),
            row => row is not null && row.Succeeded);
        audit!.Summary.Should().Contain("endpoint=/api/v1/ai/read/devices");
        audit.Summary.Should().Contain("rowCount=");
        audit.Summary.ToLowerInvariant().Should().NotContain("prompt");
    }

    [Fact]
    public async Task AiReadDelegatedScope_ShouldFailClosedForEmptyAndInvalidClaims()
    {
        await AuthenticateAsAdminAsync();
        var target = await CreateTestDeviceRegistrationAsync("ai-read-scope");
        var delegatedUserId = Guid.NewGuid();
        var permissions = new[]
        {
            AiReadPermissions.Device,
            AiReadPermissions.DeviceClientState,
            AiReadPermissions.Capacity
        };

        _fixture.SetAuthToken(CreateAiReadToken(
            permissions,
            delegatedUserId: delegatedUserId));
        var emptyScope = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceDto>>(
            "/api/v1/ai/read/devices");
        emptyScope.Items.Should().BeEmpty();

        var invalidTokens = new[]
        {
            CreateAiReadToken(
                permissions,
                rawDelegatedDeviceIds: [target.DeviceId.ToString()]),
            CreateAiReadToken(
                permissions,
                rawDelegatedUserId: "invalid-user",
                rawDelegatedDeviceIds: [target.DeviceId.ToString()]),
            CreateAiReadToken(
                permissions,
                rawDelegatedUserId: delegatedUserId.ToString(),
                rawDelegatedDeviceIds: [target.DeviceId.ToString(), "invalid-device"])
        };
        foreach (var invalidToken in invalidTokens)
        {
            _fixture.SetAuthToken(invalidToken);

            using var devices = await _fixture.HttpClient.GetAsync("/api/v1/ai/read/devices");
            devices.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            using var states = await _fixture.HttpClient.GetAsync("/api/v1/ai/read/device-client-states");
            states.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            using var capacity = await _fixture.HttpClient.GetAsync(
                $"/api/v1/ai/read/capacity/hourly?deviceId={target.DeviceId}&preset=last_24h");
            capacity.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _fixture.SetAuthToken(CreateAiReadToken(
            permissions,
            rawDelegatedUserId: delegatedUserId.ToString(),
            rawDelegatedDeviceIds: [target.DeviceId.ToString(), target.DeviceId.ToString()]));
        var deduplicatedScope = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceDto>>(
            $"/api/v1/ai/read/devices?deviceId={target.DeviceId}");
        deduplicatedScope.Items.Should().ContainSingle(item => item.Id == target.DeviceId);
    }

    [Fact]
    public async Task AiReadQueryScopeAndAudit_ShouldRedactFreeTextSentinel()
    {
        const string sentinel = "SENSITIVE;token=do-not-store";
        await AuthenticateAsAdminAsync();
        var device = await CreateTestDeviceRegistrationAsync("ai-read-redaction");
        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.Device], [device.DeviceId]));

        var response = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceDto>>(
            $"/api/v1/ai/read/devices?keyword={Uri.EscapeDataString(sentinel)}");

        response.QueryScope.Should().Contain("keyword=present");
        response.QueryScope.Should().NotContain(sentinel);

        var connectionString = await _fixture.GetConnectionStringAsync(ConnectionResourceNames.IiotDatabase);
        var audit = await EventuallyAsync(
            async () => await GetLatestAiReadAuditAsync(connectionString, "GetAiReadDevicesQuery"),
            row => row is not null && row.Succeeded && row.Summary.Contains("keyword=present", StringComparison.Ordinal));
        audit!.Summary.Should().NotContain(sentinel);
    }

    [Fact]
    public async Task AiReadServiceAccount_ShouldReadProductionReadOnlySurfaces()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("ai-read-flow");
        var completedTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-2), DateTimeKind.Utc);
        var date = DateOnly.FromDateTime(completedTime);
        var message = $"ai-read-log-{Guid.NewGuid():N}";
        var barcode = $"AI-{Guid.NewGuid():N}"[..14];
        var plcName = $"AI-{Guid.NewGuid():N}"[..10];

        await AuthenticateAsEdgeAsync(device.DeviceId);

        await PostJsonAsync("/api/v1/edge/device-logs", new
        {
            DeviceId = device.DeviceId,
            Logs = new[]
            {
                new
                {
                    Level = "INFO",
                    Message = message,
                    LogTime = completedTime
                }
            }
        });
        await PostJsonAsync("/api/v1/edge/capacity/hourly", new
        {
            DeviceId = device.DeviceId,
            Date = date,
            ShiftCode = "D",
            Hour = completedTime.Hour,
            Minute = completedTime.Minute,
            TimeLabel = completedTime.ToString("HH:mm"),
            TotalCount = 7,
            OkCount = 6,
            NgCount = 1,
            PlcName = plcName
        });
        await PostJsonAsync("/api/v1/edge/pass-stations/injection/batch", new
        {
            DeviceId = device.DeviceId,
            Items = new[]
            {
                new
                {
                    Barcode = barcode,
                    CellResult = "OK",
                    CompletedTime = completedTime,
                    Payload = new
                    {
                        PreInjectionTime = completedTime.AddSeconds(-20),
                        PreInjectionWeight = 11.2m,
                        PostInjectionTime = completedTime.AddSeconds(-5),
                        PostInjectionWeight = 12.5m,
                        InjectionVolume = 1.3m
                    }
                }
            }
        });

        _fixture.SetAuthToken(CreateAiReadToken(
            [
                AiReadPermissions.Device,
                AiReadPermissions.Capacity,
                AiReadPermissions.DeviceLog,
                AiReadPermissions.ProductionRecord
            ],
            [device.DeviceId]));

        var devices = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceDto>>("/api/v1/ai/read/devices");
        devices.Items.Should().ContainSingle(x => x.Id == device.DeviceId && x.DeviceCode == device.Code);

        var startTime = Uri.EscapeDataString(completedTime.AddMinutes(-1).ToString("O"));
        var endTime = Uri.EscapeDataString(completedTime.AddMinutes(1).ToString("O"));
        var logs = await EventuallyAsync(
            async () => await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceLogDto>>(
                $"/api/v1/ai/read/device-logs?deviceId={device.DeviceId}&startTime={startTime}&endTime={endTime}&keyword={Uri.EscapeDataString(message)}"),
            response => response.Items.Any(x => x.Message == message));
        logs.Source.Should().Be("device_logs");
        logs.Truncated.Should().BeFalse();

        var capacity = await EventuallyAsync(
            async () => await GetFromJsonAsync<AiReadListResponseDto<AiReadCapacitySummaryDto>>(
                $"/api/v1/ai/read/capacity/summary?deviceId={device.DeviceId}&startDate={date:yyyy-MM-dd}&endDate={date:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}"),
            response => response.Items.Any(x => x.TotalCount == 7 && x.OkCount == 6));
        capacity.Source.Should().Be("capacity.summary");

        var productionRecords = await EventuallyAsync(
            async () => await GetFromJsonAsync<AiReadListResponseDto<AiReadProductionRecordDto>>(
                $"/api/v1/ai/read/production-records?typeKey=injection&deviceId={device.DeviceId}&startTime={startTime}&endTime={endTime}&barcode={Uri.EscapeDataString(barcode)}"),
            response => response.Items.Any(x => x.Barcode == barcode));
        var productionRecord = productionRecords.Items.Single(x => x.Barcode == barcode);
        productionRecords.Source.Should().Be("production_records");
        productionRecord.TypeKey.Should().Be("injection");
        productionRecord.Fields.Should().ContainKey("injectionVolume");
        productionRecord.Fields.Should().NotContainKey("notConfigured");
    }

    [Fact]
    public async Task AiReadDevices_ShouldSupportExactFiltersAndStrictFieldContract()
    {
        await AuthenticateAsAdminAsync();
        var target = await CreateTestDeviceRegistrationAsync("ai-read-exact");
        var outsideScope = await CreateTestDeviceRegistrationAsync("ai-read-outside");
        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.Device], [target.DeviceId]));

        var byId = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceDto>>(
            $"/api/v1/ai/read/devices?deviceId={target.DeviceId}");
        byId.Items.Should().ContainSingle(item => item.Id == target.DeviceId);

        var encodedCode = Uri.EscapeDataString($" {target.Code.ToLowerInvariant()} ");
        var byCode = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceDto>>(
            $"/api/v1/ai/read/devices?deviceCode={encodedCode}");
        byCode.Items.Should().ContainSingle(item => item.Id == target.DeviceId);

        var byProcess = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceDto>>(
            $"/api/v1/ai/read/devices?processId={target.ProcessId}");
        byProcess.Items.Should().ContainSingle(item => item.Id == target.DeviceId);

        using var rawResponse = await _fixture.HttpClient.GetAsync(
            $"/api/v1/ai/read/devices?deviceId={target.DeviceId}&deviceCode={target.Code}&processId={target.ProcessId}");
        rawResponse.EnsureSuccessStatusCode();
        using var rawJson = JsonDocument.Parse(await rawResponse.Content.ReadAsStringAsync());
        var itemProperties = rawJson.RootElement
            .GetProperty("items")[0]
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        itemProperties.Should().BeEquivalentTo(
            ["id", "deviceCode", "deviceName", "processId"],
            options => options.WithStrictOrdering());

        using var forbidden = await _fixture.HttpClient.GetAsync(
            $"/api/v1/ai/read/devices?deviceId={outsideScope.DeviceId}");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var nonexistentId = Guid.NewGuid();
        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.Device], [nonexistentId]));
        var nonexistent = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceDto>>(
            $"/api/v1/ai/read/devices?deviceId={nonexistentId}");
        nonexistent.Items.Should().BeEmpty();

        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.Device], [target.DeviceId]));
        var hiddenOutsideCode = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceDto>>(
            $"/api/v1/ai/read/devices?deviceCode={outsideScope.Code}");
        hiddenOutsideCode.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("status")]
    [InlineData("lineName")]
    [InlineData("processName")]
    [InlineData("updatedAt")]
    public async Task AiReadDevices_ShouldRejectKnownMisleadingQueryParameters(string parameterName)
    {
        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.Device]));

        using var response = await _fixture.HttpClient.GetAsync(
            $"/api/v1/ai/read/devices?{parameterName}=misleading");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AiReadInvalidDeviceParameters_ShouldReturnBadRequestAndWriteFailureAudit()
    {
        _fixture.SetAuthToken(CreateAiReadToken(
            [AiReadPermissions.Device, AiReadPermissions.DeviceClientState]));

        using (var unsupported = await _fixture.HttpClient.GetAsync(
                   "/api/v1/ai/read/devices?status=misleading"))
        {
            unsupported.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using (var blankDeviceCode = await _fixture.HttpClient.GetAsync(
                   "/api/v1/ai/read/devices?deviceCode=%20%20%20"))
        {
            blankDeviceCode.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using (var blankStateDeviceCode = await _fixture.HttpClient.GetAsync(
                   "/api/v1/ai/read/device-client-states?deviceCode=%20%20%20"))
        {
            blankStateDeviceCode.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        var connectionString = await _fixture.GetConnectionStringAsync(ConnectionResourceNames.IiotDatabase);
        var audit = await EventuallyAsync(
            async () => await GetLatestAiReadAuditAsync(connectionString, "GetAiReadDevicesQuery"),
            row => row is not null && !row.Succeeded);
        audit!.Summary.Should().Contain("endpoint=/api/v1/ai/read/devices");
        audit.Summary.Should().NotContain("misleading");
    }

    [Fact]
    public async Task AiReadDeviceClientStates_ShouldRequireDedicatedPermissionAndDelegatedScope()
    {
        await AuthenticateAsAdminAsync();
        var target = await CreateTestDeviceRegistrationAsync("ai-state-auth");
        var outsideScope = await CreateTestDeviceRegistrationAsync("ai-state-outside");

        _fixture.ClearAuthToken();
        using (var anonymous = await _fixture.HttpClient.GetAsync("/api/v1/ai/read/device-client-states"))
        {
            anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await AuthenticateAsAdminAsync();
        using (var human = await _fixture.HttpClient.GetAsync("/api/v1/ai/read/device-client-states"))
        {
            human.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _fixture.SetAuthToken(CreateAiReadToken([]));
        using (var missingPermission = await _fixture.HttpClient.GetAsync("/api/v1/ai/read/device-client-states"))
        {
            missingPermission.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.Device], [target.DeviceId]));
        using (var devicePermissionOnly = await _fixture.HttpClient.GetAsync("/api/v1/ai/read/device-client-states"))
        {
            devicePermissionOnly.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.DeviceClientState], [target.DeviceId]));
        var allowed = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceClientStateDto>>(
            $"/api/v1/ai/read/device-client-states?deviceId={target.DeviceId}");
        allowed.Items.Should().ContainSingle(item => item.DeviceId == target.DeviceId);

        using var forbidden = await _fixture.HttpClient.GetAsync(
            $"/api/v1/ai/read/device-client-states?deviceId={outsideScope.DeviceId}");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AiReadDeviceClientStates_ShouldProjectMissingHeartbeatAndKeepRuntimeFreshnessIndependent()
    {
        await AuthenticateAsAdminAsync();
        var target = await CreateTestDeviceRegistrationAsync("ai-state-flow");
        var second = await CreateTestDeviceRegistrationAsync("ai-state-page");
        _fixture.SetAuthToken(CreateAiReadToken(
            [AiReadPermissions.DeviceClientState],
            [target.DeviceId, second.DeviceId]));

        var encodedCode = Uri.EscapeDataString($" {target.Code.ToLowerInvariant()} ");
        var missing = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceClientStateDto>>(
            $"/api/v1/ai/read/device-client-states?deviceId={target.DeviceId}&deviceCode={encodedCode}" +
            $"&processId={target.ProcessId}&keyword={Uri.EscapeDataString(target.Code)}");
        var missingItem = missing.Items.Should().ContainSingle().Subject;
        missing.RowCount.Should().Be(1);
        missing.Truncated.Should().BeFalse();
        missing.QueryScope.Should().Contain("deviceCode=present");
        missing.QueryScope.Should().NotContain(target.Code);
        missingItem.DeviceId.Should().Be(target.DeviceId);
        missingItem.ClientCode.Should().Be(target.Code);
        missingItem.SoftwareStatus.Should().Be("MissingRuntimeHeartbeat");
        missingItem.PrimaryIp.Should().BeNull();
        missingItem.Channel.Should().BeNull();
        missingItem.HostVersion.Should().BeNull();
        missingItem.HostApiVersion.Should().BeNull();
        missingItem.VersionReportedAtUtc.Should().BeNull();
        missingItem.VersionReceivedAtUtc.Should().BeNull();
        missingItem.RuntimeStatus.Should().BeNull();
        missingItem.RuntimeStartedAtUtc.Should().BeNull();
        missingItem.LastRuntimeHeartbeatAtUtc.Should().BeNull();
        missingItem.UpdatedAtUtc.Should().BeNull();

        var paged = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceClientStateDto>>(
            "/api/v1/ai/read/device-client-states?maxRows=1");
        paged.Items.Should().ContainSingle();
        paged.RowCount.Should().Be(1);
        paged.Truncated.Should().BeTrue();

        using (var strictResponse = await _fixture.HttpClient.GetAsync(
                   $"/api/v1/ai/read/device-client-states?deviceId={target.DeviceId}"))
        {
            strictResponse.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await strictResponse.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("items")[0]
                .EnumerateObject()
                .Select(property => property.Name)
                .Should()
                .BeEquivalentTo(
                    [
                        "deviceId",
                        "deviceName",
                        "clientCode",
                        "primaryIp",
                        "channel",
                        "hostVersion",
                        "hostApiVersion",
                        "versionReportedAtUtc",
                        "versionReceivedAtUtc",
                        "softwareStatus",
                        "runtimeStatus",
                        "runtimeStartedAtUtc",
                        "lastRuntimeHeartbeatAtUtc",
                        "updatedAtUtc"
                    ],
                    options => options.WithStrictOrdering());
        }

        var startedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Utc);
        var heartbeatCandidate = DateTime.UtcNow.AddSeconds(-2);
        var heartbeatReportedAtUtc = new DateTime(
            heartbeatCandidate.Ticks - heartbeatCandidate.Ticks % 10,
            DateTimeKind.Utc);
        var runtimeInstanceId = $"runtime-{Guid.NewGuid():N}";
        var heartbeatPayload = new
        {
            DeviceId = target.DeviceId,
            ClientCode = target.Code,
            RuntimeInstanceId = runtimeInstanceId,
            MachineProfile = "ai-state-e2e",
            HostVersion = "1.0.25",
            HostApiVersion = "1.0.0",
            Status = "Running",
            StartedAtUtc = startedAtUtc,
            ReportedAtUtc = heartbeatReportedAtUtc,
            LocalIpAddresses = new[] { "10.20.30.40" }
        };
        await AuthenticateAsEdgeAsync(target.DeviceId);
        await PostJsonAsync("/api/v1/edge/runtime-heartbeats", heartbeatPayload);
        await PostJsonAsync("/api/v1/edge/runtime-heartbeats", heartbeatPayload);

        using (var staleHeartbeat = await _fixture.HttpClient.PostAsJsonAsync(
                   "/api/v1/edge/runtime-heartbeats",
                   new
                   {
                       heartbeatPayload.DeviceId,
                       heartbeatPayload.ClientCode,
                       heartbeatPayload.RuntimeInstanceId,
                       heartbeatPayload.MachineProfile,
                       heartbeatPayload.HostVersion,
                       heartbeatPayload.HostApiVersion,
                       Status = "Stopped",
                       heartbeatPayload.StartedAtUtc,
                       ReportedAtUtc = heartbeatReportedAtUtc.AddSeconds(-1),
                       heartbeatPayload.LocalIpAddresses
                   }))
        {
            staleHeartbeat.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using (var conflictingHeartbeat = await _fixture.HttpClient.PostAsJsonAsync(
                   "/api/v1/edge/runtime-heartbeats",
                   new
                   {
                       heartbeatPayload.DeviceId,
                       heartbeatPayload.ClientCode,
                       heartbeatPayload.RuntimeInstanceId,
                       heartbeatPayload.MachineProfile,
                       heartbeatPayload.HostVersion,
                       heartbeatPayload.HostApiVersion,
                       Status = "Stopped",
                       heartbeatPayload.StartedAtUtc,
                       heartbeatPayload.ReportedAtUtc,
                       heartbeatPayload.LocalIpAddresses
                   }))
        {
            conflictingHeartbeat.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using (var futureHeartbeat = await _fixture.HttpClient.PostAsJsonAsync(
                   "/api/v1/edge/runtime-heartbeats",
                   new
                   {
                       heartbeatPayload.DeviceId,
                       heartbeatPayload.ClientCode,
                       heartbeatPayload.RuntimeInstanceId,
                       heartbeatPayload.MachineProfile,
                       heartbeatPayload.HostVersion,
                       heartbeatPayload.HostApiVersion,
                       heartbeatPayload.Status,
                       StartedAtUtc = DateTime.UtcNow,
                       ReportedAtUtc = DateTime.UtcNow
                           .Add(DeviceClientSoftwareStatusResolver.MaximumFutureClockSkew)
                           .AddSeconds(1),
                       heartbeatPayload.LocalIpAddresses
                   }))
        {
            futureHeartbeat.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.DeviceClientState], [target.DeviceId]));
        var running = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceClientStateDto>>(
            $"/api/v1/ai/read/device-client-states?deviceId={target.DeviceId}");
        var runningItem = running.Items.Should().ContainSingle().Subject;
        runningItem.SoftwareStatus.Should().Be("Running");
        runningItem.RuntimeStatus.Should().Be("Running");
        runningItem.LastRuntimeHeartbeatAtUtc.Should().Be(heartbeatReportedAtUtc);
        runningItem.PrimaryIp.Should().Be("10.20.30.40");

        await AuthenticateAsEdgeAsync(target.DeviceId);
        await PostJsonAsync("/api/v1/edge/client-releases/version-reports", new
        {
            DeviceId = target.DeviceId,
            ClientCode = target.Code,
            HostVersion = "1.0.26",
            HostApiVersion = "1.0.0",
            InstalledPlugins = Array.Empty<object>(),
            EnabledPlugins = Array.Empty<string>(),
            Channel = "stable",
            ReportedAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            LocalIpAddresses = new[] { "10.20.30.41" }
        });

        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.DeviceClientState], [target.DeviceId]));
        var afterVersionReport = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceClientStateDto>>(
            $"/api/v1/ai/read/device-client-states?deviceId={target.DeviceId}");
        var afterVersionItem = afterVersionReport.Items.Should().ContainSingle().Subject;
        afterVersionItem.SoftwareStatus.Should().Be("Running");
        afterVersionItem.RuntimeStatus.Should().Be("Running");
        afterVersionItem.LastRuntimeHeartbeatAtUtc.Should().Be(heartbeatReportedAtUtc);
        afterVersionItem.PrimaryIp.Should().Be("10.20.30.40");
        afterVersionItem.HostVersion.Should().Be("1.0.26");
    }

    [Theory]
    [InlineData("softwareStatus")]
    [InlineData("runtimeStatus")]
    [InlineData("status")]
    [InlineData("lineName")]
    [InlineData("processName")]
    [InlineData("updatedAt")]
    [InlineData("updatedAtUtc")]
    public async Task AiReadDeviceClientStates_ShouldRejectKnownMisleadingQueryParameters(string parameterName)
    {
        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.DeviceClientState]));

        using var response = await _fixture.HttpClient.GetAsync(
            $"/api/v1/ai/read/device-client-states?{parameterName}=misleading");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AiReadProcesses_ShouldSupportExactProcessId()
    {
        await AuthenticateAsAdminAsync();
        var device = await CreateTestDeviceRegistrationAsync("ai-read-process");
        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.Process]));

        var result = await GetFromJsonAsync<AiReadListResponseDto<AiReadProcessDto>>(
            $"/api/v1/ai/read/processes?processId={device.ProcessId}");

        var process = result.Items.Single();
        process.Id.Should().Be(device.ProcessId);
        process.ProcessCode.Should().NotBeNullOrWhiteSpace();
        process.ProcessName.Should().NotBeNullOrWhiteSpace();

        using var invalid = await _fixture.HttpClient.GetAsync(
            $"/api/v1/ai/read/processes?processId={Guid.Empty}");
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PassStationBatchUpload_ShouldPersistOnceAndBeReadableByHumanAndAiRead()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("pass-station-batch");
        await AuthenticateAsEdgeAsync(device.DeviceId);
        var completedTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-2), DateTimeKind.Utc);
        var barcode = $"PR-{Guid.NewGuid():N}"[..14];

        var request = new
        {
            DeviceId = device.DeviceId,
            Items = new[]
            {
                new
                {
                    Barcode = barcode,
                    CellResult = "NG",
                    CompletedTime = completedTime,
                    Payload = new
                    {
                        PreInjectionTime = completedTime.AddSeconds(-18),
                        PreInjectionWeight = 14.1m,
                        PostInjectionTime = completedTime.AddSeconds(-4),
                        PostInjectionWeight = 15.6m,
                        InjectionVolume = 1.5m
                    }
                }
            }
        };

        await PostJsonAsync("/api/v1/edge/pass-stations/injection/batch", request);
        using (var duplicateResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/edge/pass-stations/injection/batch", request))
        {
            duplicateResponse.EnsureSuccessStatusCode();
            var duplicate = await duplicateResponse.Content.ReadFromJsonAsync<EdgeUploadAcceptedResponseDto>(JsonOptions);
            duplicate!.Code.Should().Be("duplicate_accepted");
            duplicate.DuplicateAccepted.Should().BeTrue();
        }

        await AuthenticateAsAdminAsync();
        var startTime = Uri.EscapeDataString(completedTime.AddMinutes(-1).ToString("O"));
        var endTime = Uri.EscapeDataString(completedTime.AddMinutes(1).ToString("O"));
        var humanPassStations = await EventuallyAsync(
            async () => await GetFromJsonAsync<PagedResponse<PassStationListItemDto>>(
                $"/api/v1/human/pass-stations/injection?PageNumber=1&PageSize=20&mode=device-time&deviceId={device.DeviceId}" +
                $"&startTime={startTime}&endTime={endTime}"),
            response => response.Items.Count(x => x.Barcode == barcode) == 1);

        var humanItem = humanPassStations.Items.Single(x => x.Barcode == barcode);
        humanItem.CellResult.Should().Be("NG");
        humanItem.Fields.Should().ContainKey("injectionVolume");

        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.ProductionRecord], [device.DeviceId]));
        var aiProductionRecords = await EventuallyAsync(
            async () => await GetFromJsonAsync<AiReadListResponseDto<AiReadProductionRecordDto>>(
                $"/api/v1/ai/read/production-records?typeKey=injection&deviceId={device.DeviceId}&startTime={startTime}&endTime={endTime}&barcode={Uri.EscapeDataString(barcode)}"),
            response => response.Items.Count(x => x.Barcode == barcode) == 1);

        var aiItem = aiProductionRecords.Items.Single(x => x.Barcode == barcode);
        aiItem.Result.Should().Be("NG");
        aiItem.Fields.Should().ContainKey("injectionVolume");
    }

    [Fact]
    public async Task AiReadToken_ShouldNotAccessHumanMutationOrEdgeUpload()
    {
        _fixture.SetAuthToken(CreateAiReadToken(
            [AiReadPermissions.Device, AiReadPermissions.DeviceLog]));

        using (var humanMutation = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/human/devices", new
               {
                   DeviceName = "ai-read-mutation-denied",
                   ProcessId = Guid.NewGuid()
               }))
        {
            humanMutation.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using (var edgeUpload = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/edge/device-logs", new
               {
                   DeviceId = Guid.NewGuid(),
                   Logs = new[]
                   {
                       new
                       {
                           Level = "INFO",
                           Message = "ai-read-edge-denied",
                           LogTime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
                       }
                   }
               }))
        {
            edgeUpload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task AiReadDeviceLogsKeyword_ShouldRequireTimeRange()
    {
        _fixture.SetAuthToken(CreateAiReadToken([AiReadPermissions.DeviceLog]));

        using var response = await _fixture.HttpClient.GetAsync(
            $"/api/v1/ai/read/device-logs?deviceId={Guid.NewGuid()}&keyword=error");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static string CreateAiReadToken(
        IEnumerable<string> permissions,
        IReadOnlyCollection<Guid>? delegatedDeviceIds = null,
        Guid? delegatedUserId = null,
        string? rawDelegatedUserId = null,
        IReadOnlyCollection<string>? rawDelegatedDeviceIds = null) =>
        CloudTestDriver.CreateAiReadToken(
            permissions,
            delegatedDeviceIds,
            delegatedUserId,
            rawDelegatedUserId,
            rawDelegatedDeviceIds);

    private static async Task<AiReadAuditRow?> GetLatestAiReadAuditAsync(
        string connectionString,
        string targetIdOrKey)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            select "Succeeded", "Summary"
            from audit_trails
            where "OperationType" = 'AiRead.Query'
              and "TargetIdOrKey" = @targetIdOrKey
            order by "ExecutedAtUtc" desc
            limit 1
            """,
            connection);
        command.Parameters.AddWithValue("targetIdOrKey", targetIdOrKey);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new AiReadAuditRow(
            reader.GetBoolean(0),
            reader.GetString(1));
    }
}

public sealed record AiReadListResponseDto<T>(
    List<T> Items,
    DateTimeOffset AsOfUtc,
    string Source,
    string QueryScope,
    int RowCount,
    bool Truncated,
    string? NextCursor);

public sealed record AiReadDeviceDto(
    Guid Id,
    string DeviceCode,
    string DeviceName,
    Guid ProcessId);

public sealed record AiReadProcessDto(
    Guid Id,
    string ProcessCode,
    string ProcessName);

public sealed record AiReadDeviceClientStateDto(
    Guid DeviceId,
    string DeviceName,
    string ClientCode,
    string? PrimaryIp,
    string? Channel,
    string? HostVersion,
    string? HostApiVersion,
    DateTime? VersionReportedAtUtc,
    DateTime? VersionReceivedAtUtc,
    string SoftwareStatus,
    string? RuntimeStatus,
    DateTime? RuntimeStartedAtUtc,
    DateTime? LastRuntimeHeartbeatAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record AiReadCapacitySummaryDto(
    DateOnly Date,
    int TotalCount,
    int OkCount,
    int NgCount,
    int DayShiftTotal,
    int NightShiftTotal);

public sealed record AiReadDeviceLogDto(
    Guid Id,
    Guid DeviceId,
    string DeviceName,
    string Level,
    string Message,
    DateTime LogTime,
    DateTime ReceivedAt);

public sealed record AiReadProductionFieldSchemaDto(
    string Key,
    string Label,
    string Type,
    string? Unit,
    int? Precision,
    bool Required);

public sealed record AiReadProductionRecordDto(
    Guid RecordId,
    string TypeKey,
    string TypeName,
    Guid DeviceId,
    string DeviceName,
    string? Barcode,
    string? Result,
    DateTime? CompletedAt,
    DateTime? ReceivedAt,
    Dictionary<string, JsonElement> Fields,
    List<AiReadProductionFieldSchemaDto> FieldSchema);

public sealed record AiReadAuditRow(
    bool Succeeded,
    string Summary);
