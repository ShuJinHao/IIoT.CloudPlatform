using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FluentAssertions;
using IIoT.HttpApi;
using IIoT.MigrationWorkApp.SeedData;
using Microsoft.Extensions.Configuration;

namespace IIoT.CloudPlatform.UnitTests;

public sealed class ConfigurationValueBehaviorTests
{
    private const string KnownWeakSeedAdminPassword = "Ljh123456!";
    private const string KnownWeakJwtSecret = "iiot-cloud-jwt-secret-2026-04-22";
    private const string SeedAdminEmployeeNo = "101650";
    private const string SeedAdminRealName = "\u7CFB\u7EDF\u7BA1\u7406\u5458";
    private static readonly string SeedAdminPassword = $"UnitSeed-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}!";

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
                [SeedAdminOptions.EmployeeNoKey] = SeedAdminEmployeeNo
            })
            .Build();

        var options = SeedAdminOptions.Load(configuration);

        options.EmployeeNo.Should().Be(SeedAdminEmployeeNo);
        options.Password.Should().BeNull();
        options.RealName.Should().Be(SeedAdminRealName);
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
            SeedAdminEmployeeNo,
            null,
            SeedAdminRealName,
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
                [SeedAdminOptions.EmployeeNoKey] = SeedAdminEmployeeNo,
                [SeedAdminOptions.PasswordKey] = SeedAdminPassword
            })
            .Build();

        SeedAdminOptions.Load(defaultConfiguration).ResetPassword.Should().BeFalse();

        var resetConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SeedAdminOptions.EmployeeNoKey] = SeedAdminEmployeeNo,
                [SeedAdminOptions.PasswordKey] = SeedAdminPassword,
                [SeedAdminOptions.ResetPasswordKey] = "true"
            })
            .Build();

        SeedAdminOptions.Load(resetConfiguration).ResetPassword.Should().BeTrue();
        SeedAdminOptions.IsPasswordResetRequested(resetConfiguration).Should().BeTrue();
    }

}
