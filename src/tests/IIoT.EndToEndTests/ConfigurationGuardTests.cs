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

        controllerSource.Should().Contain("[FromQuery] string clientCode");
        controllerSource.Should().Contain("legacy");
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
        source.Should().Contain("The server encountered an unexpected error while processing the request.");
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
        httpApiConfiguration.GetValue<int>("RateLimiting:Login:PermitLimit").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("RateLimiting:EdgeUpload:TokenLimit").Should().BeGreaterThan(0);
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

        gatewayProjectSource.Should().Contain("Yarp.ReverseProxy");
        gatewayProjectSource.Should().Contain("IIoT.ServiceDefaults");

        gatewayProgramSource.Should().Contain("AddReverseProxy()");
        gatewayProgramSource.Should().Contain("LoadFromConfig(builder.Configuration.GetSection(\"ReverseProxy\"))");
        gatewayProgramSource.Should().Contain("UseMiddleware<GatewayObservabilityMiddleware>()");
        gatewayProgramSource.Should().Contain("MapReverseProxy()");

        gatewayAppSettingsSource.Should().Contain("/api/v1/human/{**catch-all}");
        gatewayAppSettingsSource.Should().Contain("/api/v1/edge/{**catch-all}");
        gatewayAppSettingsSource.Should().Contain("/api/v1/bootstrap/device-instance");
        gatewayAppSettingsSource.Should().Contain("/api/v1/bootstrap/edge-login");
        gatewayAppSettingsSource.Should().Contain("legacy-edge-bootstrap-device-instance");
        gatewayAppSettingsSource.Should().Contain("legacy-human-edge-login");
        gatewayAppSettingsSource.Should().Contain("/api/v1/edge/bootstrap/device-instance");
        gatewayAppSettingsSource.Should().Contain("/api/v1/human/identity/edge-login");
        gatewayAppSettingsSource.Should().Contain("X-IIoT-Deprecated-Alias");

        gatewayMiddlewareSource.Should().Contain("route_surface={route_surface}");
        gatewayMiddlewareSource.Should().Contain("is_deprecated_alias={is_deprecated_alias}");
        gatewayMiddlewareSource.Should().Contain("matched_route={matched_route}");
        gatewayMiddlewareSource.Should().Contain("upstream_cluster={upstream_cluster}");
        gatewayMiddlewareSource.Should().Contain("status_code={status_code}");
        gatewayMiddlewareSource.Should().Contain("elapsed_ms={elapsed_ms}");
        gatewayMiddlewareSource.Should().Contain("GatewayRouteCatalog.ReplacementRouteHeader");
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
    public void PassStationSqlContracts_ShouldRemainInternalToDapper()
    {
        var queryContractSource = File.ReadAllText(
            FindRepoFile("src", "infrastructure", "IIoT.Dapper", "Production", "QueryServices", "PassStation", "IPassStationQuerySql.cs"));
        var writeContractSource = File.ReadAllText(
            FindRepoFile("src", "infrastructure", "IIoT.Dapper", "Production", "Repositories", "PassStations", "IPassStationWriteSql.cs"));

        queryContractSource.Should().Contain("internal interface IPassStationQuerySql");
        writeContractSource.Should().Contain("internal interface IPassStationWriteSql");
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
