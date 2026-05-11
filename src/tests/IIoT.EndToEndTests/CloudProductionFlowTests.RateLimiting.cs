using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;

namespace IIoT.EndToEndTests;

public sealed partial class CloudProductionFlowTests
{
    [Fact]
    public async Task HumanIdentity_Login_ShouldReturnTooManyRequestsAfterTenthRequestWithinWindow()
    {
        _fixture.ClearAuthToken();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/human/identity/login", new
            {
                EmployeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
                Password = IIoTAppFixture.SeedAdminPassword
            });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var throttledResponse = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/human/identity/login", new
        {
            EmployeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
            Password = IIoTAppFixture.SeedAdminPassword
        });

        throttledResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task HumanIdentity_Refresh_ShouldNotConsumePasswordLoginBudget()
    {
        _fixture.ClearAuthToken();

        IssuedAuthSession? lastSession = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await _fixture.HttpClient.PostAsJsonAsync("/api/v1/human/identity/login", new
            {
                EmployeeNo = IIoTAppFixture.SeedAdminEmployeeNo,
                Password = IIoTAppFixture.SeedAdminPassword
            });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            lastSession = await ReadIssuedAuthSessionAsync(response);
        }

        lastSession.Should().NotBeNull();

        using var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/human/identity/refresh");
        refreshRequest.Headers.Add("X-IIoT-Refresh-Token", lastSession!.RefreshToken);

        using var refreshResponse = await _fixture.HttpClient.SendAsync(refreshRequest);

        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EdgeCapacity_Upload_ShouldUseSeparateDeviceBucketsForSameRemoteIp()
    {
        await AuthenticateAsAdminAsync();

        var firstDevice = await CreateTestDeviceRegistrationAsync("rate-limit-a");
        var secondDevice = await CreateTestDeviceRegistrationAsync("rate-limit-b");
        var firstToken = await IssueEdgeUploadAccessTokenAsync(firstDevice.DeviceId);
        var secondToken = await IssueEdgeUploadAccessTokenAsync(secondDevice.DeviceId);
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        for (var attempt = 0; attempt < 80; attempt++)
        {
            using var firstResponse = await PostEdgeCapacityAsync(
                firstToken,
                firstDevice.DeviceId,
                date,
                "PLC-RATE-A");
            firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            using var secondResponse = await PostEdgeCapacityAsync(
                secondToken,
                secondDevice.DeviceId,
                date,
                "PLC-RATE-B");
            secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    private async Task<string> IssueEdgeUploadAccessTokenAsync(Guid deviceId)
    {
        _fixture.ClearAuthToken();

        _deviceCodes.TryGetValue(deviceId, out var code).Should().BeTrue($"device code for {deviceId} should be tracked during test setup");
        _deviceBootstrapSecrets.TryGetValue(deviceId, out var secret)
            .Should()
            .BeTrue($"device bootstrap secret for {deviceId} should be tracked during test setup");

        using var request = CreateBootstrapRequest(code!, secret!);
        using var response = await _fixture.HttpClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var bootstrap = await response.Content.ReadFromJsonAsync<EdgeBootstrapDto>(JsonOptions);
        bootstrap.Should().NotBeNull();
        bootstrap!.UploadAccessToken.Should().NotBeNullOrWhiteSpace();
        return bootstrap.UploadAccessToken;
    }

    private async Task<HttpResponseMessage> PostEdgeCapacityAsync(
        string accessToken,
        Guid deviceId,
        DateOnly date,
        string plcName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/edge/capacity/hourly")
        {
            Content = JsonContent.Create(new
            {
                DeviceId = deviceId,
                Date = date,
                ShiftCode = "D",
                Hour = 9,
                Minute = 0,
                TimeLabel = "09:00-09:30",
                TotalCount = 1,
                OkCount = 1,
                NgCount = 0,
                PlcName = plcName
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await _fixture.HttpClient.SendAsync(request);
    }
}
