using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IIoT.ProductionService.Security;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Configuration;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace IIoT.CloudPlatform.IntegrationTestKit;

public sealed class CloudTestDriver(IIoTAppFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, TestDeviceRegistration> _devices = [];

    public IIoTAppFixture Fixture => fixture;

    public async Task AuthenticateAsAdminAsync()
    {
        using var response = await fixture.HttpClient.PostAsJsonAsync("/api/v1/human/identity/login", new
        {
            EmployeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
            Password = IIoTAppFixture.SeedAdminPassword
        });

        await EnsureSuccessAsync(response, "admin login");
        fixture.SetAuthToken(await ReadJwtTokenAsync(response));
    }

    public async Task AuthenticateAsEdgeAsync(Guid deviceId)
    {
        fixture.ClearAuthToken();
        using var request = CreateBootstrapRequestForDevice(deviceId);
        using var response = await fixture.HttpClient.SendAsync(request);
        await EnsureSuccessAsync(response, $"edge bootstrap for {deviceId}");

        var bootstrap = await response.Content.ReadFromJsonAsync<DriverEdgeBootstrapDto>(JsonOptions)
                        ?? throw new InvalidOperationException("Unable to deserialize bootstrap response.");
        if (bootstrap.Id != deviceId || string.IsNullOrWhiteSpace(bootstrap.UploadAccessToken))
            throw new InvalidOperationException($"Edge bootstrap returned an invalid identity for {deviceId}.");

        fixture.SetAuthToken(bootstrap.UploadAccessToken);
    }

    public async Task<string> IssueEdgeUploadAccessTokenAsync(Guid deviceId)
    {
        fixture.ClearAuthToken();
        using var request = CreateBootstrapRequestForDevice(deviceId);
        using var response = await fixture.HttpClient.SendAsync(request);
        await EnsureSuccessAsync(response, $"edge bootstrap for {deviceId}");
        var bootstrap = await response.Content.ReadFromJsonAsync<DriverEdgeBootstrapDto>(JsonOptions)
                        ?? throw new InvalidOperationException("Unable to deserialize bootstrap response.");
        return string.IsNullOrWhiteSpace(bootstrap.UploadAccessToken)
            ? throw new InvalidOperationException("Edge bootstrap did not return an upload access token.")
            : bootstrap.UploadAccessToken;
    }

    public async Task<TestDeviceRegistration> CreateTestDeviceRegistrationAsync(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var processId = await PostJsonAsync<Guid>("/api/v1/human/master-data/processes", new
        {
            ProcessCode = $"{prefix.ToUpperInvariant()}-{suffix}",
            ProcessName = $"{prefix}-{suffix}-name"
        });
        var created = await PostJsonAsync<CreateDeviceResultDto>("/api/v1/human/devices", new
        {
            DeviceName = $"{prefix}-device-{suffix}",
            ProcessId = processId
        });

        if (created.Id == Guid.Empty || string.IsNullOrWhiteSpace(created.Code))
            throw new InvalidOperationException("Cloud returned an invalid test device identity.");

        var bootstrapSecret = BootstrapSecretGenerator.Generate();
        var bootstrapSecretHash = BootstrapSecretHasher.Hash(bootstrapSecret);
        var connectionString = await fixture.GetConnectionStringAsync(ConnectionResourceNames.IiotDatabase);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "update devices set bootstrap_secret_hash = @hash where id = @id",
            connection);
        command.Parameters.AddWithValue("hash", bootstrapSecretHash);
        command.Parameters.AddWithValue("id", created.Id);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException($"Unable to set bootstrap secret for {created.Id}.");

        var device = new TestDeviceRegistration(created.Id, processId, created.Code, bootstrapSecret);
        _devices.Add(device.DeviceId, device);
        return device;
    }

    public HttpRequestMessage CreateBootstrapRequestForDevice(
        Guid deviceId,
        string? bootstrapSecret = null,
        string path = "/api/v1/bootstrap/device-instance")
    {
        if (!_devices.TryGetValue(deviceId, out var device))
            throw new InvalidOperationException($"Test device {deviceId} is not registered in this driver.");
        return CreateBootstrapRequest(device, bootstrapSecret, path);
    }

    public static HttpRequestMessage CreateBootstrapRequest(
        TestDeviceRegistration device,
        string? bootstrapSecret = null,
        string path = "/api/v1/bootstrap/device-instance") =>
        CreateBootstrapRequest(device.Code, bootstrapSecret ?? device.BootstrapSecret, path);

    public static HttpRequestMessage CreateBootstrapRequest(
        string clientCode,
        string bootstrapSecret,
        string path = "/api/v1/bootstrap/device-instance")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{path}?clientCode={Uri.EscapeDataString(clientCode)}");
        request.Headers.Add("X-IIoT-Bootstrap-Secret", bootstrapSecret);
        return request;
    }

    public async Task PostJsonAsync(string path, object request)
    {
        using var response = await fixture.HttpClient.PostAsJsonAsync(path, request);
        await EnsureSuccessAsync(response, path);
    }

    public async Task<T> PostJsonAsync<T>(string path, object request)
    {
        using var response = await fixture.HttpClient.PostAsJsonAsync(path, request);
        await EnsureSuccessAsync(response, path);
        var body = await response.Content.ReadAsStringAsync();
        if (typeof(T) == typeof(Guid))
            return (T)(object)JsonSerializer.Deserialize<Guid>(body, JsonOptions);
        return JsonSerializer.Deserialize<T>(body, JsonOptions)
               ?? throw new InvalidOperationException($"Unable to deserialize response from {path}.");
    }

    public static async Task<string> ReadJwtTokenAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
            throw new InvalidOperationException("Authentication response did not contain a JWT.");
        try
        {
            var token = JsonSerializer.Deserialize<string>(body, JsonOptions);
            if (!string.IsNullOrWhiteSpace(token))
                return token;
        }
        catch (JsonException)
        {
            // Some hosts return the JWT as text/plain.
        }
        return body.Trim();
    }

    public static string CreateAiReadToken(
        IEnumerable<string> permissions,
        IReadOnlyCollection<Guid>? delegatedDeviceIds = null,
        Guid? delegatedUserId = null,
        string? rawDelegatedUserId = null,
        IReadOnlyCollection<string>? rawDelegatedDeviceIds = null)
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

        var effectiveDelegatedUserId = delegatedUserId;
        if (!effectiveDelegatedUserId.HasValue && delegatedDeviceIds is not null &&
            rawDelegatedUserId is null && rawDelegatedDeviceIds is null)
            effectiveDelegatedUserId = Guid.NewGuid();

        if (rawDelegatedUserId is not null)
            claims.Add(new Claim(IIoTClaimTypes.DelegatedUserId, rawDelegatedUserId));
        else if (effectiveDelegatedUserId.HasValue)
            claims.Add(new Claim(IIoTClaimTypes.DelegatedUserId, effectiveDelegatedUserId.Value.ToString()));

        if (rawDelegatedDeviceIds is not null)
            claims.AddRange(rawDelegatedDeviceIds.Select(id => new Claim(IIoTClaimTypes.DelegatedDeviceId, id)));
        else if (delegatedDeviceIds is not null)
            claims.AddRange(delegatedDeviceIds.Select(id => new Claim(IIoTClaimTypes.DelegatedDeviceId, id.ToString())));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(IIoTAppFixture.TestJwtSecret));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "IIoT.CloudPlatform",
            audience: "IIoT.WpfClient",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"Cloud test operation '{operation}' failed with {(int)response.StatusCode} {response.StatusCode}: {body}");
    }

    private sealed record DriverEdgeBootstrapDto(Guid Id, string UploadAccessToken);
    private sealed record CreateDeviceResultDto(Guid Id, string Code);
}

public sealed record TestDeviceRegistration(
    Guid DeviceId,
    Guid ProcessId,
    string Code,
    string BootstrapSecret);
