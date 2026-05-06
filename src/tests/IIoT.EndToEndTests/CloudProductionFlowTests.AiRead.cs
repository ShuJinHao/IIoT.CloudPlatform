using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Configuration;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace IIoT.EndToEndTests;

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
        }

        await AuthenticateAsAdminAsync();
        using (var human = await _fixture.HttpClient.GetAsync("/api/v1/ai/read/devices"))
        {
            human.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
    public async Task AiReadServiceAccount_ShouldReadFourReadOnlySurfaces()
    {
        await AuthenticateAsAdminAsync();

        var device = await CreateTestDeviceRegistrationAsync("ai-read-flow");
        await AuthenticateAsEdgeAsync(device.DeviceId);
        var completedTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-2), DateTimeKind.Utc);
        var date = DateOnly.FromDateTime(completedTime);
        var message = $"ai-read-log-{Guid.NewGuid():N}";
        var barcode = $"AI-{Guid.NewGuid():N}"[..14];
        var plcName = $"AI-{Guid.NewGuid():N}"[..10];

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
                AiReadPermissions.PassStation
            ],
            [device.DeviceId]));

        var devices = await GetFromJsonAsync<AiReadListResponseDto<AiReadDeviceDto>>("/api/v1/ai/read/devices");
        devices.Items.Should().ContainSingle(x => x.Id == device.DeviceId);

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

        var passStations = await EventuallyAsync(
            async () => await GetFromJsonAsync<AiReadListResponseDto<AiReadPassStationDto>>(
                $"/api/v1/ai/read/pass-stations/injection?deviceId={device.DeviceId}&startTime={startTime}&endTime={endTime}&barcode={Uri.EscapeDataString(barcode)}"),
            response => response.Items.Any(x => x.Barcode == barcode));
        var passStation = passStations.Items.Single(x => x.Barcode == barcode);
        passStations.Source.Should().Be("pass_station_records:injection");
        passStation.Fields.Should().ContainKey("injectionVolume");
        passStation.Fields.Should().NotContainKey("notConfigured");
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
        Guid? delegatedUserId = null)
    {
        var subjectId = Guid.NewGuid();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subjectId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, "ai-read-e2e"),
            new(ClaimTypes.NameIdentifier, subjectId.ToString()),
            new(ClaimTypes.Name, "ai-read-e2e"),
            new(IIoTClaimTypes.ActorType, IIoTClaimTypes.AiServiceActor),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        claims.AddRange(permissions.Select(permission => new Claim(IIoTClaimTypes.Permission, permission)));

        if (delegatedUserId.HasValue)
        {
            claims.Add(new Claim(IIoTClaimTypes.DelegatedUserId, delegatedUserId.Value.ToString()));
        }

        if (delegatedDeviceIds is not null)
        {
            claims.AddRange(delegatedDeviceIds.Select(deviceId =>
                new Claim(IIoTClaimTypes.DelegatedDeviceId, deviceId.ToString())));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(IIoTAppFixture.TestJwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "IIoT.CloudPlatform",
            audience: "IIoT.WpfClient",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

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
    string DeviceName,
    Guid ProcessId);

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

public sealed record AiReadPassStationDto(
    Guid Id,
    Guid DeviceId,
    string? Barcode,
    string? CellResult,
    DateTime? CompletedTime,
    DateTime? ReceivedAt,
    Dictionary<string, JsonElement> Fields);

public sealed record AiReadAuditRow(
    bool Succeeded,
    string Summary);
