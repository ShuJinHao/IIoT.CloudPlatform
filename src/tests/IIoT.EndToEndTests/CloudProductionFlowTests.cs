using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using FluentAssertions;
using IIoT.EventBus;
using IIoT.Services.Contracts.Events.DeviceLogs;
using IIoT.SharedKernel.Configuration;
using MassTransit;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace IIoT.EndToEndTests;

public sealed partial class CloudProductionFlowTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan EventuallyTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan EventuallyInterval = TimeSpan.FromMilliseconds(500);

    private readonly IIoTAppFixture _fixture = new();
    private readonly Dictionary<Guid, string> _deviceCodes = new();

    public Task InitializeAsync() => _fixture.StartAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync().AsTask();

    [Fact]
    public async Task DeviceLogs_DuplicateConsume_ShouldPersistOneRow()
    {
        await AuthenticateAsAdminAsync();

        var deviceId = await CreateTestDeviceAsync("log");
        await AuthenticateAsEdgeAsync(deviceId);
        var logTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-2), DateTimeKind.Utc);
        var message = $"device-log-{Guid.NewGuid():N}";

        var request = new
        {
            DeviceId = deviceId,
            Logs = new[]
            {
                new
                {
                    Level = "INFO",
                    Message = message,
                    LogTime = logTime
                }
            }
        };

        await PostJsonAsync("/api/v1/edge/device-logs", request);
        await PostJsonAsync("/api/v1/edge/device-logs", request);

        await AuthenticateAsAdminAsync();

        var result = await EventuallyAsync(async () =>
            await GetFromJsonAsync<PagedResponse<DeviceLogListItemDto>>(
                $"/api/v1/human/device-logs/by-time-range?PageNumber=1&PageSize=20&deviceId={deviceId}" +
                $"&startTime={Uri.EscapeDataString(logTime.AddMinutes(-1).ToString("O"))}" +
                $"&endTime={Uri.EscapeDataString(logTime.AddMinutes(1).ToString("O"))}"),
            response => response.Items.Count(x => x.Message == message) == 1);

        result.Items.Should().ContainSingle(x => x.Message == message);
    }

    [Fact]
    public async Task HourlyCapacity_DuplicateConsume_ShouldUpsertLatestValues()
    {
        await AuthenticateAsAdminAsync();

        var deviceId = await CreateTestDeviceAsync("capacity");
        await AuthenticateAsEdgeAsync(deviceId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var plcName = $"PLC-{Guid.NewGuid():N}"[..10];

        var first = new
        {
            DeviceId = deviceId,
            Date = date,
            ShiftCode = "D",
            Hour = 9,
            Minute = 30,
            TimeLabel = "09:30",
            TotalCount = 10,
            OkCount = 9,
            NgCount = 1,
            PlcName = plcName
        };

        var second = new
        {
            DeviceId = deviceId,
            Date = date,
            ShiftCode = "D",
            Hour = 9,
            Minute = 30,
            TimeLabel = "09:30",
            TotalCount = 16,
            OkCount = 15,
            NgCount = 1,
            PlcName = plcName
        };

        await PostJsonAsync("/api/v1/edge/capacity/hourly", first);
        await PostJsonAsync("/api/v1/edge/capacity/hourly", second);

        await AuthenticateAsAdminAsync();

        var result = await EventuallyAsync(async () =>
            await GetFromJsonAsync<List<HourlyCapacityDto>>(
                $"/api/v1/human/capacity/hourly?deviceId={deviceId}&date={date:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}"),
            response => response.Count == 1 && response[0].TotalCount == 16 && response[0].OkCount == 15);

        result.Should().ContainSingle();
        result[0].TotalCount.Should().Be(16);
        result[0].OkCount.Should().Be(15);
        result[0].NgCount.Should().Be(1);
    }

    [Fact]
    public async Task PassDataInjection_DuplicateConsume_ShouldPersistOneRow()
    {
        await AuthenticateAsAdminAsync();

        var deviceId = await CreateTestDeviceAsync("pass");
        await AuthenticateAsEdgeAsync(deviceId);
        var completedTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-3), DateTimeKind.Utc);
        var barcode = $"BC-{Guid.NewGuid():N}"[..14];

        var request = new
        {
            DeviceId = deviceId,
            Items = new[]
            {
                new
                {
                    Barcode = barcode,
                    CellResult = "OK",
                    CompletedTime = completedTime,
                    PreInjectionTime = completedTime.AddSeconds(-15),
                    PreInjectionWeight = 12.34m,
                    PostInjectionTime = completedTime.AddSeconds(-3),
                    PostInjectionWeight = 13.21m,
                    InjectionVolume = 0.87m
                }
            }
        };

        await PostJsonAsync("/api/v1/edge/pass-stations/injection/batch", request);
        await PostJsonAsync("/api/v1/edge/pass-stations/injection/batch", request);

        await AuthenticateAsAdminAsync();

        var result = await EventuallyAsync(async () =>
            await GetFromJsonAsync<PagedResponse<InjectionPassListItemDto>>(
                $"/api/v1/human/pass-stations/injection/by-device-time?PageNumber=1&PageSize=20&deviceId={deviceId}" +
                $"&startTime={Uri.EscapeDataString(completedTime.AddMinutes(-1).ToString("O"))}" +
                $"&endTime={Uri.EscapeDataString(completedTime.AddMinutes(1).ToString("O"))}"),
            response => response.Items.Count(x => x.Barcode == barcode) == 1);

        result.Items.Should().ContainSingle(x => x.Barcode == barcode);
    }

    [Fact]
    public async Task PassDataStacking_DuplicateConsume_ShouldPersistOneRow()
    {
        await AuthenticateAsAdminAsync();

        var deviceId = await CreateTestDeviceAsync("stacking-pass");
        await AuthenticateAsEdgeAsync(deviceId);
        var connectionString = await _fixture.GetConnectionStringAsync(ConnectionResourceNames.IiotDatabase);
        var completedTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-4), DateTimeKind.Utc);
        var barcode = $"ST-{Guid.NewGuid():N}"[..14];

        var request = new
        {
            DeviceId = deviceId,
            Item = new
            {
                Barcode = barcode,
                TrayCode = "TRAY-STACK-01",
                LayerCount = 16,
                SequenceNo = 9,
                CellResult = "OK",
                CompletedTime = completedTime
            }
        };

        await PostJsonAsync("/api/v1/edge/pass-stations/stacking", request);
        await PostJsonAsync("/api/v1/edge/pass-stations/stacking", request);

        var rows = await EventuallyAsync(
            async () => await GetStackingPassRowsAsync(connectionString, deviceId, barcode),
            response => response.Count == 1);

        rows.Should().ContainSingle();
        rows[0].TrayCode.Should().Be("TRAY-STACK-01");
        rows[0].LayerCount.Should().Be(16);
        rows[0].SequenceNo.Should().Be(9);
        rows[0].CellResult.Should().Be("OK");
        rows[0].CompletedTime.ToUniversalTime().Should().BeCloseTo(completedTime, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task DeviceLogConsumer_RepeatedFailures_ShouldMoveMessageToErrorQueue()
    {
        await AuthenticateAsAdminAsync();

        var connectionString = await _fixture.GetConnectionStringAsync(ConnectionResourceNames.EventBus);
        var queueName = $"{RabbitMqEndpointNames.DeviceLogs}_error";
        var initialCount = await GetQueueMessageCountAsync(connectionString, queueName);

        await PublishInvalidDeviceLogEventAsync(connectionString);

        var finalCount = await EventuallyAsync(
            async () => await GetQueueMessageCountAsync(connectionString, queueName),
            count => count == initialCount + 1);

        finalCount.Should().Be(initialCount + 1);
    }

    [Fact]
    public async Task HumanIdentity_RolesAndPermissions_ShouldLoad()
    {
        await AuthenticateAsAdminAsync();

        var roles = await GetFromJsonAsync<List<string>>("/api/v1/human/identity/roles");
        var permissions = await GetFromJsonAsync<List<PermissionGroupDto>>("/api/v1/human/identity/permissions");

        roles.Should().NotBeEmpty();
        permissions.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HumanIdentity_LoginAndRefresh_ShouldRotateRefreshToken()
    {
        _fixture.ClearAuthToken();

        using var loginResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/human/identity/login", new
        {
            EmployeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
            Password = IIoTAppFixture.SeedAdminPassword
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginSession = await ReadIssuedAuthSessionAsync(loginResponse);
        loginSession.AccessToken.Should().NotBeNullOrWhiteSpace();
        loginSession.RefreshToken.Should().NotBeNullOrWhiteSpace();
        loginSession.AccessTokenExpiresAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
        loginSession.RefreshTokenExpiresAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/human/identity/refresh");
        refreshRequest.Headers.Add("X-IIoT-Refresh-Token", loginSession.RefreshToken);

        using var refreshResponse = await _fixture.HttpClient.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshedSession = await ReadIssuedAuthSessionAsync(refreshResponse);
        refreshedSession.RefreshToken.Should().NotBe(loginSession.RefreshToken);
        refreshedSession.AccessToken.Should().NotBe(loginSession.AccessToken);

        using var staleRefreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/human/identity/refresh");
        staleRefreshRequest.Headers.Add("X-IIoT-Refresh-Token", loginSession.RefreshToken);

        using var staleRefreshResponse = await _fixture.HttpClient.SendAsync(staleRefreshRequest);
        staleRefreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EdgeBootstrap_DeviceInstanceAndRefresh_ShouldRotateRefreshToken()
    {
        await AuthenticateAsAdminAsync();
        var device = await CreateTestDeviceRegistrationAsync("bootstrap-refresh");
        _fixture.ClearAuthToken();

        using var bootstrapResponse = await _fixture.HttpClient.GetAsync(
            $"/api/v1/bootstrap/device-instance?clientCode={Uri.EscapeDataString(device.Code)}");

        bootstrapResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var bootstrapSession = await ReadIssuedBootstrapSessionAsync(bootstrapResponse);
        bootstrapSession.Device.ClientCode.Should().Be(device.Code);
        bootstrapSession.RefreshToken.Should().NotBeNullOrWhiteSpace();

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bootstrap/edge-refresh");
        refreshRequest.Headers.Add("X-IIoT-Refresh-Token", bootstrapSession.RefreshToken);

        using var refreshResponse = await _fixture.HttpClient.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshedBootstrapSession = await ReadIssuedBootstrapSessionAsync(refreshResponse);
        refreshedBootstrapSession.Device.Id.Should().Be(device.DeviceId);
        refreshedBootstrapSession.RefreshToken.Should().NotBe(bootstrapSession.RefreshToken);
        refreshedBootstrapSession.Device.UploadAccessToken.Should().NotBe(bootstrapSession.Device.UploadAccessToken);

        using var staleRefreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bootstrap/edge-refresh");
        staleRefreshRequest.Headers.Add("X-IIoT-Refresh-Token", bootstrapSession.RefreshToken);

        using var staleRefreshResponse = await _fixture.HttpClient.SendAsync(staleRefreshRequest);
        staleRefreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HumanIdentity_Login_ShouldReturnCorsHeaders_WithoutWildcardOrigin()
    {
        _fixture.ClearAuthToken();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/human/identity/login");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Content = JsonContent.Create(new
        {
            EmployeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
            Password = IIoTAppFixture.SeedAdminPassword
        });

        using var response = await _fixture.HttpClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("Access-Control-Allow-Origin");
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("http://localhost:5173");
        response.Headers.Should().ContainKey("Access-Control-Expose-Headers");
        response.Headers.GetValues("Access-Control-Expose-Headers").Single()
            .Should()
            .Contain("X-IIoT-Refresh-Token")
            .And.Contain("X-IIoT-Refresh-Token-Expires-At")
            .And.Contain("X-IIoT-Access-Token-Expires-At");
    }

    [Fact]
    public async Task DomainOutbox_DeviceRegistration_ShouldEventuallyBeMarkedProcessed()
    {
        await AuthenticateAsAdminAsync();

        var connectionString = await _fixture.GetConnectionStringAsync(ConnectionResourceNames.IiotDatabase);
        var device = await CreateTestDeviceRegistrationAsync("outbox-device");

        var outboxMessage = await EventuallyAsync(
            async () => await GetOutboxMessageAsync(
                connectionString,
                nameof(IIoT.Core.Production.Aggregates.Devices.Events.DeviceRegisteredDomainEvent),
                device.DeviceId),
            message => message is not null && message.ProcessedAtUtc is not null);

        outboxMessage.Should().NotBeNull();
        outboxMessage!.AttemptCount.Should().BeGreaterThan(0);
        outboxMessage.ProcessedAtUtc.Should().NotBeNull();
        outboxMessage.LastError.Should().BeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TimescaleDb_ShouldInitializeConfiguredHypertables()
    {
        var connectionString = await _fixture.GetConnectionStringAsync(ConnectionResourceNames.IiotDatabase);

        var hypertables = await GetHypertableNamesAsync(connectionString);

        hypertables.Should().Contain("device_logs");
        hypertables.Should().Contain("hourly_capacity");
        hypertables.Should().Contain("pass_data_injection");
        hypertables.Should().Contain("pass_data_stacking");
    }

    [Fact]
    public async Task HumanIdentity_Login_ShouldReturnUnifiedValidationErrors()
    {
        _fixture.ClearAuthToken();

        using var response = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/human/identity/login", new
        {
            EmployeeNo = "",
            Password = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var envelope = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(JsonOptions)
                       ?? throw new InvalidOperationException("Unable to deserialize validation envelope.");
        envelope.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HumanProtectedRoutes_ShouldRequireJwt()
    {
        _fixture.ClearAuthToken();

        using var employeeResponse = await _fixture.HttpClient.GetAsync("/api/v1/human/employees?PageNumber=1&PageSize=10");
        using var identityResponse = await _fixture.HttpClient.GetAsync("/api/v1/human/identity/roles");
        using var processResponse = await _fixture.HttpClient.GetAsync("/api/v1/human/master-data/processes/all");

        employeeResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        identityResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        processResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MasterDataProcess_HumanRoutes_ShouldReturnCreatedProcess()
    {
        await AuthenticateAsAdminAsync();

        var code = $"PROC-{Guid.NewGuid():N}"[..12];
        await PostJsonAsync<Guid>("/api/v1/human/master-data/processes", new
        {
            ProcessCode = code,
            ProcessName = $"{code}-name"
        });

        var processes = await GetFromJsonAsync<List<ProcessSelectDto>>("/api/v1/human/master-data/processes/all");

        processes.Should().Contain(x => x.ProcessCode == code);
    }

    [Fact]
    public async Task EdgeBootstrap_ShouldAllowAnonymous_AndResolveByCode()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("bootstrap");
        _fixture.ClearAuthToken();

        using var response = await _fixture.HttpClient.GetAsync(
            $"/api/v1/bootstrap/device-instance?clientCode={Uri.EscapeDataString(device.Code)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-IIoT-Route-Surface");
        response.Headers.GetValues("X-IIoT-Route-Surface").Should().ContainSingle("bootstrap");
        response.Headers.Should().NotContainKey("X-IIoT-Deprecated-Alias");
        response.Headers.Should().NotContainKey("X-IIoT-Replacement-Route");

        var edge = await response.Content.ReadFromJsonAsync<EdgeBootstrapDto>(JsonOptions)
                   ?? throw new InvalidOperationException("Unable to deserialize bootstrap response.");
        edge.Id.Should().Be(device.DeviceId);
        edge.ClientCode.Should().Be(device.Code);
        edge.UploadAccessToken.Should().NotBeNullOrWhiteSpace();
        edge.UploadAccessTokenExpiresAtUtc.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task LegacyEdgeBootstrapAlias_ShouldRemainCompatible()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("bootstrap-alias");
        _fixture.ClearAuthToken();

        using var response = await _fixture.HttpClient.GetAsync(
            $"/api/v1/edge/bootstrap/device-instance?clientCode={Uri.EscapeDataString(device.Code)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-IIoT-Route-Surface");
        response.Headers.GetValues("X-IIoT-Route-Surface").Should().ContainSingle("legacy-bootstrap-alias");
        response.Headers.Should().ContainKey("X-IIoT-Deprecated-Alias");
        response.Headers.GetValues("X-IIoT-Deprecated-Alias").Should().ContainSingle("true");
        response.Headers.Should().ContainKey("X-IIoT-Replacement-Route");
        response.Headers.GetValues("X-IIoT-Replacement-Route").Should().ContainSingle("/api/v1/bootstrap/device-instance");

        var edge = await response.Content.ReadFromJsonAsync<EdgeBootstrapDto>(JsonOptions)
                   ?? throw new InvalidOperationException("Unable to deserialize legacy bootstrap response.");

        edge.Id.Should().Be(device.DeviceId);
        edge.ClientCode.Should().Be(device.Code);
        edge.UploadAccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EdgeCapacity_NewRoutes_ShouldRequireJwt_AndWorkWithJwt()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("edge-capacity");
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var plcName = $"PLC-{Guid.NewGuid():N}"[..10];
        var request = new
        {
            DeviceId = device.DeviceId,
            Date = date,
            ShiftCode = "D",
            Hour = 10,
            Minute = 0,
            TimeLabel = "10:00",
            TotalCount = 12,
            OkCount = 11,
            NgCount = 1,
            PlcName = plcName
        };

        _fixture.ClearAuthToken();

        using (var unauthorized = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/edge/capacity/hourly", request))
        {
            unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await AuthenticateAsEdgeAsync(device.DeviceId);

        await PostJsonAsync("/api/v1/edge/capacity/hourly", request);

        var result = await EventuallyAsync(async () =>
            await GetFromJsonAsync<List<HourlyCapacityDto>>(
                $"/api/v1/edge/capacity/hourly?deviceId={device.DeviceId}&date={date:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}"),
            response => response.Count == 1 && response[0].TotalCount == 12);

        result.Should().ContainSingle();
        result[0].OkCount.Should().Be(11);
    }

    [Fact]
    public async Task EdgeRecipe_NewRoute_ShouldRequireJwt_AndWorkWithJwt()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("edge-recipe");
        var recipeName = $"recipe-{Guid.NewGuid():N}"[..14];

        await PostJsonAsync<Guid>("/api/v1/human/recipes", new
        {
            RecipeName = recipeName,
            Version = "V1.0.0",
            ProcessId = device.ProcessId,
            DeviceId = device.DeviceId,
            ParametersJsonb = "{\"speed\":120}",
            Status = 1
        });

        _fixture.ClearAuthToken();

        using (var unauthorized = await _fixture.HttpClient.GetAsync($"/api/v1/edge/recipes/device/{device.DeviceId}"))
        {
            unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        await AuthenticateAsEdgeAsync(device.DeviceId);

        var recipes = await EventuallyAsync(
            async () => await GetFromJsonAsync<List<RecipeForDeviceDto>>($"/api/v1/edge/recipes/device/{device.DeviceId}"),
            response => response.Any(x => x.RecipeName == recipeName));

        recipes.Should().Contain(x => x.RecipeName == recipeName && x.DeviceId == device.DeviceId);
    }

    [Fact]
    public async Task HumanCapacity_QueryRoutes_ShouldReturnComputedAggregates()
    {
        await AuthenticateAsAdminAsync();

        var deviceId = await CreateTestDeviceAsync("human-capacity");
        await AuthenticateAsEdgeAsync(deviceId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var plcName = $"PLC-{Guid.NewGuid():N}"[..10];

        await PostJsonAsync("/api/v1/edge/capacity/hourly", new
        {
            DeviceId = deviceId,
            Date = date,
            ShiftCode = "D",
            Hour = 11,
            Minute = 30,
            TimeLabel = "11:30",
            TotalCount = 20,
            OkCount = 18,
            NgCount = 2,
            PlcName = plcName
        });

        await AuthenticateAsAdminAsync();

        var humanHourly = await EventuallyAsync(
            async () => await GetFromJsonAsync<List<HourlyCapacityDto>>(
                $"/api/v1/human/capacity/hourly?deviceId={deviceId}&date={date:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}"),
            response => response.Count == 1 && response[0].TotalCount == 20);

        var humanSummary = await EventuallyAsync(
            async () => await GetFromJsonAsync<DailySummaryDto>(
                $"/api/v1/human/capacity/summary?deviceId={deviceId}&date={date:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}"),
            response => response.TotalCount == 20 && response.OkCount == 18 && response.NgCount == 2);
        var humanRange = await EventuallyAsync(
            async () => await GetFromJsonAsync<List<DailyRangeSummaryDto>>(
                $"/api/v1/human/capacity/summary/range?deviceId={deviceId}&startDate={date:yyyy-MM-dd}&endDate={date:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}"),
            response => response.Count == 1 && response[0].TotalCount == 20);

        humanHourly.Should().ContainSingle();
        humanSummary.TotalCount.Should().Be(20);
        humanSummary.OkCount.Should().Be(18);
        humanSummary.NgCount.Should().Be(2);
        humanRange.Should().ContainSingle();
        humanRange[0].TotalCount.Should().Be(20);
    }

    [Fact]
    public async Task EdgeDeviceLogAndPassStation_NewRoutes_ShouldRequireJwt()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("edge-protected");
        var completedTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-1), DateTimeKind.Utc);

        _fixture.ClearAuthToken();

        using (var deviceLogUnauthorized = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/edge/device-logs", new
               {
                   DeviceId = device.DeviceId,
                   Logs = new[]
                   {
                       new
                       {
                           Level = "INFO",
                           Message = $"edge-protected-{Guid.NewGuid():N}",
                           LogTime = completedTime
                       }
                   }
               }))
        {
            deviceLogUnauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var passStationUnauthorized = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/edge/pass-stations/injection/batch", new
               {
                   DeviceId = device.DeviceId,
                   Items = new[]
                   {
                       new
                       {
                           Barcode = $"BC-{Guid.NewGuid():N}"[..14],
                           CellResult = "OK",
                           CompletedTime = completedTime,
                           PreInjectionTime = completedTime.AddSeconds(-15),
                           PreInjectionWeight = 12.34m,
                           PostInjectionTime = completedTime.AddSeconds(-3),
                           PostInjectionWeight = 13.21m,
                           InjectionVolume = 0.87m
                       }
                   }
               }))
        {
            passStationUnauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var stackingUnauthorized = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/edge/pass-stations/stacking", new
               {
                   DeviceId = device.DeviceId,
                   Item = new
                   {
                       Barcode = $"ST-{Guid.NewGuid():N}"[..14],
                       TrayCode = "TRAY-EDGE-01",
                       LayerCount = 8,
                       SequenceNo = 3,
                       CellResult = "Unknown",
                       CompletedTime = completedTime
                   }
               }))
        {
            stackingUnauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Bootstrap_EdgeLogin_ShouldReturnHumanJwt_ThatCannotAccessEdgeRoutes()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("edge-login");
        _fixture.ClearAuthToken();

        using var response = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/bootstrap/edge-login", new
        {
            EmployeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
            Password = IIoTAppFixture.SeedAdminPassword,
            DeviceId = device.DeviceId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-IIoT-Route-Surface");
        response.Headers.GetValues("X-IIoT-Route-Surface").Should().ContainSingle("bootstrap");
        response.Headers.Should().NotContainKey("X-IIoT-Deprecated-Alias");
        response.Headers.Should().NotContainKey("X-IIoT-Replacement-Route");
        var token = await ReadJwtTokenAsync(response);
        token.Should().NotBeNullOrWhiteSpace();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(claim => claim.Type == "actor_type" && claim.Value == "human-user");
        jwt.Claims.Should().NotContain(claim => claim.Type == "device_id");

        _fixture.SetAuthToken(token!);
        using var edgeResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/edge/device-logs", new
        {
            DeviceId = device.DeviceId,
            Logs = new[]
            {
                new
                {
                    Level = "INFO",
                    Message = $"human-jwt-{Guid.NewGuid():N}",
                    LogTime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
                }
            }
        });

        edgeResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LegacyHumanEdgeLoginAlias_ShouldRemainCompatible()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("edge-login-alias");
        _fixture.ClearAuthToken();

        using var response = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/human/identity/edge-login", new
        {
            EmployeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
            Password = IIoTAppFixture.SeedAdminPassword,
            DeviceId = device.DeviceId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("X-IIoT-Route-Surface");
        response.Headers.GetValues("X-IIoT-Route-Surface").Should().ContainSingle("legacy-edge-login-alias");
        response.Headers.Should().ContainKey("X-IIoT-Deprecated-Alias");
        response.Headers.GetValues("X-IIoT-Deprecated-Alias").Should().ContainSingle("true");
        response.Headers.Should().ContainKey("X-IIoT-Replacement-Route");
        response.Headers.GetValues("X-IIoT-Replacement-Route").Should().ContainSingle("/api/v1/bootstrap/edge-login");
        var token = await ReadJwtTokenAsync(response);
        token.Should().NotBeNullOrWhiteSpace();
    }

    private async Task AuthenticateAsAdminAsync()
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/human/identity/login", new
        {
            EmployeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
            Password = IIoTAppFixture.SeedAdminPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = await ReadJwtTokenAsync(response);
        token.Should().NotBeNullOrWhiteSpace();

        _fixture.SetAuthToken(token!);
    }

    private async Task AuthenticateAsEdgeAsync(Guid deviceId)
    {
        _fixture.ClearAuthToken();

        _deviceCodes.TryGetValue(deviceId, out var code).Should().BeTrue($"device code for {deviceId} should be tracked during test setup");
        var bootstrap = await GetFromJsonAsync<EdgeBootstrapDto>(
            $"/api/v1/bootstrap/device-instance?clientCode={Uri.EscapeDataString(code!)}");

        bootstrap.Id.Should().Be(deviceId);
        bootstrap.UploadAccessToken.Should().NotBeNullOrWhiteSpace();
        _fixture.SetAuthToken(bootstrap.UploadAccessToken);
    }

    private static async Task<string> ReadJwtTokenAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();

        try
        {
            var token = JsonSerializer.Deserialize<string>(body, JsonOptions);
            if (!string.IsNullOrWhiteSpace(token))
                return token;
        }
        catch (JsonException)
        {
        // 部分宿主会直接返回 text/plain 的 JWT，而不是 JSON 字符串。
        }

        return body.Trim();
    }

    private static async Task<IssuedAuthSession> ReadIssuedAuthSessionAsync(HttpResponseMessage response)
    {
        var token = await ReadJwtTokenAsync(response);
        var refreshToken = response.Headers.GetValues("X-IIoT-Refresh-Token").Single();
        var refreshTokenExpiresAtUtc = DateTimeOffset.Parse(
            response.Headers.GetValues("X-IIoT-Refresh-Token-Expires-At").Single());
        var accessTokenExpiresAtUtc = DateTimeOffset.Parse(
            response.Headers.GetValues("X-IIoT-Access-Token-Expires-At").Single());

        return new IssuedAuthSession(
            token,
            refreshToken,
            accessTokenExpiresAtUtc,
            refreshTokenExpiresAtUtc);
    }

    private static async Task<IssuedBootstrapSession> ReadIssuedBootstrapSessionAsync(HttpResponseMessage response)
    {
        var device = await response.Content.ReadFromJsonAsync<EdgeBootstrapDto>(JsonOptions)
                     ?? throw new InvalidOperationException("Unable to deserialize bootstrap response.");
        var refreshToken = response.Headers.GetValues("X-IIoT-Refresh-Token").Single();
        var refreshTokenExpiresAtUtc = DateTimeOffset.Parse(
            response.Headers.GetValues("X-IIoT-Refresh-Token-Expires-At").Single());
        var accessTokenExpiresAtUtc = DateTimeOffset.Parse(
            response.Headers.GetValues("X-IIoT-Access-Token-Expires-At").Single());

        return new IssuedBootstrapSession(
            device,
            refreshToken,
            accessTokenExpiresAtUtc,
            refreshTokenExpiresAtUtc);
    }

    private async Task<Guid> CreateTestDeviceAsync(string prefix)
    {
        var device = await CreateTestDeviceRegistrationAsync(prefix);
        return device.DeviceId;
    }

    private async Task<TestDeviceRegistration> CreateTestDeviceRegistrationAsync(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var processId = await CreateProcessAsync($"{prefix.ToUpperInvariant()}-{suffix}");

        var created = await PostJsonAsync<CreateDeviceResultDto>("/api/v1/human/devices", new
        {
            DeviceName = $"{prefix}-device-{suffix}",
            ProcessId = processId
        });

        created.Id.Should().NotBe(Guid.Empty);
        created.Code.Should().StartWith("DEV-");
        created.Code.Should().NotBeNullOrWhiteSpace();
        _deviceCodes[created.Id] = created.Code;

        return new TestDeviceRegistration(created.Id, processId, created.Code);
    }

    private async Task<Guid> CreateProcessAsync(string code)
    {
        var processId = await PostJsonAsync<Guid>("/api/v1/human/master-data/processes", new
        {
            ProcessCode = code,
            ProcessName = $"{code}-name"
        });

        processId.Should().NotBe(Guid.Empty);
        return processId;
    }

    private async Task PostJsonAsync(string path, object request)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(path, request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"{path} failed: {body}");
    }

    private async Task<T> PostJsonAsync<T>(string path, object request)
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync(path, request);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"{path} failed: {body}");

        if (typeof(T) == typeof(Guid))
        {
            var value = JsonSerializer.Deserialize<Guid>(body, JsonOptions);
            return (T)(object)value;
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions)
               ?? throw new InvalidOperationException($"Unable to deserialize response from {path}.");
    }

    private async Task<T> GetFromJsonAsync<T>(string path)
    {
        using var response = await _fixture.HttpClient.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"{path} failed: {body}");

        return JsonSerializer.Deserialize<T>(body, JsonOptions)
               ?? throw new InvalidOperationException($"Unable to deserialize response from {path}.");
    }

    private static async Task PublishInvalidDeviceLogEventAsync(string connectionString)
    {
        var bus = Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            cfg.Host(connectionString);
        });

        await bus.StartAsync();
        try
        {
            await bus.Publish(new DeviceLogReceivedEvent
            {
                DeviceId = Guid.NewGuid(),
                Logs =
                [
                    new DeviceLogItem
                    {
                        Level = "ERROR",
                        Message = $"invalid-device-{Guid.NewGuid():N}",
                        LogTime = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
                    }
                ]
            });
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    private static async Task<uint> GetQueueMessageCountAsync(string connectionString, string queueName)
    {
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connectionString)
        };

        try
        {
            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            var queue = await channel.QueueDeclarePassiveAsync(queueName);
            return queue.MessageCount;
        }
        catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == 404)
        {
            return 0;
        }
    }

    private static async Task<T> EventuallyAsync<T>(
        Func<Task<T>> action,
        Func<T, bool> predicate)
    {
        var deadline = DateTime.UtcNow + EventuallyTimeout;
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await action();
                if (predicate(result))
                    return result;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(EventuallyInterval);
        }

        if (lastException is not null)
            throw lastException;

        throw new TimeoutException("Condition was not satisfied before timeout.");
    }

    private static async Task<List<StackingPassRow>> GetStackingPassRowsAsync(
        string connectionString,
        Guid deviceId,
        string barcode)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            select tray_code, layer_count, sequence_no, cell_result, completed_time
            from pass_data_stacking
            where device_id = @deviceId and barcode = @barcode
            order by completed_time desc
            """,
            connection);
        command.Parameters.AddWithValue("deviceId", deviceId);
        command.Parameters.AddWithValue("barcode", barcode);

        var rows = new List<StackingPassRow>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new StackingPassRow(
                TrayCode: reader.GetString(0),
                LayerCount: reader.GetInt32(1),
                SequenceNo: reader.GetInt32(2),
                CellResult: reader.GetString(3),
                CompletedTime: reader.GetDateTime(4)));
        }

        return rows;
    }

    private static async Task<OutboxMessageRow?> GetOutboxMessageAsync(
        string connectionString,
        string eventName,
        Guid aggregateId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            select "Id", attempt_count, processed_at_utc, last_error
            from outbox_messages
            where position(@eventName in event_type) > 0
              and payload ->> 'deviceId' = @aggregateId
            order by occurred_at_utc desc
            limit 1
            """,
            connection);
        command.Parameters.AddWithValue("eventName", eventName);
        command.Parameters.AddWithValue("aggregateId", aggregateId.ToString());

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new OutboxMessageRow(
            Id: reader.GetGuid(0),
            AttemptCount: reader.GetInt32(1),
            ProcessedAtUtc: reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
            LastError: reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task<HashSet<string>> GetHypertableNamesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            select hypertable_name
            from timescaledb_information.hypertables
            """,
            connection);

        var result = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}

public sealed record PagedResponse<T>
{
    public List<T> Items { get; init; } = [];
    public PagedMetaData MetaData { get; init; } = new();
}

public sealed record PagedMetaData
{
    public long TotalCount { get; init; }
    public int PageSize { get; init; }
    public int CurrentPage { get; init; }
    public int TotalPages { get; init; }
}

public sealed record DeviceLogListItemDto(
    Guid Id,
    Guid DeviceId,
    string DeviceName,
    string Level,
    string Message,
    DateTime LogTime,
    DateTime ReceivedAt);

public sealed record HourlyCapacityDto(
    int Hour,
    int Minute,
    string TimeLabel,
    string ShiftCode,
    int TotalCount,
    int OkCount,
    int NgCount);

public sealed record DailySummaryDto(
    int TotalCount,
    int OkCount,
    int NgCount,
    int DayShiftTotal,
    int DayShiftOk,
    int DayShiftNg,
    int NightShiftTotal,
    int NightShiftOk,
    int NightShiftNg);

public sealed record DailyRangeSummaryDto(
    DateOnly Date,
    int TotalCount,
    int OkCount,
    int NgCount,
    int DayShiftTotal,
    int DayShiftOk,
    int DayShiftNg,
    int NightShiftTotal,
    int NightShiftOk,
    int NightShiftNg);

public sealed record InjectionPassListItemDto(
    Guid Id,
    Guid DeviceId,
    string Barcode,
    string CellResult,
    DateTime PreInjectionTime,
    decimal PreInjectionWeight,
    DateTime PostInjectionTime,
    decimal PostInjectionWeight,
    decimal InjectionVolume,
    DateTime CompletedTime,
    DateTime ReceivedAt);

public sealed record StackingPassRow(
    string TrayCode,
    int LayerCount,
    int SequenceNo,
    string CellResult,
    DateTime CompletedTime);

public sealed record OutboxMessageRow(
    Guid Id,
    int AttemptCount,
    DateTimeOffset? ProcessedAtUtc,
    string? LastError);

public sealed record PermissionGroupDto(
    string GroupName,
    List<string> Permissions);

public sealed record ErrorEnvelope(
    List<string> Errors);

public sealed record EdgeBootstrapDto(
    Guid Id,
    string DeviceName,
    string ClientCode,
    Guid ProcessId,
    string UploadAccessToken,
    DateTimeOffset UploadAccessTokenExpiresAtUtc);

public sealed record CreateDeviceResultDto(
    Guid Id,
    string Code);

public sealed record ProcessSelectDto(
    Guid Id,
    string ProcessCode,
    string ProcessName);

public sealed record RecipeForDeviceDto(
    Guid Id,
    string RecipeName,
    string Version,
    Guid ProcessId,
    Guid DeviceId,
    string ParametersJsonb,
    string Status);

public sealed record TestDeviceRegistration(
    Guid DeviceId,
    Guid ProcessId,
    string Code);

public sealed record IssuedAuthSession(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset RefreshTokenExpiresAtUtc);

public sealed record IssuedBootstrapSession(
    EdgeBootstrapDto Device,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset RefreshTokenExpiresAtUtc);
