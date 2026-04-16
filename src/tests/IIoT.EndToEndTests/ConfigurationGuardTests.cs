using FluentAssertions;
using IIoT.HttpApi;
using IIoT.MigrationWorkApp.SeedData;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Text.RegularExpressions;

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
        orchestratorSource.Should().Contain("设备 Code 升级已被阻止");
        orchestratorSource.Should().Contain("CREATE UNIQUE INDEX IF NOT EXISTS ix_devices_client_code ON devices (client_code);");
        orchestratorSource.Should().Contain("ALTER TABLE devices DROP COLUMN IF EXISTS mac_address;");
    }

    [Fact]
    public void EdgeBootstrapController_ShouldKeepLegacyClientCodeQueryParameterForCompatibility()
    {
        var controllerSource = File.ReadAllText(
            FindRepoFile("src", "hosts", "IIoT.HttpApi", "Controllers", "Edge", "EdgeBootstrapController.cs"));

        controllerSource.Should().Contain("[FromQuery] string clientCode");
        controllerSource.Should().Contain("existing edge clients");
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

    private static string FindRepoFile(params string[] relativeSegments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativeSegments[0]);
            if (Directory.Exists(candidate))
            {
                return Path.Combine(current.FullName, Path.Combine(relativeSegments));
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for AppHost source inspection.");
    }

    private static string FindRepoDirectory(params string[] relativeSegments)
    {
        var filePath = FindRepoFile(relativeSegments);
        return Path.GetDirectoryName(filePath)
               ?? throw new DirectoryNotFoundException("Could not resolve repository directory.");
    }
}
