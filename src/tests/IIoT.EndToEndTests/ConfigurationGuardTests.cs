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
}
