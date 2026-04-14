using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using IIoT.Services.Common.Events.DeviceLogs;
using MassTransit;
using RabbitMQ.Client;
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

        await PostJsonAsync("/api/v1/DeviceLog", request);
        await PostJsonAsync("/api/v1/DeviceLog", request);

        var result = await EventuallyAsync(async () =>
            await GetFromJsonAsync<PagedResponse<DeviceLogListItemDto>>(
                $"/api/v1/DeviceLog/by-time-range?PageNumber=1&PageSize=20&deviceId={deviceId}" +
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

        await PostJsonAsync("/api/v1/Capacity/hourly", first);
        await PostJsonAsync("/api/v1/Capacity/hourly", second);

        var result = await EventuallyAsync(async () =>
            await GetFromJsonAsync<List<HourlyCapacityDto>>(
                $"/api/v1/Capacity/hourly?deviceId={deviceId}&date={date:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}"),
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

        await PostJsonAsync("/api/v1/PassStation/injection/batch", request);
        await PostJsonAsync("/api/v1/PassStation/injection/batch", request);

        var result = await EventuallyAsync(async () =>
            await GetFromJsonAsync<PagedResponse<InjectionPassListItemDto>>(
                $"/api/v1/PassStation/injection/by-device-time?PageNumber=1&PageSize=20&deviceId={deviceId}" +
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
    public async Task HumanIdentity_NewRoutes_ShouldMatchLegacyAliases()
    {
        await AuthenticateAsAdminAsync();

        var legacyRoles = await GetFromJsonAsync<List<string>>("/api/v1/Identity/roles");
        var humanRoles = await GetFromJsonAsync<List<string>>("/api/v1/human/identity/roles");
        var legacyPermissions = await GetFromJsonAsync<List<PermissionGroupDto>>("/api/v1/Identity/permissions/all");
        var humanPermissions = await GetFromJsonAsync<List<PermissionGroupDto>>("/api/v1/human/identity/permissions");

        humanRoles.Should().BeEquivalentTo(legacyRoles);
        humanPermissions.Should().BeEquivalentTo(legacyPermissions);
    }

    [Fact]
    public async Task MasterDataProcess_NewRoutes_ShouldMatchLegacyAliases()
    {
        await AuthenticateAsAdminAsync();

        var code = $"PROC-{Guid.NewGuid():N}"[..12];
        await PostJsonAsync<Guid>("/api/v1/human/master-data/processes", new
        {
            ProcessCode = code,
            ProcessName = $"{code}-name"
        });

        var legacy = await GetFromJsonAsync<List<ProcessSelectDto>>("/api/v1/MfgProcess/all");
        var human = await GetFromJsonAsync<List<ProcessSelectDto>>("/api/v1/human/master-data/processes/all");

        human.Should().BeEquivalentTo(legacy);
        human.Should().Contain(x => x.ProcessCode == code);
    }

    [Fact]
    public async Task EdgeBootstrap_NewAndLegacyRoutes_ShouldReturnSameDeviceIdentity()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("bootstrap");
        _fixture.ClearAuthToken();

        var legacy = await GetFromJsonAsync<DeviceIdentityDto>(
            $"/api/v1/Device/instance?macAddress={Uri.EscapeDataString(device.MacAddress)}&clientCode={Uri.EscapeDataString(device.ClientCode)}");
        var edge = await GetFromJsonAsync<DeviceIdentityDto>(
            $"/api/v1/edge/bootstrap/device-instance?macAddress={Uri.EscapeDataString(device.MacAddress)}&clientCode={Uri.EscapeDataString(device.ClientCode)}");

        edge.Should().BeEquivalentTo(legacy);
        edge.Id.Should().Be(device.DeviceId);
    }

    [Fact]
    public async Task EdgeCapacity_NewRoutes_ShouldWorkWithoutJwt()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("edge-capacity");
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var plcName = $"PLC-{Guid.NewGuid():N}"[..10];

        _fixture.ClearAuthToken();

        await PostJsonAsync("/api/v1/edge/capacity/hourly", new
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
        });

        var result = await EventuallyAsync(async () =>
            await GetFromJsonAsync<List<HourlyCapacityDto>>(
                $"/api/v1/edge/capacity/hourly?deviceId={device.DeviceId}&date={date:yyyy-MM-dd}&plcName={Uri.EscapeDataString(plcName)}"),
            response => response.Count == 1 && response[0].TotalCount == 12);

        result.Should().ContainSingle();
        result[0].OkCount.Should().Be(11);
    }

    [Fact]
    public async Task EdgeRecipe_NewRoute_ShouldWorkWithoutJwt()
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

        var recipes = await EventuallyAsync(
            async () => await GetFromJsonAsync<List<RecipeForDeviceDto>>($"/api/v1/edge/recipes/device/{device.DeviceId}"),
            response => response.Any(x => x.RecipeName == recipeName));

        recipes.Should().Contain(x => x.RecipeName == recipeName && x.DeviceId == device.DeviceId);
    }

    [Fact]
    public async Task HumanIdentity_EdgeLogin_NewRoute_ShouldReturnJwt()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("edge-login");
        _fixture.ClearAuthToken();

        using var response = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/human/identity/edge-login", new
        {
            EmployeeNo = "101650",
            Password = "Ljh123456!",
            DeviceId = device.DeviceId
        });

        var token = await response.Content.ReadFromJsonAsync<string>(JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        token.Should().NotBeNullOrWhiteSpace();
    }

    private async Task AuthenticateAsAdminAsync()
    {
        using var response = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/Identity/login", new
        {
            EmployeeNo = "101650",
            Password = "Ljh123456!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = await response.Content.ReadFromJsonAsync<string>(JsonOptions);
        token.Should().NotBeNullOrWhiteSpace();

        _fixture.SetAuthToken(token!);
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
        var macAddress = BuildMacAddress(suffix);
        var clientCode = $"CLIENT-{suffix}";

        var deviceId = await PostJsonAsync<Guid>("/api/v1/Device", new
        {
            DeviceName = $"{prefix}-device-{suffix}",
            MacAddress = macAddress,
            ClientCode = clientCode,
            ProcessId = processId
        });

        deviceId.Should().NotBe(Guid.Empty);

        return new TestDeviceRegistration(deviceId, processId, macAddress, clientCode);
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

    private static string BuildMacAddress(string seed)
    {
        var hex = seed.ToUpperInvariant().PadLeft(12, '0')[..12];
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
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

        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var queue = await channel.QueueDeclarePassiveAsync(queueName);
        return queue.MessageCount;
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
    string MacAddress,
    string ClientCode);
