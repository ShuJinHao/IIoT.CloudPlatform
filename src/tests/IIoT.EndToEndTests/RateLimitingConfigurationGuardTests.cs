using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace IIoT.EndToEndTests;

public sealed class RateLimitingConfigurationGuardTests
{
    [Fact]
    public void HttpApiAppSettings_ShouldUseSplitRateLimitingSections()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(FindRepoFile("src", "hosts", "IIoT.HttpApi", "appsettings.json"))
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
    }

    [Fact]
    public void HttpApiControllers_ShouldUseDedicatedRateLimitPolicies()
    {
        var identitySource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Human", "HumanIdentityController.cs"));
        var capacitySource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Edge", "EdgeCapacityController.cs"));
        var deviceLogSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Edge", "EdgeDeviceLogController.cs"));
        var passStationSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Edge", "EdgePassStationController.cs"));
        var bootstrapSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Edge", "EdgeBootstrapController.cs"));
        var aiReadSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "AiRead", "AiReadController.cs"));
        var humanControllers = new[]
        {
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Human", "HumanCapacityController.cs"),
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Human", "HumanDeviceController.cs"),
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Human", "HumanDeviceLogController.cs"),
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Human", "HumanEmployeeController.cs"),
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Human", "HumanPassStationController.cs"),
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Human", "HumanRecipeController.cs"),
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Human", "MasterData", "HumanMasterDataProcessController.cs")
        };

        identitySource.Should().Contain("HttpApiRateLimitPolicies.PasswordLogin");
        identitySource.Should().Contain("HttpApiRateLimitPolicies.Refresh");
        identitySource.Should().Contain("HttpApiRateLimitPolicies.EdgeOperatorLogin");
        identitySource.Should().Contain("HttpApiRateLimitPolicies.GeneralApi");

        bootstrapSource.Should().Contain("HttpApiRateLimitPolicies.Bootstrap");
        aiReadSource.Should().Contain("HttpApiRateLimitPolicies.AiRead");
        capacitySource.Should().Contain("HttpApiRateLimitPolicies.CapacityUpload");
        deviceLogSource.Should().Contain("HttpApiRateLimitPolicies.DeviceLogUpload");
        passStationSource.Should().Contain("HttpApiRateLimitPolicies.PassStationUpload");

        foreach (var controller in humanControllers)
        {
            File.ReadAllText(controller).Should().Contain("HttpApiRateLimitPolicies.GeneralApi");
        }
    }

    [Fact]
    public void NginxTemplates_ShouldUseCoarseUploadProtection_AndSeparateRefreshLimit()
    {
        var aspirateNginx = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.AppHost", "aspirate-output", "nginx.conf"));
        var deployNginx = File.ReadAllText(
            FindRepoFile("deploy", "nginx", "nginx.conf"));

        foreach (var source in new[] { aspirateNginx, deployNginx })
        {
            source.Should().Contain("zone=login_limit:10m rate=10r/m");
            source.Should().Contain("zone=refresh_limit:10m rate=60r/m");
            source.Should().Contain("zone=edge_login_limit:10m rate=20r/m");
            source.Should().Contain("zone=bootstrap_limit:10m rate=60r/m");
            source.Should().Contain("zone=edge_upload_limit:20m rate=1200r/m");
            source.Should().Contain("zone=api_limit:20m rate=300r/m");
            source.Should().Contain("location = /api/v1/human/identity/refresh");
            source.Should().Contain("limit_req zone=refresh_limit burst=30 nodelay;");
            source.Should().Contain("limit_req zone=edge_upload_limit burst=400 nodelay;");
        }
    }

    private static string FindRepoFile(params string[] relativeSegments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativeSegments[0]);
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                return Path.Combine(current.FullName, Path.Combine(relativeSegments));
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for source inspection.");
    }
}
