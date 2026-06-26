using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using IIoT.HttpApi;
using IIoT.MigrationWorkApp.SeedData;
using Microsoft.Extensions.Configuration;

namespace IIoT.EndToEndTests;

public sealed class ConfigurationGuardTests
{
    private const string KnownWeakSeedAdminPassword = "Ljh123456!";
    private const string KnownWeakJwtSecret = "iiot-cloud-jwt-secret-2026-04-22";

    [Fact]
    public void DesignTimeConnectionStringResolver_MissingConnectionString_ShouldThrowClearError()
    {
        var act = () => DesignTimeConnectionStringResolver.Resolve(_ => null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{DesignTimeConnectionStringResolver.ConnectionStringEnvironmentVariable}*");
    }

    [Fact]
    public void SeedAdminOptions_Load_ShouldDefaultRealName_AndAllowMissingPassword()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SeedAdminOptions.EmployeeNoKey] = IIoTAppFixture.SeedAdminEmployeeNo
            })
            .Build();

        var options = SeedAdminOptions.Load(configuration);

        options.EmployeeNo.Should().Be(IIoTAppFixture.SeedAdminEmployeeNo);
        options.Password.Should().BeNull();
        options.RealName.Should().Be(IIoTAppFixture.SeedAdminRealName);
    }

    [Fact]
    public void SeedAdminOptions_Load_ShouldRequireEmployeeNumber()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var act = () => SeedAdminOptions.Load(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{SeedAdminOptions.EmployeeNoKey}*");
    }

    [Fact]
    public void SeedAdminOptions_RequirePassword_ShouldThrowWhenMissing()
    {
        var options = new SeedAdminOptions(
            IIoTAppFixture.SeedAdminEmployeeNo,
            null,
            IIoTAppFixture.SeedAdminRealName,
            ResetPassword: false);

        var act = () => options.RequirePassword();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{SeedAdminOptions.PasswordKey}*");
    }

    [Fact]
    public void SeedAdminOptions_Load_ShouldOnlyRequestPasswordResetWhenExplicitlyEnabled()
    {
        var defaultConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SeedAdminOptions.EmployeeNoKey] = IIoTAppFixture.SeedAdminEmployeeNo,
                [SeedAdminOptions.PasswordKey] = IIoTAppFixture.SeedAdminPassword
            })
            .Build();

        SeedAdminOptions.Load(defaultConfiguration).ResetPassword.Should().BeFalse();

        var resetConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SeedAdminOptions.EmployeeNoKey] = IIoTAppFixture.SeedAdminEmployeeNo,
                [SeedAdminOptions.PasswordKey] = IIoTAppFixture.SeedAdminPassword,
                [SeedAdminOptions.ResetPasswordKey] = "true"
            })
            .Build();

        SeedAdminOptions.Load(resetConfiguration).ResetPassword.Should().BeTrue();
        SeedAdminOptions.IsPasswordResetRequested(resetConfiguration).Should().BeTrue();
    }

    [Fact]
    public void AppHost_ShouldWireSeedAdminParametersIntoMigrationProject()
    {
        var appHostSource = File.ReadAllText(FindRepoFile("src", "hosts", "IIoT.AppHost", "AppHost.cs"));

        appHostSource.Should().Contain("AddParameter(\"seed-admin-no\"");
        appHostSource.Should().Contain("AddParameter(\"seed-admin-password\", secret: true)");
        appHostSource.Should().Contain("WithEnvironment(\"SEED_ADMIN_NO\", seedAdminNo)");
        appHostSource.Should().Contain("WithEnvironment(\"SEED_ADMIN_PASSWORD\", seedAdminPassword)");
        appHostSource.Should().Contain("AddProject<Projects.IIoT_Gateway>(\"iiot-gateway\")");
        appHostSource.Should().Contain("ReverseProxy__Clusters__httpapi__Destinations__primary__Address");
        appHostSource.Should().Contain(".WithReference(gatewayService)");
        appHostSource.Should().Contain("VITE_API_URL\", gatewayService.GetEndpoint(\"http\")");
    }

    [Fact]
    public void AppHostConfiguration_ShouldNotCommitDefaultDatabasePasswordOrRealRegistryValues()
    {
        var appHostSettingsSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.AppHost", "appsettings.json"));
        var rootAspirateSource = File.ReadAllText(FindRepoFile("aspirate.json"));
        var appHostAspirateSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.AppHost", "aspirate.json"));

        appHostSettingsSource.Should().NotContain("pg-password");
        appHostSettingsSource.Should().NotContain("123456");
        rootAspirateSource.Should().Contain("registry.example.com/iiot");
        rootAspirateSource.Should().NotContain("10.98.90.154");
        appHostAspirateSource.Should().Contain("registry.example.com");
        appHostAspirateSource.Should().NotContain("10.98.90.154");
    }

    [Fact]
    public void AppHostLaunchSettings_ShouldNotDefineProxyEnvironmentVariables()
    {
        var launchSettingsSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.AppHost", "Properties", "launchSettings.json"));

        launchSettingsSource.Should().NotContain("HTTP_PROXY");
        launchSettingsSource.Should().NotContain("HTTPS_PROXY");
        launchSettingsSource.Should().NotContain("ALL_PROXY");
        launchSettingsSource.Should().NotContain("http_proxy");
        launchSettingsSource.Should().NotContain("https_proxy");
        launchSettingsSource.Should().NotContain("all_proxy");
        launchSettingsSource.Should().NotContain("NO_PROXY");
        launchSettingsSource.Should().NotContain("no_proxy");
    }

    [Fact]
    public void AppFixture_ShouldNormalizeProxyEnvironmentBeforeCreatingAspireBuilder()
    {
        var fixtureSource = File.ReadAllText(
            FindRepoFile("src", "tests", "IIoT.EndToEndTests", "IIoTAppFixture.cs"));
        var proxyIndex = fixtureSource.IndexOf("ConfigureAspireProxyEnvironment();", StringComparison.Ordinal);
        var builderIndex = fixtureSource.IndexOf(
            "DistributedApplicationTestingBuilder.CreateAsync",
            StringComparison.Ordinal);

        proxyIndex.Should().BeGreaterThanOrEqualTo(0);
        builderIndex.Should().BeGreaterThan(proxyIndex);
        fixtureSource.Should().Contain("\"HTTP_PROXY\"");
        fixtureSource.Should().Contain("\"http_proxy\"");
        fixtureSource.Should().Contain("SetEnvironmentVariable(name, null)");
        fixtureSource.Should().Contain("SetEnvironmentVariable(\"NO_PROXY\", TestNoProxyValue)");
    }

    [Fact]
    public void TestRuntimeCredentials_ShouldNotUseKnownWeakDeploymentSecrets()
    {
        IIoTAppFixture.SeedAdminPassword.Should().NotBe(KnownWeakSeedAdminPassword);
        IIoTAppFixture.TestJwtSecret.Should().NotBe(KnownWeakJwtSecret);
        IIoTAppFixture.SeedAdminPassword.Should().StartWith("E2eSeed-");
        IIoTAppFixture.SeedAdminPassword.Should().EndWith("!");
        IIoTAppFixture.TestJwtSecret.Should().StartWith("iiot-e2e-");
    }

    [Fact]
    public void DeployPrecheck_ShouldRejectKnownWeakDeploymentSecrets()
    {
        var preDeploySource = File.ReadAllText(FindRepoFile("deploy", "scripts", "pre-deploy-check.sh"));
        var releaseCommonSource = File.ReadAllText(FindRepoFile("deploy", "scripts", "release-common.sh"));

        preDeploySource.Should().Contain("ensure_required_secret_values_changed");
        var jwtSecretGuardLine = Regex.Match(
            releaseCommonSource,
            "require_changed_secret_value[\\s\\\\\\r\\n]+JWTSETTINGS__SECRET[\\s\\S]*?iiot-cloud-jwt-secret-2026-04-22").Value;
        var seedAdminPasswordGuardLine = Regex.Match(
            releaseCommonSource,
            "require_changed_secret_value[\\s\\\\\\r\\n]+SEED_ADMIN_PASSWORD[\\s\\S]*?Ljh123456!").Value;

        jwtSecretGuardLine.Should().Contain("change-me-jwt-secret");
        jwtSecretGuardLine.Should().Contain(KnownWeakJwtSecret);
        seedAdminPasswordGuardLine.Should().Contain("change-me-admin-password");
        seedAdminPasswordGuardLine.Should().Contain(KnownWeakSeedAdminPassword);
    }

    [Fact]
    public void LocalDeployEnv_WhenPresent_ShouldNotUseKnownWeakDeploymentSecrets()
    {
        var envPath = FindRepoFile("deploy", ".env");
        if (!File.Exists(envPath))
        {
            return;
        }

        var envSource = File.ReadAllText(envPath);

        envSource.Should().NotContain($"JWTSETTINGS__SECRET={KnownWeakJwtSecret}");
        envSource.Should().NotContain($"SEED_ADMIN_PASSWORD={KnownWeakSeedAdminPassword}");
    }

    [Fact]
    public void MigrationWorkApp_ShouldPrecheckAndNormalizeLegacyDeviceCodesBeforeCreatingUniqueIndex()
    {
        var orchestratorSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.MigrationWorkApp", "DatabaseInitializationOrchestrator.cs"));

        orchestratorSource.Should().Contain("UPPER(BTRIM(client_code))");
        orchestratorSource.Should().Contain("COUNT(*) AS duplicate_count");
        orchestratorSource.Should().Contain("BuildNormalizedClientCodeConflictMessage");
        orchestratorSource.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS ix_devices_client_code ON devices (client_code);");
        orchestratorSource.Should().Contain("ALTER TABLE devices DROP COLUMN IF EXISTS mac_address;");
    }

    [Fact]
    public void EdgeBootstrapController_ShouldRequireClientCodeAndBootstrapSecret()
    {
        var controllerSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Edge", "EdgeBootstrapController.cs"));
        var programSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Program.cs"));
        var filterSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Infrastructure", "RefreshTokenResponseFilter.cs"));

        controllerSource.Should().Contain("[FromQuery] string clientCode");
        controllerSource.Should().Contain("X-IIoT-Bootstrap-Secret");
        controllerSource.Should().Contain("RefreshTokenResponseFilter.SetHeaders");
        controllerSource.Should().NotContain("RefreshTokenHeaderNames.ApplyTo");
        programSource.Should().Contain("RefreshTokenResponseFilter");
        filterSource.Should().Contain("IAsyncResultFilter");
        controllerSource.Should().Contain("BootstrapSecretHeaderNames.Secret");
        controllerSource.Should().Contain("string? bootstrapSecret");
    }

    [Fact]
    public void HumanDeviceController_ShouldNotExposeManualBootstrapSecretRotation()
    {
        var controllerSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Human", "HumanDeviceController.cs"));

        controllerSource.Should().NotContain("bootstrap-secret/rotate");
        controllerSource.Should().NotContain("RotateBootstrapSecret");
        controllerSource.Should().NotContain("RotateDeviceBootstrapSecretCommand");
    }

    [Fact]
    public void BootstrapHardeningDesign_ShouldDocumentMandatorySecret()
    {
        var cloudRulesSource = File.ReadAllText(FindRepoFile("docs", "云端规则.md"));
        var envExampleSource = File.ReadAllText(FindRepoFile("deploy", ".env.example"));
        var deployReadmeSource = File.ReadAllText(FindRepoFile("deploy", "README.md"));

        cloudRulesSource.Should().Contain("ClientCode");
        cloudRulesSource.Should().Contain("DeviceId");
        deployReadmeSource.Should().Contain("/api/v1/bootstrap/device-instance");
        deployReadmeSource.Should().Contain("X-IIoT-Bootstrap-Secret");
        envExampleSource.Should().Contain("BOOTSTRAP_AUTH_REQUIRE_SECRET=true");
        envExampleSource.Should().Contain("X-IIoT-Bootstrap-Secret");
    }

    [Fact]
    public void HttpApiControllers_ShouldOnlyExposeHumanAndEdgeRoutes()
    {
        var controllerDirectory = FindRepoDirectory("src", "hosts", "IIoT.HttpApi", "Controllers");
        var invalidRoutes = new List<string>();

        foreach (var file in Directory.GetFiles(controllerDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            var matches = Regex.Matches(source, "\\[Route\\(\"([^\"]+)\"\\)\\]");

            foreach (Match match in matches)
            {
                var route = match.Groups[1].Value;
                if (!route.StartsWith("api/v1/human/", StringComparison.Ordinal)
                    && !route.StartsWith("api/v1/edge/", StringComparison.Ordinal)
                    && !route.StartsWith("api/v1/public/", StringComparison.Ordinal)
                    && !route.StartsWith("api/v1/ai/read", StringComparison.Ordinal)
                    && !route.StartsWith("api/v1/ai/identity", StringComparison.Ordinal))
                {
                    invalidRoutes.Add($"{Path.GetFileName(file)}:{route}");
                }
            }
        }

        invalidRoutes.Should().BeEmpty();
    }

    [Fact]
    public void HttpApiAppSettings_ShouldDefinePermissionCacheExpirationMinutes()
    {
        var appSettingsPath = FindRepoFile("src", "hosts", "IIoT.HttpApi", "appsettings.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(appSettingsPath)
            .Build();

        configuration.GetValue<int>("PermissionCache:ExpirationMinutes").Should().BeGreaterThan(0);
    }

    [Fact]
    public void InfrastructureDependencyInjection_ShouldConfigureFusionCacheBackplaneWithRedisConnectionString()
    {
        var infrastructureSource = File.ReadAllText(
            FindRepoFile("src", "infrastructure", "IIoT.Infrastructure", "DependencyInjection.cs"));

        infrastructureSource.Should().Contain("GetConnectionString(\"redis-cache\")");
        infrastructureSource.Should().Contain("WithStackExchangeRedisBackplane(options =>");
        infrastructureSource.Should().Contain("options.Configuration = redisConnectionString;");
        infrastructureSource.Should().Contain("CacheSafetyOptions.SectionName");
        infrastructureSource.Should().Contain("FailSafeMaxDuration = cacheSafetyOptions.ResolveFailSafeDuration()");
    }

    [Fact]
    public void HttpApi_ShouldRegisterDeviceBindingAndRateLimiting()
    {
        var dependencyInjectionSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "DependencyInjection.cs"));
        var programSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Program.cs"));
        var forwardedHeadersIndex = programSource.IndexOf("app.UseForwardedHeaders();", StringComparison.Ordinal);
        var authenticationIndex = programSource.IndexOf("app.UseAuthentication();", StringComparison.Ordinal);
        var rateLimiterIndex = programSource.IndexOf("app.UseRateLimiter();", StringComparison.Ordinal);

        dependencyInjectionSource.Should().Contain("AddOpenBehavior(typeof(DeviceBindingBehavior<,>))");
        dependencyInjectionSource.Should().Contain("AddRateLimiter(options =>");
        dependencyInjectionSource.Should().Contain("HttpApiForwardedHeadersOptions.SectionName");
        dependencyInjectionSource.Should().Contain("GetRequiredValidatedOptions<HttpApiForwardedHeadersOptions>");
        dependencyInjectionSource.Should().Contain("Configure<ForwardedHeadersOptions>");
        forwardedHeadersIndex.Should().BeGreaterThanOrEqualTo(0);
        forwardedHeadersIndex.Should().BeLessThan(authenticationIndex);
        forwardedHeadersIndex.Should().BeLessThan(rateLimiterIndex);
        programSource.Should().Contain("app.UseRateLimiter();");
    }

    [Fact]
    public void UseCaseExceptionHandler_ShouldMapKnownRuntimeExceptions()
    {
        var source = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Infrastructure", "UseCaseExceptionHandler.cs"));

        source.Should().Contain("TimeoutException");
        source.Should().Contain("ArgumentException");
        source.Should().Contain("InvalidOperationException");
        source.Should().Contain("StatusCodes.Status500InternalServerError");
        source.Should().Contain("服务器处理请求时发生未预期错误。");
    }

    [Fact]
    public void HostAppSettings_ShouldDefineHardeningAndInfrastructureSections()
    {
        var httpApiAppSettingsPath = FindRepoFile("src", "hosts", "IIoT.HttpApi", "appsettings.json");
        var httpApiConfiguration = new ConfigurationBuilder()
            .AddJsonFile(httpApiAppSettingsPath)
            .Build();
        var dataWorkerAppSettingsPath = FindRepoFile("src", "hosts", "IIoT.DataWorker", "appsettings.json");
        var dataWorkerConfiguration = new ConfigurationBuilder()
            .AddJsonFile(dataWorkerAppSettingsPath)
            .Build();
        var migrationAppSettingsPath = FindRepoFile("src", "hosts", "IIoT.MigrationWorkApp", "appsettings.json");
        var migrationConfiguration = new ConfigurationBuilder()
            .AddJsonFile(migrationAppSettingsPath)
            .Build();

        httpApiConfiguration.GetValue<int>("DistributedLock:LeaseSeconds").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("DistributedLock:RenewalCadenceSeconds").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("CacheSafety:FailSafeMinutes").Should().Be(30);
        httpApiConfiguration.GetValue<bool>("ForwardedHeaders:Enabled").Should().BeFalse();
        httpApiConfiguration.GetValue<int>("ForwardedHeaders:ForwardLimit").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("RateLimiting:PasswordLogin:PermitLimit").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("RateLimiting:PassStationUpload:TokenLimit").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<bool>("BootstrapAuth:RequireSecret").Should().BeTrue();
        httpApiConfiguration.GetValue<string>("EdgeInstallerArtifacts:RootPath").Should().Be("edge-updates/installers");
        httpApiConfiguration.GetValue<string>("OidcProvider:Issuer").Should().NotBeNullOrWhiteSpace();
        httpApiConfiguration.GetValue<string>("OidcProvider:AicopilotClientId").Should().Be("aicopilot");
        var aicopilotRedirectUris = httpApiConfiguration.GetSection("OidcProvider:AicopilotRedirectUris")
            .Get<string[]>() ?? [];
        aicopilotRedirectUris.Should().NotBeEmpty();
        aicopilotRedirectUris.Should().OnlyContain(
            redirectUri => IsAicopilotBackendCallbackRedirectUri(redirectUri),
            "Cloud OIDC redirect_uri must target the AICopilot backend callback endpoint.");
        httpApiConfiguration.GetValue<int>("OidcProvider:AuthorizationCodeLifetimeMinutes").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("OidcProvider:SessionIdleMinutes").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("Infrastructure:Postgres:CommandTimeoutSeconds").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("Infrastructure:EventBus:RetryLimit").Should().BeGreaterThanOrEqualTo(0);
        httpApiConfiguration.GetValue<int>("Infrastructure:EventBus:PrefetchMultiplier").Should().BeGreaterThan(0);

        dataWorkerConfiguration.GetValue<int>("Infrastructure:Postgres:CommandTimeoutSeconds").Should().BeGreaterThan(0);
        dataWorkerConfiguration.GetValue<int>("Infrastructure:EventBus:Consumers:PassStationConcurrentMessageLimit").Should().BeGreaterThan(0);
        dataWorkerConfiguration.GetValue<int>("Infrastructure:EventBus:Consumers:DeviceLogConcurrentMessageLimit").Should().BeGreaterThan(0);
        dataWorkerConfiguration.GetValue<int>("Infrastructure:EventBus:Consumers:HourlyCapacityConcurrentMessageLimit").Should().BeGreaterThan(0);

        migrationConfiguration.GetValue<int>("Infrastructure:Postgres:CommandTimeoutSeconds").Should().BeGreaterThan(0);
        migrationConfiguration.GetValue<int>("Infrastructure:Postgres:MaxRetryCount").Should().BeGreaterThanOrEqualTo(0);
        migrationConfiguration.GetValue<string>("OidcProvider:AicopilotClientId").Should().Be("aicopilot");
        migrationConfiguration.GetSection("OidcProvider:AicopilotRedirectUris")
            .Get<string[]>()!
            .Should()
            .OnlyContain(
                redirectUri => IsAicopilotBackendCallbackRedirectUri(redirectUri),
                "MigrationWorkApp seeds the same AICopilot backend callback redirect_uri whitelist.");
    }

    [Fact]
    public void DataWorkerDockerfile_ShouldUseHealthcheck_AndMigrationWorkAppShouldRemainOneShot()
    {
        var dataWorkerDockerfile = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.DataWorker", "Dockerfile"));
        var dataWorkerProgram = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.DataWorker", "Program.cs"));
        var migrationDockerfile = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.MigrationWorkApp", "Dockerfile"));
        var composeSource = File.ReadAllText(
            FindRepoFile("deploy", "docker-compose.prod.yml"));

        dataWorkerDockerfile.Should().Contain("HEALTHCHECK");
        dataWorkerDockerfile.Should().Contain("dotnet IIoT.DataWorker.dll --healthcheck");
        dataWorkerProgram.Should().Contain("--healthcheck");
        dataWorkerProgram.Should().Contain("CanConnectAsync");
        migrationDockerfile.Should().NotContain("HEALTHCHECK");
        composeSource.Should().Contain("iiot-migration:");
        composeSource.Should().Contain("restart: \"no\"");
    }

    [Fact]
    public void EventContractsAndConsumers_ShouldDeclareSchemaVersionGuard()
    {
        var eventContractFiles = new[]
        {
            FindRepoFile("src", "services", "IIoT.Services.Contracts", "Events", "Capacities", "HourlyCapacityReceivedEvent.cs"),
            FindRepoFile("src", "services", "IIoT.Services.Contracts", "Events", "DeviceLogs", "DeviceLogReceivedEvent.cs"),
            FindRepoFile("src", "services", "IIoT.Services.Contracts", "Events", "PassStations", "PassStationBatchReceivedEvent.cs")
        };
        var consumerFiles = new[]
        {
            FindRepoFile("src", "hosts", "IIoT.DataWorker", "Consumers", "Production", "HourlyCapacityConsumer.cs"),
            FindRepoFile("src", "hosts", "IIoT.DataWorker", "Consumers", "Production", "DeviceLogConsumer.cs"),
            FindRepoFile("src", "hosts", "IIoT.DataWorker", "Consumers", "Production", "PassStationConsumer.cs")
        };
        var schemaVersionGuardSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.DataWorker", "Consumers", "Production", "EventSchemaVersionGuard.cs"));

        foreach (var file in eventContractFiles)
        {
            File.ReadAllText(file).Should().Contain("SchemaVersion { get; init; } = 1");
        }

        foreach (var file in consumerFiles)
        {
            File.ReadAllText(file).Should().Contain("EventSchemaVersionGuard.EnsureSupported");
        }

        schemaVersionGuardSource.Should().Contain("schemaVersion == CurrentSchemaVersion");
        schemaVersionGuardSource.Should().NotContain("schemaVersion is 0");
    }

    [Fact]
    public void DeploymentTemplates_ShouldExternalizeSecretsAndUseStandardDeployEntry()
    {
        var gitIgnoreSource = File.ReadAllText(FindRepoFile(".gitignore"));
        var envExampleSource = File.ReadAllText(FindRepoFile("deploy", ".env.example"));
        var composeSource = File.ReadAllText(FindRepoFile("deploy", "docker-compose.prod.yml"));

        gitIgnoreSource.Should().Contain("deploy/.env");
        gitIgnoreSource.Should().Contain("deploy/certs/");
        gitIgnoreSource.Should().Contain("aspirate-state.json");
        envExampleSource.Should().Contain("change-me-postgres-password");
        envExampleSource.Should().Contain("IIOT_GATEWAY_IMAGE");
        envExampleSource.Should().Contain("PUBLIC_BASE_URL=");
        envExampleSource.Should().Contain("FORWARDED_HEADERS_ENABLED");
        envExampleSource.Should().Contain("FORWARDED_HEADERS_KNOWNNETWORKS__0");
        envExampleSource.Should().Contain("OIDC_PROVIDER_SIGNING_CERTIFICATE_PATH=/app/certs/cloud-oidc-signing.pfx");
        envExampleSource.Should().Contain("EDGE_UPDATES_DIR=/srv/iiot/edge-updates");
        envExampleSource.Should().NotContain("DEPLOY_HOST=");
        envExampleSource.Should().NotContain("DEPLOY_USER=");
        envExampleSource.Should().NotContain("STACK_NAME=");
        envExampleSource.Should().NotContain("SERVER_IP");
        envExampleSource.Should().NotContain("TLS_CERT_FILE");
        envExampleSource.Should().NotContain("TLS_KEY_FILE");
        envExampleSource.Should().NotContain("GATEWAY_HTTPS_PORT");

        composeSource.Should().Contain("iiot-gateway:");
        composeSource.Should().Contain("ReverseProxy__Clusters__httpapi__Destinations__primary__Address");
        composeSource.Should().Contain("ForwardedHeaders__Enabled:");
        composeSource.Should().Contain("ForwardedHeaders__KnownNetworks__0:");
        composeSource.Should().Contain("nginx-gateway:");
        composeSource.Should().Contain("EdgeInstallerArtifacts__RootPath: /app/edge-updates/installers");
        composeSource.Should().Contain("${IIOT_NGINX_IMAGE}");
        composeSource.Should().NotContain("SERVER_IP");
        composeSource.Should().NotContain("TLS_CERT_FILE");
        composeSource.Should().NotContain("GATEWAY_HTTPS_PORT");
        composeSource.Should().NotContain("ALLOW_ANONYMOUS: \"true\"");
    }

    [Fact]
    public void LegacyAspirateDeploymentArtifacts_ShouldBeRemovedFromRepository()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepoFile(".gitignore"))
                             ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        var legacyArtifacts = new[]
        {
            Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-output", ".env.example"),
            Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-output", "build-push.ps1"),
            Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-output", "deploy.ps1"),
            Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-output", "docker-compose.yaml"),
            Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-output", "nginx.conf"),
            Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-output", "部署操作手册.txt")
        };

        legacyArtifacts.Should().OnlyContain(path => !File.Exists(path));
    }

    [Fact]
    public void GeneratedAspirateStateFiles_ShouldBeIgnoredAndRemovedFromRepository()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepoFile(".gitignore"))
                             ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        var rootStatePath = Path.Combine(repositoryRoot, "aspirate-state.json");
        var appHostStatePath = Path.Combine(repositoryRoot, "src", "hosts", "IIoT.AppHost", "aspirate-state.json");

        File.Exists(rootStatePath).Should().BeFalse();
        File.Exists(appHostStatePath).Should().BeFalse();
    }

    [Fact]
    public void DeploymentArtifacts_ShouldIgnoreGeneratedBackupOutputs()
    {
        var gitIgnoreSource = File.ReadAllText(FindRepoFile(".gitignore"));

        gitIgnoreSource.Should().Contain("deploy/backups/");
        gitIgnoreSource.Should().Contain("deploy/releases/");
    }

    [Fact]
    public void StandardDeploymentDocs_ShouldUseSelfHostedRunnerAndNoSshDeployPath()
    {
        var readmeSource = File.ReadAllText(FindRepoFile("deploy", "README.md"));
        var runnerSource = File.ReadAllText(FindRepoFile("deploy", "RUNNER.md"));
        var imageWorkflowSource = File.ReadAllText(FindRepoFile(".github", "workflows", "cloud-image.yml"));
        var deployWorkflowSource = File.ReadAllText(FindRepoFile(".github", "workflows", "cloud-deploy.yml"));

        readmeSource.Should().Contain("GitHub 托管 runner 不能访问 `10.98.90.154:80` Harbor");
        runnerSource.Should().Contain("/data/github-runner/cloud");
        runnerSource.Should().Contain("github-runner");
        imageWorkflowSource.Should().Contain("runs-on: [self-hosted, iiot-linux-prod]");
        imageWorkflowSource.Should().Contain("harbor-retention.sh");
        imageWorkflowSource.Should().Contain("HARBOR_KEEP_SHA_TAGS: 3");
        deployWorkflowSource.Should().Contain("runs-on: [self-hosted, iiot-linux-prod]");

        foreach (var source in new[] { readmeSource, runnerSource, imageWorkflowSource, deployWorkflowSource })
        {
            source.Should().NotContain("appleboy/ssh-action");
            source.Should().NotContain("appleboy/scp-action");
            source.Should().NotContain("DEPLOY_HOST");
            source.Should().NotContain("DEPLOY_SSH");
            source.Should().NotContain("ghcr.io");
        }
    }

    [Fact]
    public void DeployTemplates_ShouldDocumentSingleNodeSecretsAndSmokePolicy()
    {
        var readmeSource = File.ReadAllText(FindRepoFile("deploy", "README.md"));
        var envExampleSource = File.ReadAllText(FindRepoFile("deploy", ".env.example"));
        var composeSource = File.ReadAllText(FindRepoFile("deploy", "docker-compose.prod.yml"));

        readmeSource.Should().Contain("single-machine production starter");
        readmeSource.Should().Contain("`release_tag = sha-*`");
        readmeSource.Should().Contain("日常部署禁止手动触发 `cloud-image` 的 `workflow_dispatch`");
        readmeSource.Should().Contain("日常部署必须通过 push/merge 到 `main` 自动触发 `cloud-image`");
        readmeSource.Should().Contain("传入 `services` 时只拉取并重启指定服务");
        readmeSource.Should().Contain("Cloud catalog 会扫描 `/app/edge-updates/installers/stable/{version}/installer-artifact.json`");
        readmeSource.Should().Contain("runner 必须使用专用非 root 用户运行");
        readmeSource.Should().Contain("Docker Hub 不作为生产依赖源");
        readmeSource.Should().Contain("Edge 客户端安装素材不进 Harbor");
        readmeSource.Should().Contain("应用镜像仓库只保留当前生产 `sha-*` tag");
        readmeSource.Should().Contain("EdgeInstallerArtifacts__RootPath=/app/edge-updates/installers");
        readmeSource.Should().Contain("EdgeInstallerArtifacts__VelopackReleasesBaseUrl=${PUBLIC_BASE_URL}/edge-updates/velopack");
        readmeSource.Should().Contain("[RUNNER.md](./RUNNER.md)");
        readmeSource.Should().Contain("GET /internal/healthz");
        readmeSource.Should().Contain("[OPERATIONS.md](./OPERATIONS.md)");
        readmeSource.Should().Contain("deploy/scripts/deploy-release.sh");
        readmeSource.Should().Contain("deploy/scripts/rollback-release.sh");
        readmeSource.Should().Contain("deploy/releases/current-release.env");
        readmeSource.Should().Contain("deploy/cron/iiot-backup.cron.example");
        readmeSource.Should().Contain("deploy/cron/iiot-backup-verify.cron.example");
        readmeSource.Should().Contain("deploy/cron/iiot-post-release-cleanup.cron.example");
        readmeSource.Should().Contain("daily backup at `02:30`");
        readmeSource.Should().Contain("weekly restore verification at `03:30` every Sunday");
        readmeSource.Should().Contain("`latest` 不能作为生产应用版本");

        envExampleSource.Should().Contain("Must replace: Harbor application image repositories");
        envExampleSource.Should().Contain("Must replace: runtime secrets");
        envExampleSource.Should().Contain("Template defaults: single-machine published ports");
        envExampleSource.Should().Contain("operator-managed .env");
        envExampleSource.Should().Contain("pushed to Harbor");
        envExampleSource.Should().Contain("Infrastructure__EventBus__EndpointPrefix=");
        envExampleSource.Should().Contain("IIOT_HTTPAPI_IMAGE=harbor.example.com/iiot/iiot-httpapi:sha-");
        envExampleSource.Should().Contain("IIOT_GATEWAY_IMAGE=harbor.example.com/iiot/iiot-gateway:sha-");
        envExampleSource.Should().Contain("IIOT_DATAWORKER_IMAGE=harbor.example.com/iiot/iiot-dataworker:sha-");
        envExampleSource.Should().Contain("IIOT_MIGRATION_IMAGE=harbor.example.com/iiot/iiot-migrationworkapp:sha-");
        envExampleSource.Should().Contain("IIOT_WEB_IMAGE=harbor.example.com/iiot/iiot-web:sha-");
        envExampleSource.Should().NotContain("IIOT_HTTPAPI_IMAGE=harbor.example.com/iiot/iiot-httpapi:latest");
        envExampleSource.Should().Contain("IIOT_NGINX_IMAGE=harbor.example.com/mirror/nginx:1.27-alpine");
        envExampleSource.Should().Contain("IIOT_POSTGRES_IMAGE=harbor.example.com/mirror/timescaledb:latest-pg17");
        envExampleSource.Should().Contain("IIOT_REDIS_IMAGE=harbor.example.com/mirror/redis:7.4-alpine");
        envExampleSource.Should().Contain("IIOT_RABBITMQ_IMAGE=harbor.example.com/mirror/rabbitmq:3-management-alpine");
        envExampleSource.Should().Contain("IIOT_SEQ_IMAGE=harbor.example.com/mirror/seq:2024.3");
        envExampleSource.Should().NotContain("IIOT_NGINX_IMAGE=nginx:");
        envExampleSource.Should().NotContain("IIOT_POSTGRES_IMAGE=timescale/");
        envExampleSource.Should().NotContain("IIOT_REDIS_IMAGE=redis:");
        envExampleSource.Should().NotContain("IIOT_RABBITMQ_IMAGE=rabbitmq:");
        envExampleSource.Should().NotContain("IIOT_SEQ_IMAGE=datalust/");
        envExampleSource.Should().Contain("BACKUP_RETENTION_DAYS=14");
        envExampleSource.Should().Contain("BACKUP_MAX_AGE_HOURS=24");
        envExampleSource.Should().Contain("BACKUP_VERIFY_MAX_AGE_DAYS=7");
        envExampleSource.Should().Contain("GATEWAY_HTTP_PORT=81");
        envExampleSource.Should().Contain("BOOTSTRAP_AUTH_REQUIRE_SECRET=true");
        envExampleSource.Should().Contain("X-IIoT-Bootstrap-Secret");
        envExampleSource.Should().Contain("installers/stable/{version}");
        envExampleSource.Should().Contain("POSTGRES_MEM_LIMIT=4g");
        envExampleSource.Should().Contain("HTTPAPI_MEM_LIMIT=1536m");
        envExampleSource.Should().Contain("DATAWORKER_MEM_LIMIT=1536m");
        envExampleSource.Should().Contain("OUTBOX_BATCH_SIZE=500");
        envExampleSource.Should().Contain("OUTBOX_POLLING_INTERVAL_SECONDS=1");
        envExampleSource.Should().Contain("PASS_STATION_CONSUMER_CONCURRENCY=16");
        envExampleSource.Should().Contain("RATE_LIMIT_PASS_STATION_UPLOAD_TOKEN_LIMIT=3000");

        composeSource.Should().Contain("Single-machine production starter for IIoT.CloudPlatform.");
        composeSource.Should().Contain("Single-node launch keeps one explicit upstream destination.");
        composeSource.Should().Contain("Infrastructure__EventBus__EndpointPrefix:");
        composeSource.Should().Contain("BootstrapAuth__RequireSecret: ${BOOTSTRAP_AUTH_REQUIRE_SECRET}");
        composeSource.Should().Contain("EdgeInstallerArtifacts__RootPath: /app/edge-updates/installers");
        composeSource.Should().Contain("EdgeInstallerArtifacts__VelopackReleasesBaseUrl: ${PUBLIC_BASE_URL}/edge-updates/velopack");
        composeSource.Should().Contain("${EDGE_UPDATES_DIR:-/srv/iiot/edge-updates}:/app/edge-updates:rw");
        composeSource.Should().Contain("${EDGE_UPDATES_DIR:-/srv/iiot/edge-updates}:/usr/share/nginx/html/edge-updates:ro");
        composeSource.Should().Contain("postgres:");
        composeSource.Should().Contain("mem_limit: ${POSTGRES_MEM_LIMIT:-4g}");
        composeSource.Should().Contain("cpus: ${POSTGRES_CPUS:-2.0}");
        composeSource.Should().Contain("redis-cache:");
        composeSource.Should().Contain("mem_limit: ${REDIS_MEM_LIMIT:-512m}");
        composeSource.Should().Contain("rabbitmq:");
        composeSource.Should().Contain("mem_limit: ${RABBITMQ_MEM_LIMIT:-1g}");
        composeSource.Should().Contain("seq:");
        composeSource.Should().Contain("nginx-gateway:");
        composeSource.Should().Contain("mem_limit: ${NGINX_MEM_LIMIT:-256m}");
        composeSource.Should().Contain("mem_limit: ${HTTPAPI_MEM_LIMIT:-1536m}");
        composeSource.Should().Contain("mem_limit: ${DATAWORKER_MEM_LIMIT:-1536m}");
        composeSource.Should().Contain("Outbox__BatchSize: ${OUTBOX_BATCH_SIZE:-500}");
        composeSource.Should().Contain("Outbox__PollingIntervalSeconds: ${OUTBOX_POLLING_INTERVAL_SECONDS:-1}");
        composeSource.Should().Contain("Infrastructure__EventBus__Consumers__PassStationConcurrentMessageLimit: ${PASS_STATION_CONSUMER_CONCURRENCY:-16}");
        composeSource.Should().Contain("RateLimiting__PassStationUpload__TokenLimit: ${RATE_LIMIT_PASS_STATION_UPLOAD_TOKEN_LIMIT:-3000}");
    }

    [Fact]
    public void HarborDeployReadme_ShouldDocumentRegistryOwnershipAndPrivateServerFlow()
    {
        var documentationSource = File.ReadAllText(FindRepoFile("deploy", "README.md"));
        var envExampleSource = File.ReadAllText(FindRepoFile("deploy", ".env.example"));

        documentationSource.Should().Contain("Harbor 变量");
        documentationSource.Should().Contain("OCI_REGISTRY");
        documentationSource.Should().Contain("OCI_NAMESPACE");
        documentationSource.Should().Contain("OCI_REGISTRY_USERNAME");
        documentationSource.Should().Contain("OCI_REGISTRY_PASSWORD");
        documentationSource.Should().Contain("iiot-linux-prod");
        documentationSource.Should().Contain("github-runner");
        documentationSource.Should().Contain("Docker Hub 不作为生产依赖源");
        documentationSource.Should().Contain("mirror-third-party-images.sh");
        documentationSource.Should().Contain("docker login <OCI_REGISTRY>");
        documentationSource.Should().Contain("docker compose pull");
        envExampleSource.Should().Contain("IIOT_GATEWAY_IMAGE=harbor.example.com/iiot/iiot-gateway:sha-");
        envExampleSource.Should().Contain("IIOT_REDIS_IMAGE=harbor.example.com/mirror/redis:7.4-alpine");
    }

    [Fact]
    public void CloudProjects_ShouldReferenceSplitServiceProjectsInsteadOfLegacyCommonProject()
    {
        var servicesRoot = FindRepoFile("src", "services");
        foreach (var projectFile in Directory.GetFiles(servicesRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(projectFile);
            source.Should().NotContain("IIoT.Services.Common\\IIoT.Services.Common.csproj");
        }

        var infrastructureRoot = FindRepoFile("src", "infrastructure");
        foreach (var projectFile in Directory.GetFiles(infrastructureRoot, "*.csproj", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(projectFile);
            source.Should().NotContain("IIoT.Services.Common\\IIoT.Services.Common.csproj");
        }

        var solutionSource = File.ReadAllText(FindRepoFile("IIoT.CloudPlatform.slnx"));
        solutionSource.Should().Contain("src/services/IIoT.Services.Contracts/IIoT.Services.Contracts.csproj");
        solutionSource.Should().Contain("src/services/IIoT.Services.CrossCutting/IIoT.Services.CrossCutting.csproj");
        solutionSource.Should().NotContain("src/services/IIoT.Services.Common/IIoT.Services.Common.csproj");
    }

    [Fact]
    public void CloudSource_ShouldNotUseLegacyServicesCommonNamespaces()
    {
        var offenders = EnumerateSourceFiles("src", "*.cs")
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new { line, index })
                .Where(x => Regex.IsMatch(
                    x.line,
                    @"^\s*(global\s+using|using|namespace)\s+IIoT\.Services\.Common\.",
                    RegexOptions.CultureInvariant))
                .Select(x => $"{file}:{x.index + 1}:{x.line.Trim()}"))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void CloudSource_ShouldOnlyDeclareConnectionResourceNamesInDedicatedConstants()
    {
        var constantFile = FindRepoFile(
            "src",
            "shared",
            "IIoT.SharedKernel",
            "Configuration",
            "ConnectionResourceNames.cs");

        var offenders = EnumerateSourceFiles("src", "*.cs")
            .Where(file => !string.Equals(file, constantFile, StringComparison.OrdinalIgnoreCase))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new { line, index })
                .Where(x => Regex.IsMatch(x.line, "\"(iiot-db|eventbus)\"", RegexOptions.CultureInvariant))
                .Select(x => $"{file}:{x.index + 1}:{x.line.Trim()}"))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void NginxTemplate_ShouldRouteApiTrafficThroughGatewayOverHttp()
    {
        var source = File.ReadAllText(FindRepoFile("deploy", "nginx", "nginx.conf"));

        source.Should().Contain("listen 80;");
        source.Should().Contain("Content-Security-Policy");
        source.Should().Contain("limit_req_zone");
        source.Should().Contain("upstream gateway_pool");
        source.Should().Contain("server iiot-gateway:8080;");
        source.Should().Contain("proxy_pass http://gateway_pool;");
        source.Should().Contain("location /api/v1/bootstrap/");
        source.Should().NotContain("include /etc/nginx/proxy_params;");
        source.Should().NotContain("listen 443 ssl http2;");
        source.Should().NotContain("Strict-Transport-Security");
        source.Should().NotContain("ssl_certificate");
        source.Should().Contain("proxy_set_header X-Real-IP $remote_addr;");
        source.Should().Contain("proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;");
        source.Should().Contain("proxy_set_header X-Forwarded-Proto $scheme;");
        source.Should().Contain("proxy_set_header X-Forwarded-Host $http_host;");
        source.Should().Contain("proxy_set_header X-Request-Id $request_id;");
    }

    [Fact]
    public void DeployNginxTemplate_ShouldUseGatewayPoolStructuredLogsAndRequestIdForwarding()
    {
        var source = File.ReadAllText(FindRepoFile("deploy", "nginx", "nginx.conf"));

        source.Should().Contain("upstream gateway_pool");
        source.Should().Contain("keepalive 32;");
        source.Should().Contain("access_log /dev/stdout iiot_gateway;");
        source.Should().Contain("error_log /dev/stderr warn;");
        source.Should().Contain("request_id=$request_id");
        source.Should().Contain("upstream_addr=\"$upstream_addr\"");
        source.Should().Contain("upstream_status=\"$upstream_status\"");
        source.Should().Contain("upstream_response_time=\"$upstream_response_time\"");
        source.Should().Contain("request_time=$request_time");
        source.Should().Contain("route_path=\"$uri\"");
        source.Should().Contain("proxy_http_version 1.1;");
        source.Should().Contain("proxy_set_header Connection \"\";");
        source.Should().Contain("proxy_set_header X-Request-Id $request_id;");
        source.Should().Contain("proxy_pass http://gateway_pool;");
        source.Should().Contain("location = /internal/healthz");
        source.Should().Contain("allow 127.0.0.1;");
        source.Should().Contain("allow ::1;");
        source.Should().Contain("deny all;");
        source.Should().NotContain("proxy_set_header Connection \"upgrade\";");
        source.Should().NotContain("proxy_set_header Upgrade $http_upgrade;");
    }

    [Fact]
    public void GatewayHost_ShouldDefineYarpRoutesForHumanEdgeAndBootstrapSurfaces()
    {
        var gatewayProjectSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.Gateway", "IIoT.Gateway.csproj"));
        var gatewayProgramSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.Gateway", "Program.cs"));
        var gatewayAppSettingsSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.Gateway", "appsettings.json"));
        var gatewayMiddlewareSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.Gateway", "Infrastructure", "GatewayObservabilityMiddleware.cs"));
        var gatewayRouteCatalogSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.Gateway", "Infrastructure", "GatewayRouteCatalog.cs"));

        gatewayProjectSource.Should().Contain("Yarp.ReverseProxy");
        gatewayProjectSource.Should().Contain("IIoT.ServiceDefaults");

        gatewayProgramSource.Should().Contain("AddProblemDetails()");
        gatewayProgramSource.Should().Contain("UseExceptionHandler()");
        gatewayProgramSource.Should().Contain("AddReverseProxy()");
        gatewayProgramSource.Should().Contain("LoadFromConfig(builder.Configuration.GetSection(\"ReverseProxy\"))");
        gatewayProgramSource.Should().Contain("AddSingleton<IGatewayRouteCatalog, GatewayRouteCatalog>()");
        gatewayProgramSource.Should().Contain("UseMiddleware<GatewayObservabilityMiddleware>()");
        gatewayProgramSource.Should().Contain("MapReverseProxy()");

        gatewayAppSettingsSource.Should().Contain("\"GatewayRoutes\"");
        gatewayAppSettingsSource.Should().Contain("\"BlockedAliases\"");
        gatewayAppSettingsSource.Should().Contain("blocked-edge-bootstrap");
        gatewayAppSettingsSource.Should().Contain("blocked-human-edge-login");
        gatewayAppSettingsSource.Should().Contain("\"PathPrefix\": \"/api/v1/edge/bootstrap\"");
        gatewayAppSettingsSource.Should().Contain("\"Path\": \"/api/v1/human/identity/edge-login\"");
        gatewayAppSettingsSource.Should().Contain("/api/v1/human/{**catch-all}");
        gatewayAppSettingsSource.Should().Contain("/api/v1/public/{**catch-all}");
        gatewayAppSettingsSource.Should().Contain("/api/v1/edge/{**catch-all}");
        gatewayAppSettingsSource.Should().Contain("/api/v1/ai/read/{**catch-all}");
        gatewayAppSettingsSource.Should().Contain("/internal/healthz");
        gatewayAppSettingsSource.Should().Contain("/api/v1/bootstrap/device-instance");
        gatewayAppSettingsSource.Should().Contain("/api/v1/bootstrap/edge-login");
        gatewayAppSettingsSource.Should().Contain("/api/v1/bootstrap/edge-refresh");
        gatewayAppSettingsSource.Should().Contain("\"HealthCheck\"");
        gatewayAppSettingsSource.Should().Contain("\"Active\"");
        gatewayAppSettingsSource.Should().Contain("\"Path\": \"/internal/healthz\"");
        gatewayAppSettingsSource.Should().NotContain("legacy-edge-bootstrap-device-instance");
        gatewayAppSettingsSource.Should().NotContain("legacy-human-edge-login");
        gatewayAppSettingsSource.Should().Contain("internal-health");
        gatewayAppSettingsSource.Should().Contain("\"Set\": \"public\"");
        gatewayRouteCatalogSource.Should().Contain("GatewayRoutes:BlockedAliases");
        gatewayRouteCatalogSource.Should().Contain("ReverseProxy:Routes");
        gatewayRouteCatalogSource.Should().Contain("PathPrefix");
        gatewayRouteCatalogSource.Should().Contain("Match:Path");
        gatewayRouteCatalogSource.Should().NotContain("/api/v1/");

        gatewayMiddlewareSource.Should().Contain("route_surface={route_surface}");
        gatewayMiddlewareSource.Should().Contain("is_blocked_alias={is_blocked_alias}");
        gatewayMiddlewareSource.Should().Contain("matched_route={matched_route}");
        gatewayMiddlewareSource.Should().Contain("upstream_cluster={upstream_cluster}");
        gatewayMiddlewareSource.Should().Contain("status_code={status_code}");
        gatewayMiddlewareSource.Should().Contain("elapsed_ms={elapsed_ms}");
        gatewayMiddlewareSource.Should().Contain("StatusCodes.Status404NotFound");
    }

    [Fact]
    public void CloudOidcProvider_ShouldUseAuthorizationCodePkceAndMinimalIdentityClaims()
    {
        var dependencyInjectionSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "DependencyInjection.cs"));
        var controllerSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Oidc", "CloudOidcController.cs"));
        var gatewayAppSettingsSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.Gateway", "appsettings.json"));
        var humanIdentityControllerSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Human", "HumanIdentityController.cs"));

        dependencyInjectionSource.Should().Contain("AddOpenIddict()");
        dependencyInjectionSource.Should().Contain("AllowAuthorizationCodeFlow()");
        dependencyInjectionSource.Should().Contain("RequireProofKeyForCodeExchange()");
        dependencyInjectionSource.Should().Contain("SetAuthorizationEndpointUris(\"/connect/authorize\")");
        dependencyInjectionSource.Should().Contain("SetTokenEndpointUris(\"/connect/token\")");
        dependencyInjectionSource.Should().Contain("SetUserInfoEndpointUris(\"/connect/userinfo\")");
        dependencyInjectionSource.Should().Contain("SetEndSessionEndpointUris(\"/connect/logout\")");
        dependencyInjectionSource.Should().Contain("EnableAuthorizationEndpointPassthrough()");
        dependencyInjectionSource.Should().Contain("EnableTokenEndpointPassthrough()");
        dependencyInjectionSource.Should().Contain("EnableUserInfoEndpointPassthrough()");
        dependencyInjectionSource.Should().Contain("EnableEndSessionEndpointPassthrough()");
        dependencyInjectionSource.Should().NotContain("AllowRefreshTokenFlow");

        controllerSource.Should().Contain("[HttpGet(\"~/connect/authorize\")]");
        controllerSource.Should().Contain("[HttpPost(\"~/connect/token\")]");
        controllerSource.Should().Contain("[HttpGet(\"~/connect/userinfo\")]");
        controllerSource.Should().Contain("[HttpGet(\"~/connect/logout\")]");
        controllerSource.Should().Contain("string.IsNullOrWhiteSpace(request.State)");
        controllerSource.Should().Contain("string.IsNullOrWhiteSpace(request.Nonce)");
        controllerSource.Should().Contain("\"employee_no\"");
        controllerSource.Should().Contain("\"employee_id\"");
        controllerSource.Should().Contain("\"account_enabled\"");
        controllerSource.Should().Contain("\"employee_active\"");
        controllerSource.Should().Contain("\"status_version\"");
        controllerSource.Should().NotContain("cloud_roles");
        controllerSource.Should().NotContain("permissions");
        controllerSource.Should().NotContain("device_assignments");

        gatewayAppSettingsSource.Should().Contain("\"oidc-discovery\"");
        gatewayAppSettingsSource.Should().Contain("\"Path\": \"/.well-known/openid-configuration\"");
        gatewayAppSettingsSource.Should().Contain("\"oidc-jwks\"");
        gatewayAppSettingsSource.Should().Contain("\"Path\": \"/.well-known/jwks\"");
        gatewayAppSettingsSource.Should().Contain("\"oidc-connect\"");
        gatewayAppSettingsSource.Should().Contain("\"Path\": \"/connect/{**catch-all}\"");
        gatewayAppSettingsSource.Should().Contain("\"ai-identity\"");
        gatewayAppSettingsSource.Should().Contain("\"Path\": \"/api/v1/ai/identity/{**catch-all}\"");

        humanIdentityControllerSource.Should().Contain("ICloudOidcSessionService");
        humanIdentityControllerSource.Should().Contain("SignInAsync(");
        humanIdentityControllerSource.Should().Contain("command.EmployeeNo");
    }

    [Fact]
    public void HttpApi_ShouldReturnProblemDetailsForResultFailures()
    {
        var apiControllerBaseSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Infrastructure", "ApiControllerBase.cs"));

        apiControllerBaseSource.Should().Contain("ProblemDetails");
        apiControllerBaseSource.Should().Contain("application/problem+json");
        apiControllerBaseSource.Should().Contain("Extensions[\"errors\"]");
        apiControllerBaseSource.Should().NotContain("new { errors =");
    }

    [Fact]
    public void EdgeUploadEndpoints_ShouldDeclareRequestSizeLimits()
    {
        var deviceLogControllerSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Edge", "EdgeDeviceLogController.cs"));
        var capacityControllerSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Edge", "EdgeCapacityController.cs"));
        var passStationControllerSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Edge", "EdgePassStationController.cs"));
        var limitsSource = File.ReadAllText(
            FindRepoFile("src", "services", "IIoT.ProductionService", "Commands", "Edge", "UploadValidationLimits.cs"));
        var validatorsSource = File.ReadAllText(
            FindRepoFile("src", "services", "IIoT.ProductionService", "Validators", "ProductionCommandValidators.cs"));

        limitsSource.Should().Contain("MaxUploadRequestBodyBytes = 5 * 1024 * 1024");
        limitsSource.Should().Contain("MaxDeviceLogItems = 1000");
        limitsSource.Should().Contain("MaxPassStationItems = 1000");
        deviceLogControllerSource.Should().Contain("[RequestSizeLimit(UploadValidationLimits.MaxUploadRequestBodyBytes)]");
        capacityControllerSource.Should().Contain("[RequestSizeLimit(UploadValidationLimits.MaxUploadRequestBodyBytes)]");
        passStationControllerSource.Should().Contain("[RequestSizeLimit(UploadValidationLimits.MaxUploadRequestBodyBytes)]");
        passStationControllerSource.Should().Contain("/api/v1/edge/process-records");
        passStationControllerSource.Should().Contain("{typeKey}/batch");
        validatorsSource.Should().Contain("ReceiveDeviceLogCommandValidator");
        validatorsSource.Should().Contain("ReceiveHourlyCapacityCommandValidator");
        validatorsSource.Should().Contain("ReceivePassStationBatchCommandValidator");
    }

    [Fact]
    public void GatewayConfigAndRequestLogging_ShouldKeepSingleDestinationTimeoutAndRequestCorrelation()
    {
        var gatewayAppSettingsSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.Gateway", "appsettings.json"));
        var requestLoggingSource = File.ReadAllText(
            FindRepoFile("src", "infrastructure", "IIoT.Infrastructure", "Logging", "IIoTRequestLoggingExtensions.cs"));
        var gatewayMiddlewareSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.Gateway", "Infrastructure", "GatewayObservabilityMiddleware.cs"));

        gatewayAppSettingsSource.Should().Contain("\"ActivityTimeout\": \"00:01:00\"");
        gatewayAppSettingsSource.Should().Contain("\"primary\"");
        gatewayAppSettingsSource.Should().NotContain("\"secondary\"");

        requestLoggingSource.Should().Contain("request_id={RequestId}");
        requestLoggingSource.Should().Contain("trace_id={TraceId}");
        requestLoggingSource.Should().Contain("X-Request-Id");
        gatewayMiddlewareSource.Should().Contain("request_id={request_id}");
        gatewayMiddlewareSource.Should().Contain("trace_id={trace_id}");
    }

    [Fact]
    public void CloudCiWorkflow_ShouldKeepPushCiFastAndGateFullEndToEndBehindManualDispatch()
    {
        var workflowSource = File.ReadAllText(FindRepoFile(".github", "workflows", "cloud-ci.yml"));

        workflowSource.Should().Contain("workflow_dispatch:");
        workflowSource.Should().Contain("include_end_to_end:");
        workflowSource.Should().Contain("Run configuration guard tests");
        workflowSource.Should().Contain("--filter ConfigurationGuardTests");
        workflowSource.Should().Contain("if: github.event_name == 'workflow_dispatch' && inputs.include_end_to_end == true");
        workflowSource.Should().Contain("timeout-minutes: 15");
        workflowSource.Should().Contain("Run end-to-end tests");
        workflowSource.Should().Contain("dotnet test src/tests/IIoT.EndToEndTests/IIoT.EndToEndTests.csproj -c Release --no-build");
    }

    [Fact]
    public void CloudDeployWorkflow_ShouldUseReleaseTagInputAndSharedDeployScript()
    {
        var workflowSource = File.ReadAllText(FindRepoFile(".github", "workflows", "cloud-deploy.yml"));

        workflowSource.Should().Contain("release_tag:");
        workflowSource.Should().Contain("Release tag from push-triggered cloud-image (sha-*)");
        workflowSource.Should().Contain("Copy Deploy services input from cloud-image Step Summary; empty means full release only");
        workflowSource.Should().Contain("runs-on: [self-hosted, iiot-linux-prod]");
        workflowSource.Should().Contain("if [[ ! \"$release_tag\" =~ ^sha-[0-9a-f]+$ ]]");
        workflowSource.Should().Contain("RELEASE_TAG: ${{ inputs.release_tag }}");
        workflowSource.Should().Contain("DEPLOY_GIT_SHA: ${{ github.sha }}");
        workflowSource.Should().Contain("DEPLOY_TRIGGERED_BY: ${{ github.actor }}");
        workflowSource.Should().Contain("DEPLOY_TARGET_DIR: ${{ secrets.DEPLOY_TARGET_DIR }}");
        workflowSource.Should().Contain("DEPLOY_ENV_FILE: ${{ secrets.DEPLOY_ENV_FILE }}");
        workflowSource.Should().Contain("SEED_ADMIN_PASSWORD: ${{ secrets.SEED_ADMIN_PASSWORD }}");
        workflowSource.Should().Contain("Self-hosted runner must not run as root.");
        workflowSource.Should().Contain("rsync -a --delete");
        workflowSource.Should().Contain("printf '%s\\n' \"$DEPLOY_ENV_FILE\" > \"$DEPLOY_TARGET_DIR/.env\"");
        workflowSource.Should().Contain("replace_env_value \"$DEPLOY_TARGET_DIR/.env\" SEED_ADMIN_PASSWORD \"$SEED_ADMIN_PASSWORD\"");
        workflowSource.Should().Contain("docker login \"${{ secrets.OCI_REGISTRY }}\"");
        workflowSource.Should().Contain("services:");
        workflowSource.Should().Contain("DEPLOY_SERVICES: ${{ inputs.services }}");
        workflowSource.Should().Contain("deploy_args=(\"$RELEASE_TAG\")");
        workflowSource.Should().Contain("deploy_args+=(--services \"$DEPLOY_SERVICES\")");
        workflowSource.Should().Contain("./scripts/deploy-release.sh \"${deploy_args[@]}\"");
        workflowSource.Should().NotContain("runs-on: ubuntu-latest");
        workflowSource.Should().NotContain("appleboy/ssh-action");
        workflowSource.Should().NotContain("appleboy/scp-action");
        workflowSource.Should().NotContain("ghcr.io");
        workflowSource.Should().NotContain("envs: GITHUB_ACTOR,GITHUB_TOKEN,RELEASE_TAG,DEPLOY_GIT_SHA,DEPLOY_TRIGGERED_BY");
        workflowSource.Should().NotContain("compose run --rm iiot-migration");
        workflowSource.Should().NotContain("probe_status \"${public_base_url}/internal/healthz\" \"200\"");
    }

    [Fact]
    public void CloudImageWorkflow_ShouldUseIntranetRunnerAndHarborMirrorBaseImages()
    {
        var workflowSource = File.ReadAllText(FindRepoFile(".github", "workflows", "cloud-image.yml"));
        var harborRetentionScript = File.ReadAllText(FindRepoFile("deploy", "scripts", "harbor-retention.sh"));
        var webDockerfileSource = File.ReadAllText(FindRepoFile("src", "hosts", "IIoT.AppHost", "iiot-web.Dockerfile"));
        var backendDockerfileSources = new[]
        {
            File.ReadAllText(FindRepoFile("src", "hosts", "IIoT.HttpApi", "Dockerfile")),
            File.ReadAllText(FindRepoFile("src", "hosts", "IIoT.Gateway", "Dockerfile")),
            File.ReadAllText(FindRepoFile("src", "hosts", "IIoT.DataWorker", "Dockerfile")),
            File.ReadAllText(FindRepoFile("src", "hosts", "IIoT.MigrationWorkApp", "Dockerfile")),
        };

        workflowSource.Should().Contain("runs-on: [self-hosted, iiot-linux-prod]");
        workflowSource.Should().Contain("Self-hosted runner must not run as root.");
        workflowSource.Should().Contain("docker buildx build");
        workflowSource.Should().Contain("Prune old Harbor image tags");
        workflowSource.Should().Contain("bash deploy/scripts/harbor-retention.sh \"${{ matrix.image }}\"");
        workflowSource.Should().Contain("--build-arg \"DOTNET_SDK_IMAGE=${{ steps.registry.outputs.registry }}/mirror/dotnet-sdk:10.0.301\"");
        workflowSource.Should().Contain("--build-arg \"DOTNET_ASPNET_IMAGE=${{ steps.registry.outputs.registry }}/mirror/dotnet-aspnet:10.0.9\"");
        workflowSource.Should().Contain("--build-arg \"NODE_BASE_IMAGE=${{ steps.registry.outputs.registry }}/mirror/node:22-slim\"");
        workflowSource.Should().Contain("--build-arg \"NGINX_BASE_IMAGE=${{ steps.registry.outputs.registry }}/mirror/nginx:1.27-alpine\"");
        workflowSource.Should().Contain("      - \"src/hosts/**\"");
        workflowSource.Should().Contain("      - \"src/core/**\"");
        workflowSource.Should().Contain("      - \"src/shared/**\"");
        workflowSource.Should().Contain("      - \"src/services/**\"");
        workflowSource.Should().Contain("      - \"src/infrastructure/**\"");
        workflowSource.Should().Contain("      - \"src/ui/iiot-web/**\"");
        workflowSource.Should().Contain("      - \"deploy/docker-compose.prod.yml\"");
        workflowSource.Should().Contain("      - \"deploy/nginx/**\"");
        workflowSource.Should().NotContain("      - \"src/**\"");
        workflowSource.Should().NotContain("      - \"deploy/**\"");
        workflowSource.Should().NotContain("runs-on: ubuntu-latest");
        workflowSource.Should().NotContain("ghcr.io");
        workflowSource.Should().NotContain("docker/build-push-action");
        workflowSource.Should().NotContain("docker/metadata-action");
        workflowSource.Should().NotContain("docker/setup-buildx-action");

        harborRetentionScript.Should().Contain("HARBOR_KEEP_SHA_TAGS");
        harborRetentionScript.Should().Contain("HARBOR_KEEP_SHA_TAG");
        harborRetentionScript.Should().Contain("sha-[0-9a-f]");
        harborRetentionScript.Should().Contain("Harbor GC must run");

        webDockerfileSource.Should().Contain("ARG NODE_BASE_IMAGE=node:22-slim");
        webDockerfileSource.Should().Contain("FROM ${NODE_BASE_IMAGE} AS build");
        webDockerfileSource.Should().Contain("ARG NGINX_BASE_IMAGE=nginx:1.27-alpine");
        webDockerfileSource.Should().Contain("FROM ${NGINX_BASE_IMAGE} AS final");

        foreach (var dockerfileSource in backendDockerfileSources)
        {
            dockerfileSource.Should().Contain("ARG DOTNET_SDK_IMAGE=");
            dockerfileSource.Should().Contain("ARG DOTNET_ASPNET_IMAGE=");
            dockerfileSource.Should().Contain("FROM ${DOTNET_SDK_IMAGE} AS build");
            dockerfileSource.Should().Contain("FROM ${DOTNET_ASPNET_IMAGE} AS final");
            dockerfileSource.Should().NotContain("mcr.microsoft.com");
        }
    }

    [Fact]
    public void WebNginxTemplate_ShouldAvoidStaleSpaChunkFallbacks()
    {
        var source = File.ReadAllText(FindRepoFile("src", "hosts", "IIoT.AppHost", "iiot-web.nginx.conf"));

        source.Should().Contain("location = /index.html");
        source.Should().Contain("Cache-Control \"no-cache, no-store, must-revalidate\"");
        source.Should().Contain("location /assets/");
        source.Should().Contain("Cache-Control \"public, max-age=31536000, immutable\"");
        source.Should().Contain("try_files $uri =404;");
        source.Should().Contain("try_files $uri $uri/ /index.html;");
    }

    [Fact]
    public void DeployOperationsScripts_ShouldExistAndUseExpectedCommands()
    {
        var backupSource = File.ReadAllText(FindRepoFile("deploy", "scripts", "postgres-backup.sh"));
        var restoreSource = File.ReadAllText(FindRepoFile("deploy", "scripts", "postgres-restore.sh"));
        var verifySource = File.ReadAllText(FindRepoFile("deploy", "scripts", "postgres-verify-backup.sh"));
        var opsCheckSource = File.ReadAllText(FindRepoFile("deploy", "scripts", "ops-check.sh"));
        var releaseCommonSource = File.ReadAllText(FindRepoFile("deploy", "scripts", "release-common.sh"));
        var preDeploySource = File.ReadAllText(FindRepoFile("deploy", "scripts", "pre-deploy-check.sh"));
        var mirrorImagesSource = File.ReadAllText(FindRepoFile("deploy", "scripts", "mirror-third-party-images.sh"));
        var deployReleaseSource = File.ReadAllText(FindRepoFile("deploy", "scripts", "deploy-release.sh"));
        var postDeploySource = File.ReadAllText(FindRepoFile("deploy", "scripts", "post-deploy-check.sh"));
        var rollbackSource = File.ReadAllText(FindRepoFile("deploy", "scripts", "rollback-release.sh"));

        backupSource.Should().Contain("pg_dump -h 127.0.0.1 -Fc -U postgres -d iiot-db");
        backupSource.Should().Contain("backups/postgres");
        backupSource.Should().Contain(".sha256");
        backupSource.Should().Contain("latest-successful-backup.txt");
        backupSource.Should().Contain("BACKUP_RETENTION_DAYS");
        backupSource.Should().Contain("find \"$BACKUP_DIR\"");
        backupSource.Should().Contain("sha256sum");

        restoreSource.Should().Contain("CHECKSUM_FILE=\"$DUMP_FILE.sha256\"");
        restoreSource.Should().Contain("sha256sum -c");
        restoreSource.Should().Contain("compose stop nginx-gateway iiot-web iiot-gateway iiot-httpapi iiot-dataworker");
        restoreSource.Should().Contain("pg_restore -h 127.0.0.1 --clean --if-exists --no-owner --no-privileges -U postgres -d iiot-db");
        restoreSource.Should().Contain("compose run --rm iiot-migration");
        restoreSource.Should().Contain("\"$SCRIPT_DIR/ops-check.sh\"");

        verifySource.Should().Contain("latest-successful-verify.txt");
        verifySource.Should().Contain("locate_latest_dump");
        verifySource.Should().Contain("CHECKSUM_FILE=\"$DUMP_FILE.sha256\"");
        verifySource.Should().Contain("sha256sum -c");
        verifySource.Should().Contain("iiot-restore-verify-");
        verifySource.Should().Contain("createdb -h 127.0.0.1 -U postgres");
        verifySource.Should().Contain("dropdb -h 127.0.0.1 --if-exists -U postgres");
        verifySource.Should().Contain("select count(*) from devices;");
        verifySource.Should().Contain("select count(*) from employees;");
        verifySource.Should().Contain("select count(*) from recipes;");
        verifySource.Should().Contain("select count(*) from outbox_messages;");
        verifySource.Should().Contain("select count(*) from \"__EFMigrationsHistory\";");

        opsCheckSource.Should().Contain("curl --silent --show-error --output /dev/null --write-out '%{http_code}'");
        opsCheckSource.Should().Contain("/internal/healthz");
        opsCheckSource.Should().Contain("select count(*) from outbox_messages where processed_at_utc is null;");
        opsCheckSource.Should().Contain("rabbitmqctl list_queues -q name messages");
        opsCheckSource.Should().Contain("latest-successful-backup.txt");
        opsCheckSource.Should().Contain("latest-successful-verify.txt");
        opsCheckSource.Should().Contain("BACKUP_MAX_AGE_HOURS");
        opsCheckSource.Should().Contain("BACKUP_VERIFY_MAX_AGE_DAYS");
        opsCheckSource.Should().Contain("REQUIRE_BACKUP=${REQUIRE_BACKUP:-1}");
        opsCheckSource.Should().Contain("if [ \"$REQUIRE_BACKUP\" = \"1\" ]; then");
        opsCheckSource.Should().Contain("latest_backup_age_hours=");
        opsCheckSource.Should().Contain("latest_backup_verified_age_days=");
        opsCheckSource.Should().Contain("latest_backup_file=");
        opsCheckSource.Should().Contain("stat -c %Y");
        opsCheckSource.Should().Contain("iiot-pass-station-batches");
        opsCheckSource.Should().Contain("iiot-device-logs");
        opsCheckSource.Should().Contain("iiot-hourly-capacities");
        opsCheckSource.Should().Contain("exit 1");
        opsCheckSource.Should().Contain("exit 2");

        releaseCommonSource.Should().Contain("CURRENT_RELEASE_FILE");
        releaseCommonSource.Should().Contain("PREVIOUS_RELEASE_FILE");
        releaseCommonSource.Should().Contain("STAGED_RELEASE_FILE");
        releaseCommonSource.Should().Contain("RELEASE_HISTORY_DIR");
        releaseCommonSource.Should().Contain("ensure_release_tag");
        releaseCommonSource.Should().Contain("Application image may not use :latest");
        releaseCommonSource.Should().Contain("INFRA_IMAGE_KEYS");
        releaseCommonSource.Should().Contain("ensure_infra_images_not_docker_hub");
        releaseCommonSource.Should().Contain("Infrastructure image must be mirrored to Harbor");
        releaseCommonSource.Should().Contain("Infrastructure image must include an explicit Harbor registry");
        releaseCommonSource.Should().Contain("write_release_manifest");
        releaseCommonSource.Should().Contain("record_release_history");
        releaseCommonSource.Should().Contain("apply_app_images_to_dotenv");
        releaseCommonSource.Should().Contain("resolve_release_images_for_keys");
        releaseCommonSource.Should().Contain("apply_app_images_to_dotenv_for_keys");

        mirrorImagesSource.Should().Contain("mcr.microsoft.com/dotnet/sdk:10.0.301");
        mirrorImagesSource.Should().Contain("dotnet-sdk:10.0.301");
        mirrorImagesSource.Should().Contain("mcr.microsoft.com/dotnet/aspnet:10.0.9");
        mirrorImagesSource.Should().Contain("dotnet-aspnet:10.0.9");

        preDeploySource.Should().Contain("ensure_release_tag \"$RELEASE_TAG\"");
        preDeploySource.Should().Contain("compose config -q");
        preDeploySource.Should().Contain("require_infra_image_values");
        preDeploySource.Should().Contain("ensure_infra_images_not_docker_hub");
        preDeploySource.Should().Contain("resolve_release_images \"$RELEASE_TAG\"");
        preDeploySource.Should().Contain("ensure_target_images_not_latest");
        preDeploySource.Should().Contain("probe_status \"${public_base_url}/internal/healthz\" \"200\" 3");
        preDeploySource.Should().Contain("REQUIRE_BACKUP=0");
        preDeploySource.Should().Contain("BACKUP_MAX_AGE_HOURS=${PRE_DEPLOY_BACKUP_MAX_AGE_HOURS:-999999}");
        preDeploySource.Should().Contain("\"$SCRIPT_DIR/ops-check.sh\"");

        mirrorImagesSource.Should().Contain("MIRROR_REGISTRY=${MIRROR_REGISTRY:-10.98.90.154:80}");
        mirrorImagesSource.Should().Contain("MIRROR_NAMESPACE=${MIRROR_NAMESPACE:-mirror}");
        mirrorImagesSource.Should().Contain("timescale/timescaledb:latest-pg17");
        mirrorImagesSource.Should().Contain("redis:7.4-alpine");
        mirrorImagesSource.Should().Contain("rabbitmq:3-management-alpine");
        mirrorImagesSource.Should().Contain("datalust/seq:2024.3");
        mirrorImagesSource.Should().Contain("nginx:1.27-alpine");
        mirrorImagesSource.Should().Contain("node:22-slim");
        mirrorImagesSource.Should().Contain("docker push \"$target_image\"");

        deployReleaseSource.Should().Contain("\"$SCRIPT_DIR/pre-deploy-check.sh\" \"$RELEASE_TAG\"");
        deployReleaseSource.Should().Contain("\"$SCRIPT_DIR/postgres-backup.sh\"");
        deployReleaseSource.Should().Contain("write_release_manifest");
        deployReleaseSource.Should().Contain("normalize_services");
        deployReleaseSource.Should().Contain("apply_app_images_to_dotenv \"$TEMP_RELEASE_ENV_FILE\"");
        deployReleaseSource.Should().Contain("compose pull $SELECTED_SERVICES");
        deployReleaseSource.Should().Contain("hydrate_unselected_images_from_running_containers");
        deployReleaseSource.Should().Contain("if [ \"$key\" = \"IIOT_MIGRATION_IMAGE\" ]; then");
        deployReleaseSource.Should().Contain("read_manifest_value \"$DEPLOY_DIR/.env\" \"$key\"");
        deployReleaseSource.Should().Contain("warning: keeping placeholder image for unselected one-shot migration service");
        deployReleaseSource.Should().Contain("compose up -d --no-deps $RUNTIME_SELECTED_SERVICES");
        deployReleaseSource.Should().Contain("compose up -d $RUNTIME_SELECTED_SERVICES");
        deployReleaseSource.Should().Contain("compose run -T --rm iiot-migration");
        deployReleaseSource.Should().Contain("\"$SCRIPT_DIR/post-deploy-check.sh\"");
        deployReleaseSource.Should().Contain("cp \"$CURRENT_RELEASE_FILE\" \"$PREVIOUS_RELEASE_FILE\"");
        deployReleaseSource.Should().Contain("record_release_history");

        postDeploySource.Should().Contain("for service_name in nginx-gateway iiot-gateway iiot-httpapi iiot-dataworker iiot-web");
        postDeploySource.Should().Contain("require_running_service \"$service_name\"");
        postDeploySource.Should().Contain("probe_status \"${public_base_url}/\" \"200\"");
        postDeploySource.Should().Contain("probe_status \"${public_base_url}/internal/healthz\" \"200\"");
        postDeploySource.Should().Contain("\"$SCRIPT_DIR/ops-check.sh\"");

        rollbackSource.Should().Contain("resolve_release_file_path");
        rollbackSource.Should().Contain("load_release_images_from_manifest");
        rollbackSource.Should().Contain("compose pull iiot-httpapi iiot-gateway iiot-dataworker iiot-web");
        rollbackSource.Should().Contain("\"$SCRIPT_DIR/post-deploy-check.sh\"");
        rollbackSource.Should().Contain("cp \"$CURRENT_RELEASE_FILE\" \"$PREVIOUS_RELEASE_FILE\"");
        rollbackSource.Should().Contain("write_release_manifest");
        rollbackSource.Should().Contain("record_release_history");
        rollbackSource.Should().NotContain("iiot-migration");
        rollbackSource.Should().NotContain("postgres-restore.sh");
    }

    [Fact]
    public void OperationsManual_ShouldDocumentHealthBackupRestoreAndExitCodes()
    {
        var operationsSource = File.ReadAllText(FindRepoFile("deploy", "OPERATIONS.md"));

        operationsSource.Should().Contain("GET /internal/healthz");
        operationsSource.Should().Contain("127.0.0.1");
        operationsSource.Should().Contain("./scripts/postgres-backup.sh");
        operationsSource.Should().Contain("./scripts/postgres-restore.sh");
        operationsSource.Should().Contain("./scripts/postgres-verify-backup.sh");
        operationsSource.Should().Contain("./scripts/ops-check.sh");
        operationsSource.Should().Contain("./scripts/deploy-release.sh");
        operationsSource.Should().Contain("./scripts/rollback-release.sh");
        operationsSource.Should().Contain("iiot-linux-prod");
        operationsSource.Should().Contain("github-runner");
        operationsSource.Should().Contain("Docker Hub is not a production dependency source");
        operationsSource.Should().Contain("current-release.env");
        operationsSource.Should().Contain("previous-release.env");
        operationsSource.Should().Contain("staged-release.env");
        operationsSource.Should().Contain("history/");
        operationsSource.Should().Contain("latest-successful-backup.txt");
        operationsSource.Should().Contain("latest-successful-verify.txt");
        operationsSource.Should().Contain(".sha256");
        operationsSource.Should().Contain("02:30");
        operationsSource.Should().Contain("03:30");
        operationsSource.Should().Contain("latest_backup_age_hours");
        operationsSource.Should().Contain("latest_backup_verified_age_days");
        operationsSource.Should().Contain("latest_backup_file");
        operationsSource.Should().Contain("`0`");
        operationsSource.Should().Contain("`1`");
        operationsSource.Should().Contain("`2`");
        operationsSource.Should().Contain("Redis is treated as cache");
        operationsSource.Should().Contain("RabbitMQ queue state is not covered");
        operationsSource.Should().Contain("It only rolls back the 5 application images.");
        operationsSource.Should().Contain("It does not run database downgrade logic.");
        operationsSource.Should().Contain("Transfer to the existing database recovery flow");
        operationsSource.Should().Contain("Do not replay while `/internal/healthz` is failing");
    }

    [Fact]
    public void DeployCronTemplates_ShouldUseFixedSchedulesForBackupAndRestoreVerification()
    {
        var backupCronSource = File.ReadAllText(FindRepoFile("deploy", "cron", "iiot-backup.cron.example"));
        var verifyCronSource = File.ReadAllText(FindRepoFile("deploy", "cron", "iiot-backup-verify.cron.example"));
        var cleanupCronSource = File.ReadAllText(FindRepoFile("deploy", "cron", "iiot-post-release-cleanup.cron.example"));

        backupCronSource.Should().Contain("30 2 * * *");
        backupCronSource.Should().Contain("./scripts/postgres-backup.sh");
        backupCronSource.Should().Contain("/srv/iiot-cloud/deploy");

        verifyCronSource.Should().Contain("30 3 * * 0");
        verifyCronSource.Should().Contain("./scripts/postgres-verify-backup.sh");
        verifyCronSource.Should().Contain("/srv/iiot-cloud/deploy");

        cleanupCronSource.Should().Contain("30 4 * * 0");
        cleanupCronSource.Should().Contain("./scripts/post-release-cleanup.sh");
        cleanupCronSource.Should().Contain("/data/iiot-platform/cloud/deploy");
    }

    [Fact]
    public void RecipeDeviceIdIndexMigration_ShouldExist()
    {
        var migrationsDirectory = FindRepoFile("src", "infrastructure", "IIoT.EntityFrameworkCore", "Migrations");
        var migrationFiles = Directory.GetFiles(migrationsDirectory, "*AddRecipeDeviceIdIndex*.cs", SearchOption.TopDirectoryOnly)
            .Where(file => !file.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .ToList();

        migrationFiles.Should().ContainSingle();
        File.ReadAllText(migrationFiles[0]).Should().Contain("ix_recipes_device_id");
    }

    [Fact]
    public void HttpApi_ShouldDefineHumanEdgeAndBootstrapSwaggerGroups()
    {
        var programSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Program.cs"));
        var conventionSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Infrastructure", "OpenApi", "RouteSurfaceApiExplorerConvention.cs"));

        programSource.Should().Contain("AddSwaggerGen");
        programSource.Should().Contain("SwaggerDoc(\"human\"");
        programSource.Should().Contain("SwaggerDoc(\"edge\"");
        programSource.Should().Contain("SwaggerDoc(\"bootstrap\"");
        programSource.Should().Contain("SwaggerDoc(\"ai-read\"");
        programSource.Should().Contain("SwaggerEndpoint(\"/swagger/human/swagger.json\", \"human\")");
        programSource.Should().Contain("SwaggerEndpoint(\"/swagger/edge/swagger.json\", \"edge\")");
        programSource.Should().Contain("SwaggerEndpoint(\"/swagger/bootstrap/swagger.json\", \"bootstrap\")");
        programSource.Should().Contain("SwaggerEndpoint(\"/swagger/ai-read/swagger.json\", \"ai-read\")");
        conventionSource.Should().Contain("return \"ai-read\";");
        conventionSource.Should().Contain("return \"bootstrap\";");
        conventionSource.Should().Contain("return \"edge\";");
        conventionSource.Should().Contain("return \"human\";");
    }

    [Fact]
    public void GatewayIntegrationSurface_ShouldDocumentFormalRoutesAndRejectedAliases()
    {
        var documentSource = File.ReadAllText(FindRepoFile("deploy", "README.md"));

        documentSource.Should().Contain("/api/v1/human/*");
        documentSource.Should().Contain("/api/v1/edge/*");
        documentSource.Should().Contain("/api/v1/bootstrap/*");
        documentSource.Should().Contain("/api/v1/bootstrap/device-instance");
        documentSource.Should().Contain("/api/v1/bootstrap/edge-login");
        documentSource.Should().Contain("X-IIoT-Bootstrap-Secret");
        documentSource.Should().Contain("/api/v1/edge/bootstrap/device-instance");
        documentSource.Should().Contain("/api/v1/human/identity/edge-login");
        documentSource.Should().Contain("rejected");
    }

    [Fact]
    public void PassStationRuntime_ShouldUseUnifiedSchemaAndRepository()
    {
        var schemaSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "config", "pass-station-types.json"));
        var queryServiceSource = File.ReadAllText(
            FindRepoFile("src", "infrastructure", "IIoT.Dapper", "Production", "QueryServices", "PassStation", "PassStationRecordQueryService.cs"));
        var repositorySource = File.ReadAllText(
            FindRepoFile("src", "infrastructure", "IIoT.Dapper", "Production", "Repositories", "PassStations", "PassStationRecordRepository.cs"));

        schemaSource.Should().Contain("\"typeKey\": \"injection\"");
        schemaSource.Should().Contain("\"typeKey\": \"stacking\"");
        schemaSource.Should().Contain("\"typeKey\": \"homogenization\"");
        queryServiceSource.Should().Contain("pass_station_records");
        repositorySource.Should().Contain("payload_jsonb");
        repositorySource.Should().NotContain("pass_data_injection");
        repositorySource.Should().NotContain("pass_data_stacking");
    }

    [Fact]
    public void HumanRequestFolders_ShouldOnlyContainHumanCommandsAndQueries()
    {
        AssertRequestFolderConvention("Commands", "Human", "IHumanCommand");
        AssertRequestFolderConvention("Queries", "Human", "IHumanQuery");
    }

    [Fact]
    public void EdgeRequestFolders_ShouldOnlyContainDeviceCommandsAndQueries()
    {
        AssertRequestFolderConvention("Commands", "Edge", "IDeviceCommand");
        AssertRequestFolderConvention("Queries", "Edge", "IDeviceQuery");
    }

    [Fact]
    public void BootstrapRequestFolders_ShouldOnlyContainAnonymousBootstrapQueries()
    {
        AssertRequestFolderConvention("Queries", "Bootstrap", "IAnonymousBootstrapQuery");
    }

    [Fact]
    public void AiReadRequestFolders_ShouldOnlyContainAiReadQueries()
    {
        AssertRequestFolderConvention("Queries", "AiRead", "IAiReadQuery");
    }

    [Fact]
    public void PublicRequestFolders_ShouldOnlyContainPublicQueries()
    {
        AssertRequestFolderConvention("Queries", "Public", "IPublicQuery");
    }

    [Fact]
    public void InternalRequestFolders_ShouldOnlyContainBareCommandsAndQueries()
    {
        AssertRequestFolderConvention("Commands", "Internal", "ICommand");
        AssertRequestFolderConvention("Queries", "Internal", "IQuery");
    }

    private static void AssertRequestFolderConvention(
        string category,
        string audience,
        string expectedInterface)
    {
        var servicesRoot = FindRepoFile("src", "services");
        var invalidDeclarations = new List<string>();

        foreach (var file in Directory.GetFiles(servicesRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(path => IsUnderRequestFolder(path, category, audience)))
        {
            var source = File.ReadAllText(file);
            var declarations = ParseRequestDeclarations(source);

            if (declarations.Count == 0)
            {
                continue;
            }

            foreach (var declaration in declarations)
            {
                if (!string.Equals(declaration.InterfaceName, expectedInterface, StringComparison.Ordinal))
                {
                    invalidDeclarations.Add(
                        $"{Path.GetFileName(file)}:{declaration.RecordName} -> {declaration.InterfaceName} (expected {expectedInterface})");
                }
            }
        }

        invalidDeclarations.Should().BeEmpty();
    }

    private static bool IsUnderRequestFolder(string filePath, string category, string audience)
    {
        var separator = Path.DirectorySeparatorChar;
        var normalized = filePath.Replace(Path.AltDirectorySeparatorChar, separator);

        return normalized.Contains($"{separator}{category}{separator}{audience}{separator}", StringComparison.Ordinal)
               && !normalized.Contains($"{separator}bin{separator}", StringComparison.OrdinalIgnoreCase)
               && !normalized.Contains($"{separator}obj{separator}", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string rootSegment, string searchPattern)
    {
        var root = FindRepoFile(rootSegment);

        return Directory.GetFiles(root, searchPattern, SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static List<RequestDeclaration> ParseRequestDeclarations(string source)
    {
        var matches = Regex.Matches(
            source,
            @"public\s+(?:sealed\s+)?record\s+(?<name>\w+(?:<[^>]+>)?)\s*(?:\([^;]*?\))?\s*:\s*(?<iface>I\w+)\s*<",
            RegexOptions.Singleline);

        return matches
            .Select(match => new RequestDeclaration(
                match.Groups["name"].Value,
                match.Groups["iface"].Value))
            .ToList();
    }

    private sealed record RequestDeclaration(string RecordName, string InterfaceName);

    private static bool IsAicopilotBackendCallbackRedirectUri(string redirectUri)
    {
        return Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
               && string.Equals(
                   uri.AbsolutePath,
                   "/api/identity/cloud-oidc/callback",
                   StringComparison.Ordinal);
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

    private static string FindRepoDirectory(params string[] relativeSegments)
    {
        var filePath = FindRepoFile(relativeSegments);
        return Path.GetDirectoryName(filePath)
               ?? throw new DirectoryNotFoundException("Could not resolve repository directory.");
    }
}
