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
        dependencyInjectionSource.Should().Contain("forwardedHeaders.Validate();");
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
    public void HttpApiAndDataWorkerAppSettings_ShouldDefineHardeningSections()
    {
        var httpApiAppSettingsPath = FindRepoFile("src", "hosts", "IIoT.HttpApi", "appsettings.json");
        var httpApiConfiguration = new ConfigurationBuilder()
            .AddJsonFile(httpApiAppSettingsPath)
            .Build();
        var dataWorkerAppSettingsPath = FindRepoFile("src", "hosts", "IIoT.DataWorker", "appsettings.json");
        var dataWorkerConfiguration = new ConfigurationBuilder()
            .AddJsonFile(dataWorkerAppSettingsPath)
            .Build();

        httpApiConfiguration.GetValue<int>("DistributedLock:LeaseSeconds").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("DistributedLock:RenewalCadenceSeconds").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("CacheSafety:FailSafeMinutes").Should().Be(30);
        httpApiConfiguration.GetValue<bool>("ForwardedHeaders:Enabled").Should().BeFalse();
        httpApiConfiguration.GetValue<int>("ForwardedHeaders:ForwardLimit").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("RateLimiting:Login:PermitLimit").Should().BeGreaterThan(0);
        httpApiConfiguration.GetValue<int>("RateLimiting:EdgeUpload:TokenLimit").Should().BeGreaterThan(0);

        dataWorkerConfiguration.GetValue<int>("Consumers:PassStationConcurrentMessageLimit").Should().BeGreaterThan(0);
        dataWorkerConfiguration.GetValue<int>("Consumers:DeviceLogConcurrentMessageLimit").Should().BeGreaterThan(0);
        dataWorkerConfiguration.GetValue<int>("Consumers:HourlyCapacityConcurrentMessageLimit").Should().BeGreaterThan(0);
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
        envExampleSource.Should().Contain("change-me-postgres-password");
        envExampleSource.Should().Contain("ASPIRE_DASHBOARD_FRONTEND_BROWSERTOKEN");
        envExampleSource.Should().Contain("FORWARDED_HEADERS_ENABLED");
        envExampleSource.Should().Contain("FORWARDED_HEADERS_KNOWNNETWORKS__0");
        composeSource.Should().Contain("DASHBOARD__FRONTEND__AUTHMODE: \"BrowserToken\"");
        composeSource.Should().Contain("DASHBOARD__OTLP__AUTHMODE: \"ApiKey\"");
        composeSource.Should().Contain("ASPIRE_DASHBOARD_OTLP_PRIMARYAPIKEY");
        composeSource.Should().Contain("ForwardedHeaders__Enabled:");
        composeSource.Should().Contain("ForwardedHeaders__KnownNetworks__0:");
        composeSource.Should().NotContain("ALLOW_ANONYMOUS: \"true\"");
    }

    [Fact]
    public void NginxTemplate_ShouldRequireHttpsSecurityHeadersAndRateLimits()
    {
        var source = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.AppHost", "aspirate-output", "nginx.conf"));

        source.Should().Contain("listen 443 ssl http2;");
        source.Should().Contain("Strict-Transport-Security");
        source.Should().Contain("Content-Security-Policy");
        source.Should().Contain("limit_req_zone");
        source.Should().Contain("return 301 https://$host$request_uri;");
        source.Should().NotContain("include /etc/nginx/proxy_params;");
        source.Should().Contain("proxy_set_header X-Real-IP $remote_addr;");
        source.Should().Contain("proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;");
        source.Should().Contain("proxy_set_header X-Forwarded-Proto $scheme;");
        source.Should().Contain("proxy_set_header X-Forwarded-Host $host;");
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
