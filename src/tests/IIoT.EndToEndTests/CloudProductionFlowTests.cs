using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IIoT.Services.Common.Events.DeviceLogs;
using MassTransit;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace IIoT.EndToEndTests;

public sealed class CloudProductionFlowTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan EventuallyTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan EventuallyInterval = TimeSpan.FromMilliseconds(500);

    private readonly IIoTAppFixture _fixture = new();

    public Task InitializeAsync() => _fixture.StartAsync();

    public Task DisposeAsync() => _fixture.DisposeAsync().AsTask();

    [Fact]
    public async Task DeviceLogs_DuplicateConsume_ShouldPersistOneRow()
    {
        await AuthenticateAsAdminAsync();

        var deviceId = await CreateTestDeviceAsync("log");
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

        var result = await EventuallyAsync(async () =>
            await GetFromJsonAsync<PagedResponse<InjectionPassListItemDto>>(
                $"/api/v1/human/pass-stations/injection/by-device-time?PageNumber=1&PageSize=20&deviceId={deviceId}" +
                $"&startTime={Uri.EscapeDataString(completedTime.AddMinutes(-1).ToString("O"))}" +
                $"&endTime={Uri.EscapeDataString(completedTime.AddMinutes(1).ToString("O"))}"),
            response => response.Items.Count(x => x.Barcode == barcode) == 1);

        result.Items.Should().ContainSingle(x => x.Barcode == barcode);
    }

    [Fact]
    public async Task DeviceLogConsumer_RepeatedFailures_ShouldMoveMessageToErrorQueue()
    {
        await AuthenticateAsAdminAsync();

        var connectionString = await _fixture.GetConnectionStringAsync("eventbus");
        var queueName = "device-log_error";
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

        var edge = await GetFromJsonAsync<DeviceIdentityDto>(
            $"/api/v1/edge/bootstrap/device-instance?clientCode={Uri.EscapeDataString(device.Code)}");
        edge.Id.Should().Be(device.DeviceId);
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

        await AuthenticateAsAdminAsync();

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

        await AuthenticateAsAdminAsync();

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
    }

    [Fact]
    public async Task HumanIdentity_EdgeLogin_NewRoute_ShouldReturnJwt()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("edge-login");
        _fixture.ClearAuthToken();

        using var response = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/human/identity/edge-login", new
        {
            EmployeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
            Password = IIoTAppFixture.SeedAdminPassword,
            DeviceId = device.DeviceId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
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
            // Some hosts return JWTs as text/plain instead of a JSON string.
        }

        return body.Trim();
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

public sealed record PermissionGroupDto(
    string GroupName,
    List<string> Permissions);

public sealed record DeviceIdentityDto(
    Guid Id,
    string DeviceName,
    Guid ProcessId);

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
