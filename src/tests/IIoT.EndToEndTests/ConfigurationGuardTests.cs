using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using IIoT.HttpApi;
using IIoT.MigrationWorkApp.SeedData;
using Microsoft.Extensions.Configuration;

namespace IIoT.EndToEndTests;

public sealed class ConfigurationGuardTests
{
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
            IIoTAppFixture.SeedAdminRealName);

        var act = () => options.RequirePassword();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{SeedAdminOptions.PasswordKey}*");
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
    public void EdgeBootstrapController_ShouldKeepLegacyClientCodeQueryParameterForCompatibility()
    {
        var controllerSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Edge", "EdgeBootstrapController.cs"));
        var programSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Program.cs"));
        var filterSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Infrastructure", "RefreshTokenResponseFilter.cs"));

        controllerSource.Should().Contain("[FromQuery] string clientCode");
        controllerSource.Should().Contain("legacy");
        controllerSource.Should().Contain("RefreshTokenResponseFilter.SetHeaders");
        controllerSource.Should().NotContain("RefreshTokenHeaderNames.ApplyTo");
        programSource.Should().Contain("RefreshTokenResponseFilter");
        filterSource.Should().Contain("IAsyncResultFilter");
        controllerSource.Should().Contain("BootstrapSecretHeaderNames.Secret");
        controllerSource.Should().Contain("string? bootstrapSecret");
    }

    [Fact]
    public void BootstrapHardeningDesign_ShouldDocumentCompatibleNextStep()
    {
        var documentSource = File.ReadAllText(FindRepoFile("docs", "bootstrap-auth-hardening.md"));
        var triageSource = File.ReadAllText(FindRepoFile("docs", "deepseek-audit-triage-2026-04-29.md"));

        documentSource.Should().Contain("预共享启动密钥");
        documentSource.Should().Contain("X-IIoT-Bootstrap-Secret");
        documentSource.Should().Contain("BootstrapAuth:RequireSecret");
        documentSource.Should().Contain("clientCode");
        documentSource.Should().Contain("DeviceId");
        triageSource.Should().Contain("A01 Bootstrap RequireSecret=false");
        triageSource.Should().Contain("不能直接把源码默认值改为 `true`");
        triageSource.Should().Contain("EdgeClient 升级后必须改为 `true`");
        triageSource.Should().Contain("PR-16");
        triageSource.Should().Contain("PR-17");
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
                    && !route.StartsWith("api/v1/edge/", StringComparison.Ordinal))
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
        httpApiConfiguration.GetValue<bool>("BootstrapAuth:RequireSecret").Should().BeFalse();
        httpApiConfiguration.GetValue<int>("Infrastructure:Postgres:CommandTimeoutSeconds").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("Infrastructure:EventBus:RetryLimit").Should().BeGreaterThanOrEqualTo(0);
        httpApiConfiguration.GetValue<int>("Infrastructure:EventBus:PrefetchMultiplier").Should().BeGreaterThan(0);

        dataWorkerConfiguration.GetValue<int>("Infrastructure:Postgres:CommandTimeoutSeconds").Should().BeGreaterThan(0);
        dataWorkerConfiguration.GetValue<int>("Infrastructure:EventBus:Consumers:PassStationConcurrentMessageLimit").Should().BeGreaterThan(0);
        dataWorkerConfiguration.GetValue<int>("Infrastructure:EventBus:Consumers:DeviceLogConcurrentMessageLimit").Should().BeGreaterThan(0);
        dataWorkerConfiguration.GetValue<int>("Infrastructure:EventBus:Consumers:HourlyCapacityConcurrentMessageLimit").Should().BeGreaterThan(0);

        migrationConfiguration.GetValue<int>("Infrastructure:Postgres:CommandTimeoutSeconds").Should().BeGreaterThan(0);
        migrationConfiguration.GetValue<int>("Infrastructure:Postgres:MaxRetryCount").Should().BeGreaterThanOrEqualTo(0);
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

        foreach (var file in eventContractFiles)
        {
            File.ReadAllText(file).Should().Contain("SchemaVersion { get; init; } = 1");
        }

        foreach (var file in consumerFiles)
        {
            File.ReadAllText(file).Should().Contain("EventSchemaVersionGuard.EnsureSupported");
        }
    }

    [Fact]
    public void DeploymentTemplates_ShouldExternalizeSecretsAndSecureDashboard()
    {
        var gitIgnoreSource = File.ReadAllText(FindRepoFile(".gitignore"));
        var envExampleSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.AppHost", "aspirate-output", ".env.example"));
        var composeSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.AppHost", "aspirate-output", "docker-compose.yaml"));

        gitIgnoreSource.Should().Contain("src/hosts/IIoT.AppHost/aspirate-output/.env");
        gitIgnoreSource.Should().Contain("aspirate-state.json");
        envExampleSource.Should().Contain("change-me-postgres-password");
        envExampleSource.Should().Contain("IIOT_GATEWAY_IMAGE");
        envExampleSource.Should().Contain("DEPLOY_HOST=");
        envExampleSource.Should().Contain("DEPLOY_USER=");
        envExampleSource.Should().Contain("DEPLOY_PORT=");
        envExampleSource.Should().Contain("STACK_NAME=");
        envExampleSource.Should().Contain("DEPLOY_DIR=");
        envExampleSource.Should().Contain("PUBLIC_BASE_URL=");
        envExampleSource.Should().Contain("ASPIRE_DASHBOARD_FRONTEND_BROWSERTOKEN");
        envExampleSource.Should().Contain("FORWARDED_HEADERS_ENABLED");
        envExampleSource.Should().Contain("FORWARDED_HEADERS_KNOWNNETWORKS__0");
        envExampleSource.Should().NotContain("SERVER_IP");
        envExampleSource.Should().NotContain("TLS_CERT_FILE");
        envExampleSource.Should().NotContain("TLS_KEY_FILE");
        envExampleSource.Should().NotContain("GATEWAY_HTTPS_PORT");

        composeSource.Should().Contain("DASHBOARD__FRONTEND__AUTHMODE: \"BrowserToken\"");
        composeSource.Should().Contain("DASHBOARD__OTLP__AUTHMODE: \"ApiKey\"");
        composeSource.Should().Contain("ASPIRE_DASHBOARD_OTLP_PRIMARYAPIKEY");
        composeSource.Should().Contain("iiot-gateway:");
        composeSource.Should().Contain("ReverseProxy__Clusters__httpapi__Destinations__primary__Address");
        composeSource.Should().Contain("ForwardedHeaders__Enabled:");
        composeSource.Should().Contain("ForwardedHeaders__KnownNetworks__0:");
        composeSource.Should().Contain("VITE_API_URL=${PUBLIC_BASE_URL}/api");
        composeSource.Should().NotContain("SERVER_IP");
        composeSource.Should().NotContain("TLS_CERT_FILE");
        composeSource.Should().NotContain("GATEWAY_HTTPS_PORT");
        composeSource.Should().NotContain("ALLOW_ANONYMOUS: \"true\"");
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
    public void DeploymentScriptsAndManual_ShouldUseStandardizedCloudSettings()
    {
        var buildPushSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.AppHost", "aspirate-output", "build-push.ps1"));
        var deploySource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.AppHost", "aspirate-output", "deploy.ps1"));
        var manualSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.AppHost", "aspirate-output", "部署操作手册.txt"));

        buildPushSource.Should().Contain("IIOT_REGISTRY");
        buildPushSource.Should().Contain(".env");
        buildPushSource.Should().NotContain("10.98.90.154");
        buildPushSource.Should().NotContain("SERVER_IP");

        deploySource.Should().Contain("DEPLOY_HOST");
        deploySource.Should().Contain("DEPLOY_USER");
        deploySource.Should().Contain("DEPLOY_PORT");
        deploySource.Should().Contain("STACK_NAME");
        deploySource.Should().Contain("DEPLOY_DIR");
        deploySource.Should().Contain("PUBLIC_BASE_URL");
        deploySource.Should().NotContain("10.98.90.154");
        deploySource.Should().NotContain("SERVER_IP");
        deploySource.Should().NotContain("\"root\"");

        manualSource.Should().Contain("PUBLIC_BASE_URL");
        manualSource.Should().Contain("DEPLOY_HOST");
        manualSource.Should().Contain("RABBITMQ_DEFAULT_USER");
        manualSource.Should().Contain("iiot-gateway");
        manualSource.Should().Contain("HTTP");
        manualSource.Should().NotContain("10.98.90.154");
        manualSource.Should().NotContain("guest/guest");
    }

    [Fact]
    public void DeployTemplates_ShouldDocumentSingleNodeSecretsAndSmokePolicy()
    {
        var readmeSource = File.ReadAllText(FindRepoFile("deploy", "README.md"));
        var envExampleSource = File.ReadAllText(FindRepoFile("deploy", ".env.example"));
        var composeSource = File.ReadAllText(FindRepoFile("deploy", "docker-compose.prod.yml"));

        readmeSource.Should().Contain("single-machine production starter");
        readmeSource.Should().Contain("`release_tag` produced by `cloud-image`");
        readmeSource.Should().Contain("`/internal/healthz` remains the production readiness probe");
        readmeSource.Should().Contain("[OPERATIONS.md](./OPERATIONS.md)");
        readmeSource.Should().Contain("deploy/scripts/deploy-release.sh");
        readmeSource.Should().Contain("deploy/scripts/rollback-release.sh");
        readmeSource.Should().Contain("deploy/releases/current-release.env");
        readmeSource.Should().Contain("deploy/cron/iiot-backup.cron.example");
        readmeSource.Should().Contain("deploy/cron/iiot-backup-verify.cron.example");
        readmeSource.Should().Contain("daily backup at `02:30`");
        readmeSource.Should().Contain("weekly restore verification at `03:30` every Sunday");
        readmeSource.Should().Contain("`latest` is not a standard production application version in this batch");

        envExampleSource.Should().Contain("Must replace: application image repositories");
        envExampleSource.Should().Contain("Must replace: runtime secrets");
        envExampleSource.Should().Contain("Template defaults: single-machine published ports");
        envExampleSource.Should().Contain("DEPLOY_ENV_FILE");
        envExampleSource.Should().Contain("Infrastructure__EventBus__EndpointPrefix=");
        envExampleSource.Should().Contain("IIOT_HTTPAPI_IMAGE=ghcr.io/example/iiot-httpapi:sha-");
        envExampleSource.Should().Contain("IIOT_GATEWAY_IMAGE=ghcr.io/example/iiot-gateway:sha-");
        envExampleSource.Should().Contain("IIOT_DATAWORKER_IMAGE=ghcr.io/example/iiot-dataworker:sha-");
        envExampleSource.Should().Contain("IIOT_MIGRATION_IMAGE=ghcr.io/example/iiot-migrationworkapp:sha-");
        envExampleSource.Should().Contain("IIOT_WEB_IMAGE=ghcr.io/example/iiot-web:sha-");
        envExampleSource.Should().NotContain("IIOT_HTTPAPI_IMAGE=ghcr.io/example/iiot-httpapi:latest");
        envExampleSource.Should().Contain("BACKUP_RETENTION_DAYS=14");
        envExampleSource.Should().Contain("BACKUP_MAX_AGE_HOURS=24");
        envExampleSource.Should().Contain("BACKUP_VERIFY_MAX_AGE_DAYS=7");
        envExampleSource.Should().Contain("GATEWAY_HTTP_PORT=81");
        envExampleSource.Should().Contain("BOOTSTRAP_AUTH_REQUIRE_SECRET=false");
        envExampleSource.Should().Contain("X-IIoT-Bootstrap-Secret");

        composeSource.Should().Contain("Single-machine production starter for IIoT.CloudPlatform.");
        composeSource.Should().Contain("Single-node launch keeps one explicit upstream destination.");
        composeSource.Should().Contain("Infrastructure__EventBus__EndpointPrefix:");
        composeSource.Should().Contain("BootstrapAuth__RequireSecret: ${BOOTSTRAP_AUTH_REQUIRE_SECRET}");
        composeSource.Should().Contain("postgres:");
        composeSource.Should().Contain("mem_limit: 1g");
        composeSource.Should().Contain("redis-cache:");
        composeSource.Should().Contain("mem_limit: 256m");
        composeSource.Should().Contain("rabbitmq:");
        composeSource.Should().Contain("mem_limit: 512m");
        composeSource.Should().Contain("seq:");
        composeSource.Should().Contain("nginx-gateway:");
        composeSource.Should().Contain("mem_limit: 128m");
    }

    [Fact]
    public void CloudConfigurationMap_ShouldDocumentConfigOwnershipAndPrecedence()
    {
        var documentationSource = File.ReadAllText(FindRepoFile("docs", "cloud-configuration-map.md"));

        documentationSource.Should().Contain("宿主运行时配置");
        documentationSource.Should().Contain("基础设施运行时配置");
        documentationSource.Should().Contain("部署模板配置");
        documentationSource.Should().Contain("本地开发 / 测试专用配置");
        documentationSource.Should().Contain("脚本参数 > .env > 进程环境变量");
        documentationSource.Should().Contain("ForwardedHeaders");
        documentationSource.Should().Contain("PermissionCache");
        documentationSource.Should().Contain("Infrastructure:Postgres");
        documentationSource.Should().Contain("Infrastructure:EventBus");
        documentationSource.Should().Contain("IIoT.Services.Contracts");
        documentationSource.Should().Contain("IIoT.Services.CrossCutting");
        documentationSource.Should().Contain("IIoT.Gateway");
        documentationSource.Should().Contain("IIOT_GATEWAY_IMAGE");
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
        var source = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.AppHost", "aspirate-output", "nginx.conf"));

        source.Should().Contain("listen 80;");
        source.Should().Contain("Content-Security-Policy");
        source.Should().Contain("limit_req_zone");
        source.Should().Contain("http://iiot-gateway:8080");
        source.Should().Contain("location /api/v1/bootstrap/");
        source.Should().NotContain("include /etc/nginx/proxy_params;");
        source.Should().NotContain("listen 443 ssl http2;");
        source.Should().NotContain("Strict-Transport-Security");
        source.Should().NotContain("ssl_certificate");
        source.Should().Contain("proxy_set_header X-Real-IP $remote_addr;");
        source.Should().Contain("proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;");
        source.Should().Contain("proxy_set_header X-Forwarded-Proto $scheme;");
        source.Should().Contain("proxy_set_header X-Forwarded-Host $host;");
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
        gatewayProgramSource.Should().Contain("UseMiddleware<GatewayObservabilityMiddleware>()");
        gatewayProgramSource.Should().Contain("MapReverseProxy()");

        gatewayAppSettingsSource.Should().Contain("/api/v1/human/{**catch-all}");
        gatewayAppSettingsSource.Should().Contain("/api/v1/edge/{**catch-all}");
        gatewayAppSettingsSource.Should().Contain("/internal/healthz");
        gatewayAppSettingsSource.Should().Contain("/api/v1/bootstrap/device-instance");
        gatewayAppSettingsSource.Should().Contain("/api/v1/bootstrap/edge-login");
        gatewayAppSettingsSource.Should().Contain("/api/v1/bootstrap/edge-refresh");
        gatewayAppSettingsSource.Should().Contain("\"HealthCheck\"");
        gatewayAppSettingsSource.Should().Contain("\"Active\"");
        gatewayAppSettingsSource.Should().Contain("\"Path\": \"/internal/healthz\"");
        gatewayAppSettingsSource.Should().Contain("legacy-edge-bootstrap-device-instance");
        gatewayAppSettingsSource.Should().Contain("legacy-human-edge-login");
        gatewayAppSettingsSource.Should().Contain("/api/v1/edge/bootstrap/device-instance");
        gatewayAppSettingsSource.Should().Contain("/api/v1/human/identity/edge-login");
        gatewayAppSettingsSource.Should().Contain("internal-health");
        gatewayAppSettingsSource.Should().Contain("X-IIoT-Deprecated-Alias");
        gatewayRouteCatalogSource.Should().Contain("/internal/healthz");
        gatewayRouteCatalogSource.Should().Contain("/api/v1/bootstrap/edge-refresh");
        gatewayRouteCatalogSource.Should().Contain("\"bootstrap-edge-refresh\"");
        gatewayRouteCatalogSource.Should().Contain("\"internal-healthz\"");
        gatewayRouteCatalogSource.Should().Contain("\"internal-health\"");

        gatewayMiddlewareSource.Should().Contain("route_surface={route_surface}");
        gatewayMiddlewareSource.Should().Contain("is_deprecated_alias={is_deprecated_alias}");
        gatewayMiddlewareSource.Should().Contain("matched_route={matched_route}");
        gatewayMiddlewareSource.Should().Contain("upstream_cluster={upstream_cluster}");
        gatewayMiddlewareSource.Should().Contain("status_code={status_code}");
        gatewayMiddlewareSource.Should().Contain("elapsed_ms={elapsed_ms}");
        gatewayMiddlewareSource.Should().Contain("GatewayRouteCatalog.ReplacementRouteHeader");
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
    public void CloudDeployWorkflow_ShouldUseReleaseTagInputAndSharedDeployScript()
    {
        var workflowSource = File.ReadAllText(FindRepoFile(".github", "workflows", "cloud-deploy.yml"));

        workflowSource.Should().Contain("release_tag:");
        workflowSource.Should().Contain("Release tag from cloud-image (sha-*)");
        workflowSource.Should().Contain("if [[ ! \"$release_tag\" =~ ^sha-[0-9a-f]+$ ]]");
        workflowSource.Should().Contain("RELEASE_TAG: ${{ inputs.release_tag }}");
        workflowSource.Should().Contain("DEPLOY_GIT_SHA: ${{ github.sha }}");
        workflowSource.Should().Contain("DEPLOY_TRIGGERED_BY: ${{ github.actor }}");
        workflowSource.Should().Contain("envs: GITHUB_ACTOR,GITHUB_TOKEN,RELEASE_TAG,DEPLOY_GIT_SHA,DEPLOY_TRIGGERED_BY");
        workflowSource.Should().Contain("chmod +x ./scripts/*.sh");
        workflowSource.Should().Contain("./scripts/deploy-release.sh \"$RELEASE_TAG\"");
        workflowSource.Should().NotContain("compose run --rm iiot-migration");
        workflowSource.Should().NotContain("probe_status \"${public_base_url}/internal/healthz\" \"200\"");
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
        var deployReleaseSource = File.ReadAllText(FindRepoFile("deploy", "scripts", "deploy-release.sh"));
        var postDeploySource = File.ReadAllText(FindRepoFile("deploy", "scripts", "post-deploy-check.sh"));
        var rollbackSource = File.ReadAllText(FindRepoFile("deploy", "scripts", "rollback-release.sh"));

        backupSource.Should().Contain("pg_dump -Fc -U postgres -d iiot-db");
        backupSource.Should().Contain("backups/postgres");
        backupSource.Should().Contain(".sha256");
        backupSource.Should().Contain("latest-successful-backup.txt");
        backupSource.Should().Contain("BACKUP_RETENTION_DAYS");
        backupSource.Should().Contain("find \"$BACKUP_DIR\"");
        backupSource.Should().Contain("sha256sum");

        restoreSource.Should().Contain("CHECKSUM_FILE=\"$DUMP_FILE.sha256\"");
        restoreSource.Should().Contain("sha256sum -c");
        restoreSource.Should().Contain("compose stop nginx-gateway iiot-web iiot-gateway iiot-httpapi iiot-dataworker");
        restoreSource.Should().Contain("pg_restore --clean --if-exists --no-owner --no-privileges -U postgres -d iiot-db");
        restoreSource.Should().Contain("compose run --rm iiot-migration");
        restoreSource.Should().Contain("\"$SCRIPT_DIR/ops-check.sh\"");

        verifySource.Should().Contain("latest-successful-verify.txt");
        verifySource.Should().Contain("locate_latest_dump");
        verifySource.Should().Contain("CHECKSUM_FILE=\"$DUMP_FILE.sha256\"");
        verifySource.Should().Contain("sha256sum -c");
        verifySource.Should().Contain("iiot-restore-verify-");
        verifySource.Should().Contain("createdb -U postgres");
        verifySource.Should().Contain("dropdb --if-exists -U postgres");
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
        releaseCommonSource.Should().Contain("write_release_manifest");
        releaseCommonSource.Should().Contain("record_release_history");
        releaseCommonSource.Should().Contain("apply_app_images_to_dotenv");

        preDeploySource.Should().Contain("ensure_release_tag \"$RELEASE_TAG\"");
        preDeploySource.Should().Contain("compose config -q");
        preDeploySource.Should().Contain("resolve_release_images \"$RELEASE_TAG\"");
        preDeploySource.Should().Contain("ensure_target_images_not_latest");
        preDeploySource.Should().Contain("probe_status \"${public_base_url}/internal/healthz\" \"200\" 3");
        preDeploySource.Should().Contain("\"$SCRIPT_DIR/ops-check.sh\"");

        deployReleaseSource.Should().Contain("\"$SCRIPT_DIR/pre-deploy-check.sh\" \"$RELEASE_TAG\"");
        deployReleaseSource.Should().Contain("\"$SCRIPT_DIR/postgres-backup.sh\"");
        deployReleaseSource.Should().Contain("write_release_manifest");
        deployReleaseSource.Should().Contain("apply_app_images_to_dotenv");
        deployReleaseSource.Should().Contain("compose pull iiot-httpapi iiot-gateway iiot-dataworker iiot-migration iiot-web");
        deployReleaseSource.Should().Contain("compose run --rm iiot-migration");
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

        backupCronSource.Should().Contain("30 2 * * *");
        backupCronSource.Should().Contain("./scripts/postgres-backup.sh");
        backupCronSource.Should().Contain("/srv/iiot-cloud/deploy");

        verifyCronSource.Should().Contain("30 3 * * 0");
        verifyCronSource.Should().Contain("./scripts/postgres-verify-backup.sh");
        verifyCronSource.Should().Contain("/srv/iiot-cloud/deploy");
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
        programSource.Should().Contain("SwaggerEndpoint(\"/swagger/human/swagger.json\", \"human\")");
        programSource.Should().Contain("SwaggerEndpoint(\"/swagger/edge/swagger.json\", \"edge\")");
        programSource.Should().Contain("SwaggerEndpoint(\"/swagger/bootstrap/swagger.json\", \"bootstrap\")");
        conventionSource.Should().Contain("return \"bootstrap\";");
        conventionSource.Should().Contain("return \"edge\";");
        conventionSource.Should().Contain("return \"human\";");
    }

    [Fact]
    public void GatewayIntegrationSurface_ShouldDocumentFormalRoutesAndDeprecatedAliases()
    {
        var documentSource = File.ReadAllText(FindRepoFile("docs", "gateway-integration-surface.md"));

        documentSource.Should().Contain("/api/v1/human/*");
        documentSource.Should().Contain("/api/v1/edge/*");
        documentSource.Should().Contain("/api/v1/bootstrap/*");
        documentSource.Should().Contain("/api/v1/bootstrap/device-instance");
        documentSource.Should().Contain("/api/v1/bootstrap/edge-login");
        documentSource.Should().Contain("/api/v1/edge/bootstrap/device-instance");
        documentSource.Should().Contain("/api/v1/human/identity/edge-login");
        documentSource.Should().Contain("deprecated");
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
