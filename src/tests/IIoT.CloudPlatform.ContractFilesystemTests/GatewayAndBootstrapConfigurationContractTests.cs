using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IIoT.CloudPlatform.ContractFilesystemTests;

public sealed class GatewayAndBootstrapConfigurationContractTests
{
    [Fact]
    public void HttpApiSecurityPipeline_ShouldRegisterDeviceBindingAndApplyForwardedHeadersBeforeIdentityAndRateLimiting()
    {
        var dependencyInjectionSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "hosts", "IIoT.HttpApi", "DependencyInjection.cs"));
        var programSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "hosts", "IIoT.HttpApi", "Program.cs"));

        dependencyInjectionSource.Should().Contain("AddOpenBehavior(typeof(DeviceBindingBehavior<,>))");
        dependencyInjectionSource.Should().Contain("GetRequiredValidatedOptions<HttpApiForwardedHeadersOptions>");
        dependencyInjectionSource.Should().Contain("Configure<ForwardedHeadersOptions>");
        dependencyInjectionSource.Should().Contain("AddRateLimiter(options =>");

        programSource.Should().Contain("options.Conventions.Add(new RouteSurfaceApiExplorerConvention())");
        programSource.Should().Contain("builder.Services.AddSwaggerGen(options =>");
        programSource.Should().Contain("options.DocInclusionPredicate((documentName, apiDescription) =>");
        programSource.Split("options.SwaggerDoc(", StringSplitOptions.None).Length.Should().Be(5);
        programSource.Split("options.SwaggerEndpoint(", StringSplitOptions.None).Length.Should().Be(5);
        foreach (var surface in new[] { "human", "edge", "bootstrap", "ai-read" })
        {
            programSource.Should().Contain($"options.SwaggerDoc(\"{surface}\"");
            programSource.Should().Contain(
                $"options.SwaggerEndpoint(\"/swagger/{surface}/swagger.json\", \"{surface}\")");
        }

        var forwardedHeadersIndex = programSource.IndexOf("app.UseForwardedHeaders();", StringComparison.Ordinal);
        var authenticationIndex = programSource.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var rateLimiterIndex = programSource.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);
        var authorizationIndex = programSource.IndexOf("app.UseAuthorization();", StringComparison.Ordinal);

        forwardedHeadersIndex.Should().BeGreaterThanOrEqualTo(0);
        authenticationIndex.Should().BeGreaterThan(forwardedHeadersIndex);
        rateLimiterIndex.Should().BeGreaterThan(authenticationIndex);
        authorizationIndex.Should().BeGreaterThan(rateLimiterIndex);
    }

    [Fact]
    public void AspireFixture_ShouldNormalizeAllProxyVariablesBeforeCreatingTheDistributedApplication()
    {
        var fixtureSource = File.ReadAllText(
            CloudRepositoryPath.Find(
                "src",
                "testing",
                "IIoT.CloudPlatform.IntegrationTestKit",
                "IIoTAppFixture.cs"));
        var proxyIndex = fixtureSource.IndexOf("ConfigureAspireProxyEnvironment();", StringComparison.Ordinal);
        var builderIndex = fixtureSource.IndexOf(
            "DistributedApplicationTestingBuilder.CreateAsync",
            StringComparison.Ordinal);

        proxyIndex.Should().BeGreaterThanOrEqualTo(0);
        builderIndex.Should().BeGreaterThan(proxyIndex);
        foreach (var variable in new[]
                 {
                     "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY",
                     "http_proxy", "https_proxy", "all_proxy",
                     "NO_PROXY", "no_proxy"
                 })
        {
            fixtureSource.Should().Contain($"\"{variable}\"");
        }

        fixtureSource.Should().Contain("SetEnvironmentVariable(name, null)");
        fixtureSource.Should().Contain("SetEnvironmentVariable(\"NO_PROXY\", TestNoProxyValue)");
        fixtureSource.Should().Contain("localhost,127.0.0.1,::1,host.docker.internal");
        fixtureSource.Should().Contain("WaitForGatewayHealthzAsync(_httpClient, startupTimeout.Token)");
    }

    [Fact]
    public void AppHost_ShouldWireSecretSeedParametersMigrationGatewayAndWebEndpoints()
    {
        var appHostSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "hosts", "IIoT.AppHost", "AppHost.cs"));
        var viteConfigurationSource = File.ReadAllText(
            CloudRepositoryPath.Find("src", "ui", "iiot-web", "vite.config.ts"));

        appHostSource.Should().Contain("AddParameter(\"seed-admin-no\")");
        appHostSource.Should().Contain("AddParameter(\"seed-admin-password\", secret: true)");
        appHostSource.Should().Contain("WithEnvironment(\"SEED_ADMIN_NO\", seedAdminNo)");
        appHostSource.Should().Contain("WithEnvironment(\"SEED_ADMIN_PASSWORD\", seedAdminPassword)");
        appHostSource.Should().Contain("AddProject<Projects.IIoT_Gateway>(\"iiot-gateway\")");
        appHostSource.Should().Contain("ReverseProxy__Clusters__httpapi__Destinations__primary__Address");
        appHostSource.Should().Contain("apiService.GetEndpoint(\"http\")");
        appHostSource.Should().Contain(".WithReference(gatewayService)");
        appHostSource.Should().Contain("VITE_API_URL\", gatewayService.GetEndpoint(\"http\")");
        Assert.Contains("AppHostTestingGuard.EnsureAllowed(", appHostSource, StringComparison.Ordinal);
        Assert.DoesNotContain(".WithEnvironment(\"DOTNET_ENVIRONMENT\", \"Testing\")", appHostSource, StringComparison.Ordinal);
        Assert.Contains(".WithEnvironment(\"DOTNET_ENVIRONMENT\", builder.Environment.EnvironmentName)", appHostSource, StringComparison.Ordinal);

        var fixtureSource = File.ReadAllText(
            CloudRepositoryPath.Find(
                "src", "testing", "IIoT.CloudPlatform.IntegrationTestKit", "IIoTAppFixture.cs"));
        fixtureSource.Should().Contain(
            "SetEnvironmentVariable(\"DOTNET_ENVIRONMENT\", \"Testing\")");

        viteConfigurationSource.Should().Contain("process.env.VITE_API_URL");
        viteConfigurationSource.Should().NotContain("Object.entries(process.env)");
        viteConfigurationSource.Should().NotContain("console.log(");
    }

    [Fact]
    public void BootstrapDeploymentContract_ShouldRequireSecretWithoutOptionalBypass()
    {
        var cloudRules = File.ReadAllText(CloudRepositoryPath.Find("docs", "云端规则.md"));
        var environmentExample = File.ReadAllText(CloudRepositoryPath.Find("deploy", ".env.example"));
        var deploymentReadme = File.ReadAllText(CloudRepositoryPath.Find("deploy", "README.md"));
        var productionCompose = File.ReadAllText(CloudRepositoryPath.Find("deploy", "docker-compose.prod.yml"));
        var httpApiConfiguration = new ConfigurationBuilder()
            .AddJsonFile(CloudRepositoryPath.Find("src", "hosts", "IIoT.HttpApi", "appsettings.json"))
            .Build();

        cloudRules.Should().Contain("ClientCode");
        cloudRules.Should().Contain("DeviceId");
        deploymentReadme.Should().Contain("/api/v1/bootstrap/device-instance");
        deploymentReadme.Should().Contain("X-IIoT-Bootstrap-Secret");
        environmentExample.Should().Contain("X-IIoT-Bootstrap-Secret");
        environmentExample.Should().NotContain("BOOTSTRAP_AUTH_REQUIRE_SECRET");
        productionCompose.Should().NotContain("BootstrapAuth__RequireSecret");
        httpApiConfiguration.GetSection("BootstrapAuth").Exists().Should().BeFalse();
    }

    [Fact]
    public void GatewayConfiguration_ShouldExposeOnlyCanonicalRoutesAndExplicitRejectedAliases()
    {
        var configuration = LoadGatewayConfiguration();
        var routes = configuration.GetSection("ReverseProxy:Routes").GetChildren()
            .ToDictionary(section => section.Key, section => section, StringComparer.Ordinal);
        var blockedAliases = configuration.GetSection("GatewayRoutes:BlockedAliases").GetChildren()
            .ToDictionary(section => section.Key, section => section, StringComparer.Ordinal);

        routes.Keys.Should().BeEquivalentTo(
        [
            "internal-healthz",
            "oidc-discovery",
            "oidc-jwks",
            "oidc-connect",
            "human",
            "public",
            "machine",
            "edge",
            "ai-read",
            "ai-identity",
            "bootstrap-device-instance",
            "bootstrap-edge-login",
            "bootstrap-edge-refresh"
        ]);
        blockedAliases.Keys.Should().BeEquivalentTo(
            ["blocked-edge-bootstrap", "blocked-human-edge-login"]);
        blockedAliases["blocked-edge-bootstrap"]["PathPrefix"].Should().Be("/api/v1/edge/bootstrap");
        blockedAliases["blocked-human-edge-login"]["Path"].Should().Be("/api/v1/human/identity/edge-login");

        AssertRoute(routes, "human", "/api/v1/human/{**catch-all}", "human");
        AssertRoute(routes, "public", "/api/v1/public/{**catch-all}", "public");
        AssertRoute(routes, "machine", "/api/v1/machine/{**catch-all}", "machine");
        AssertRoute(routes, "edge", "/api/v1/edge/{**catch-all}", "edge");
        AssertRoute(routes, "ai-read", "/api/v1/ai/read/{**catch-all}", "ai-read");
        AssertRoute(routes, "ai-identity", "/api/v1/ai/identity/{**catch-all}", "ai-identity");
        AssertRoute(routes, "oidc-discovery", "/.well-known/openid-configuration", "oidc");
        AssertRoute(routes, "oidc-jwks", "/.well-known/jwks", "oidc");
        AssertRoute(routes, "oidc-connect", "/connect/{**catch-all}", "oidc");
        AssertRoute(routes, "bootstrap-device-instance", "/api/v1/bootstrap/device-instance", "bootstrap");
        AssertRoute(routes, "bootstrap-edge-login", "/api/v1/bootstrap/edge-login", "bootstrap");
        AssertRoute(routes, "bootstrap-edge-refresh", "/api/v1/bootstrap/edge-refresh", "bootstrap");

        routes["bootstrap-device-instance"].GetSection("Transforms").GetChildren()
            .Should().Contain(section => section["PathSet"] == "/api/v1/edge/bootstrap/device-instance");
        routes["bootstrap-edge-login"].GetSection("Transforms").GetChildren()
            .Should().Contain(section => section["PathSet"] == "/api/v1/human/identity/edge-login");
        routes["bootstrap-edge-refresh"].GetSection("Transforms").GetChildren()
            .Should().Contain(section => section["PathSet"] == "/api/v1/edge/bootstrap/edge-refresh");
    }

    [Fact]
    public void GatewayClusterConfiguration_ShouldKeepOneDestinationTimeoutAndHealthProbe()
    {
        var configuration = LoadGatewayConfiguration();
        var cluster = configuration.GetSection("ReverseProxy:Clusters:httpapi");
        var destinations = cluster.GetSection("Destinations").GetChildren().ToArray();

        destinations.Should().ContainSingle();
        destinations[0].Key.Should().Be("primary");
        destinations[0]["Address"].Should().Be("http://localhost:8080/");
        cluster["HttpRequest:ActivityTimeout"].Should().Be("00:01:00");
        cluster.GetValue<bool>("HealthCheck:Active:Enabled").Should().BeTrue();
        cluster["HealthCheck:Active:Path"].Should().Be("/internal/healthz");
    }

    [Fact]
    public void GatewayDocumentation_ShouldNameFormalRoutesAndRejectedAliases()
    {
        var deploymentReadme = File.ReadAllText(CloudRepositoryPath.Find("deploy", "README.md"));

        deploymentReadme.Should().Contain("/api/v1/human/*");
        deploymentReadme.Should().Contain("/api/v1/edge/*");
        deploymentReadme.Should().Contain("/api/v1/machine/*");
        deploymentReadme.Should().Contain("/api/v1/bootstrap/*");
        deploymentReadme.Should().Contain("/api/v1/bootstrap/device-instance");
        deploymentReadme.Should().Contain("/api/v1/bootstrap/edge-login");
        deploymentReadme.Should().Contain("X-IIoT-Bootstrap-Secret");
        deploymentReadme.Should().Contain("/api/v1/edge/bootstrap/device-instance");
        deploymentReadme.Should().Contain("/api/v1/human/identity/edge-login");
        deploymentReadme.Should().Contain("rejected");
    }

    private static IConfigurationRoot LoadGatewayConfiguration() =>
        new ConfigurationBuilder()
            .AddJsonFile(CloudRepositoryPath.Find("src", "hosts", "IIoT.Gateway", "appsettings.json"))
            .Build();

    private static void AssertRoute(
        IReadOnlyDictionary<string, IConfigurationSection> routes,
        string name,
        string path,
        string surface)
    {
        var route = routes[name];
        route["Match:Path"].Should().Be(path);
        route["ClusterId"].Should().Be("httpapi");
        route.GetSection("Transforms").GetChildren().Should().Contain(section =>
            section["RequestHeader"] == "X-IIoT-Route-Surface" && section["Set"] == surface);
    }

}
