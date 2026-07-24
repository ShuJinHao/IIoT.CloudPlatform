using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IIoT.CloudPlatform.ArchitectureTests;

public sealed class RepositoryConfigurationBoundaryTests
{
    [Fact]
    public void MigrationWorkApp_ShouldNotCarryCloudSideEdgeHostSeedFilesOrConfiguration()
    {
        var seedDataRoot = CloudRepositoryPath.Find("src", "hosts", "IIoT.MigrationWorkApp", "SeedData");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(CloudRepositoryPath.Find("src", "hosts", "IIoT.MigrationWorkApp", "appsettings.json"))
            .Build();

        File.Exists(Path.Combine(seedDataRoot, "EdgeHostSeedOptions.cs")).Should().BeFalse();
        File.Exists(Path.Combine(seedDataRoot, "EdgeHostSeedData.cs")).Should().BeFalse();
        configuration.GetSection("EdgeHostSeeds").Exists().Should().BeFalse();
    }

    [Fact]
    public void HostConfigurations_ShouldDefineRequiredHardeningAndInfrastructureSections()
    {
        var httpApi = LoadConfiguration("src", "hosts", "IIoT.HttpApi", "appsettings.json");
        var dataWorker = LoadConfiguration("src", "hosts", "IIoT.DataWorker", "appsettings.json");
        var migration = LoadConfiguration("src", "hosts", "IIoT.MigrationWorkApp", "appsettings.json");

        httpApi.GetValue<int>("DistributedLock:LeaseSeconds").Should().BeGreaterThan(0);
        httpApi.GetValue<int>("DistributedLock:RenewalCadenceSeconds").Should().BeGreaterThan(0);
        httpApi.GetValue<int>("DistributedLock:OperationTimeoutMilliseconds").Should().BeGreaterThan(0);
        httpApi.GetValue<int>("DistributedLock:RenewalShutdownTimeoutMilliseconds").Should().BeGreaterThan(0);
        dataWorker.GetValue<int>("DistributedLock:OperationTimeoutMilliseconds").Should().BeGreaterThan(0);
        dataWorker.GetValue<int>("DistributedLock:RenewalShutdownTimeoutMilliseconds").Should().BeGreaterThan(0);
        migration.GetValue<int>("DistributedLock:OperationTimeoutMilliseconds").Should().BeGreaterThan(0);
        migration.GetValue<int>("DistributedLock:RenewalShutdownTimeoutMilliseconds").Should().BeGreaterThan(0);
        httpApi.GetValue<int>("CacheSafety:FailSafeMinutes").Should().Be(30);
        httpApi.GetValue<bool>("ForwardedHeaders:Enabled").Should().BeFalse();
        httpApi.GetValue<int>("ForwardedHeaders:ForwardLimit").Should().BeGreaterThan(0);
        httpApi.GetValue<int>("RateLimiting:PasswordLogin:PermitLimit").Should().BeGreaterThan(0);
        httpApi.GetValue<int>("RateLimiting:PassStationUpload:TokenLimit").Should().BeGreaterThan(0);
        httpApi.GetSection("BootstrapAuth").Exists().Should().BeFalse();
        httpApi.GetValue<string>("EdgeInstallerArtifacts:RootPath").Should().Be("edge-updates/installers");
        httpApi.GetValue<string>("OidcProvider:Issuer").Should().NotBeNullOrWhiteSpace();
        httpApi.GetValue<string>("OidcProvider:AicopilotClientId").Should().Be("aicopilot");
        AssertAicopilotRedirectUris(httpApi);
        httpApi.GetValue<int>("OidcProvider:AuthorizationCodeLifetimeMinutes").Should().Be(5);
        httpApi.GetValue<int>("OidcProvider:AccessTokenLifetimeMinutes").Should().Be(24 * 60);
        httpApi.GetValue<int>("OidcProvider:IdentityTokenLifetimeMinutes").Should().Be(24 * 60);
        httpApi.GetValue<int>("OidcProvider:SessionIdleMinutes").Should().Be(24 * 60);
        httpApi.GetValue<int>("Infrastructure:Postgres:CommandTimeoutSeconds").Should().BeGreaterThan(0);
        httpApi.GetValue<int>("Infrastructure:EventBus:RetryLimit").Should().BeGreaterThanOrEqualTo(0);
        httpApi.GetValue<int>("Infrastructure:EventBus:PrefetchMultiplier").Should().BeGreaterThan(0);
        dataWorker.GetValue<int>("Infrastructure:Postgres:CommandTimeoutSeconds").Should().BeGreaterThan(0);
        dataWorker.GetValue<int>("Infrastructure:EventBus:Consumers:PassStationConcurrentMessageLimit").Should().BeGreaterThan(0);
        dataWorker.GetValue<int>("Infrastructure:EventBus:Consumers:DeviceLogConcurrentMessageLimit").Should().BeGreaterThan(0);
        dataWorker.GetValue<int>("Infrastructure:EventBus:Consumers:HourlyCapacityConcurrentMessageLimit").Should().BeGreaterThan(0);
        migration.GetValue<int>("Infrastructure:Postgres:CommandTimeoutSeconds").Should().BeGreaterThan(0);
        migration.GetValue<int>("Infrastructure:Postgres:MaxRetryCount").Should().BeGreaterThanOrEqualTo(0);
        migration.GetValue<string>("OidcProvider:AicopilotClientId").Should().Be("aicopilot");
        AssertAicopilotRedirectUris(migration);
        migration.GetValue<int>("OidcProvider:AuthorizationCodeLifetimeMinutes").Should().Be(5);
        migration.GetValue<int>("OidcProvider:AccessTokenLifetimeMinutes").Should().Be(24 * 60);
        migration.GetValue<int>("OidcProvider:IdentityTokenLifetimeMinutes").Should().Be(24 * 60);
        migration.GetValue<int>("OidcProvider:SessionIdleMinutes").Should().Be(24 * 60);
    }

    [Fact]
    public void CloudProjectGraph_ShouldNotReferenceRetiredServicesCommonProject()
    {
        var projectFiles = Directory.GetFiles(CloudRepositoryPath.Find("src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}services{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}infrastructure{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (var projectFile in projectFiles)
        {
            var references = XDocument.Load(projectFile)
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(static include => !string.IsNullOrWhiteSpace(include))
                .Select(include => Path.GetFullPath(include!, Path.GetDirectoryName(projectFile)!));

            references.Should().NotContain(path =>
                string.Equals(
                    Path.GetFileName(path),
                    "IIoT.Services.Common.csproj",
                    StringComparison.OrdinalIgnoreCase));
        }

        var solutionProjects = XDocument.Load(CloudRepositoryPath.Find("IIoT.CloudPlatform.slnx"))
            .Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => ((string?)element.Attribute("Path"))?.Replace('\\', '/'))
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        solutionProjects.Should().Contain("src/services/IIoT.Services.Contracts/IIoT.Services.Contracts.csproj");
        solutionProjects.Should().Contain("src/services/IIoT.Services.CrossCutting/IIoT.Services.CrossCutting.csproj");
        solutionProjects.Should().NotContain("src/services/IIoT.Services.Common/IIoT.Services.Common.csproj");
    }

    private static void AssertAicopilotRedirectUris(IConfiguration configuration)
    {
        var redirectUris = configuration.GetSection("OidcProvider:AicopilotRedirectUris").Get<string[]>() ?? [];
        redirectUris.Should().NotBeEmpty();
        foreach (var uri in redirectUris)
        {
            Uri.TryCreate(uri, UriKind.Absolute, out var parsed).Should().BeTrue();
            parsed!.AbsolutePath.Should().Be("/api/identity/cloud-oidc/callback");
        }
    }

    private static IConfigurationRoot LoadConfiguration(params string[] relativeSegments) =>
        new ConfigurationBuilder().AddJsonFile(CloudRepositoryPath.Find(relativeSegments)).Build();
}
