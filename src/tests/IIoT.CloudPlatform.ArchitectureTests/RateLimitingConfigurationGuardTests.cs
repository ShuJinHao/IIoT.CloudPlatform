using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace IIoT.CloudPlatform.ArchitectureTests;

public sealed class RateLimitingConfigurationGuardTests
{
    [Fact]
    public void HttpApiAppSettings_ShouldUseSplitRateLimitingSections()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(CloudRepositoryPath.Find("src", "hosts", "IIoT.HttpApi", "appsettings.json"))
            .Build();

        configuration.GetValue<int>("RateLimiting:GeneralApi:PermitLimit").Should().Be(300);
        configuration.GetValue<int>("RateLimiting:PasswordLogin:PermitLimit").Should().Be(10);
        configuration.GetValue<int>("RateLimiting:Refresh:PermitLimit").Should().Be(60);
        configuration.GetValue<int>("RateLimiting:EdgeOperatorLogin:PermitLimit").Should().Be(20);
        configuration.GetValue<int>("RateLimiting:Bootstrap:PermitLimit").Should().Be(30);
        configuration.GetValue<int>("RateLimiting:AiRead:PermitLimit").Should().Be(60);
        configuration.GetValue<int>("RateLimiting:CapacityUpload:TokenLimit").Should().Be(120);
        configuration.GetValue<int>("RateLimiting:DeviceLogUpload:TokenLimit").Should().Be(120);
        configuration.GetValue<int>("RateLimiting:PassStationUpload:TokenLimit").Should().Be(600);
        configuration.GetValue<int>("RateLimiting:EdgeHostPlcStateUpload:TokenLimit").Should().Be(120);
    }

    [Fact]
    public void NginxTemplates_ShouldUseCoarseUploadProtection_AndSeparateRefreshLimit()
    {
        var deployNginx = File.ReadAllText(
            CloudRepositoryPath.Find("deploy", "nginx", "nginx.conf"));

        deployNginx.Should().Contain("zone=login_limit:10m rate=10r/m");
        deployNginx.Should().Contain("zone=refresh_limit:10m rate=60r/m");
        deployNginx.Should().Contain("zone=bootstrap_limit:10m rate=60r/m");
        deployNginx.Should().Contain("zone=edge_upload_limit:20m rate=12000r/m");
        deployNginx.Should().Contain("zone=api_limit:20m rate=300r/m");
        deployNginx.Should().Contain("location = /api/v1/human/identity/refresh");
        deployNginx.Should().Contain("location /api/v1/bootstrap/");
        deployNginx.Should().NotContain("location = /api/v1/human/identity/edge-login");
        deployNginx.Should().NotContain("location /api/v1/edge/bootstrap/");
        deployNginx.Should().Contain("limit_req zone=refresh_limit burst=30 nodelay;");
        deployNginx.Should().Contain(
            "location ~ ^/api/v1/edge/(capacity/hourly|device-logs|pass-stations|edge-hosts/plc-runtime-states)");
        deployNginx.Should().Contain("limit_req zone=edge_upload_limit burst=2000 nodelay;");
    }

}
