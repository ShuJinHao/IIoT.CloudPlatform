using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.CloudPlatform.UnitTests;

public sealed class ClientReleaseSemanticVersionTests
{
    [Theory]
    [InlineData("0.0.0")]
    [InlineData("2.0.14")]
    [InlineData("2.0.14-alpha")]
    [InlineData("2.0.14-alpha.1")]
    [InlineData("2.0.14-rc-1")]
    public void TryParse_ShouldAcceptStrictSupportedVersions(string value)
    {
        Assert.True(ClientReleaseSemanticVersion.TryParse(value, out var parsed));
        Assert.Equal(value, parsed!.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 2.0.14")]
    [InlineData("2.0.14 ")]
    [InlineData("2")]
    [InlineData("2.0")]
    [InlineData("02.0.14")]
    [InlineData("2.00.14")]
    [InlineData("2.0.014")]
    [InlineData("2.0.14-")]
    [InlineData("2.0.14-alpha..1")]
    [InlineData("2.0.14-alpha.01")]
    [InlineData("2.0.14+")]
    [InlineData("2.0.14+build.1")]
    [InlineData("2.0.14-预发布")]
    public void TryParse_ShouldRejectUnsupportedOrAmbiguousVersions(string value)
    {
        Assert.False(ClientReleaseSemanticVersion.TryParse(value, out _));
    }

    [Fact]
    public void Compare_ShouldFollowSemanticPrereleasePrecedence()
    {
        string[] ordered =
        [
            "1.0.0-alpha",
            "1.0.0-alpha.1",
            "1.0.0-alpha.beta",
            "1.0.0-beta",
            "1.0.0-beta.2",
            "1.0.0-beta.11",
            "1.0.0-rc.1",
            "1.0.0",
            "2.0.0"
        ];

        for (var index = 0; index < ordered.Length - 1; index++)
        {
            Assert.True(
                ClientReleaseSemanticVersion.Compare(
                    ordered[index],
                    ordered[index + 1]) < 0);
        }
    }

    [Fact]
    public void IsInRange_ShouldUseTheSameStrictComparison()
    {
        Assert.True(ClientReleaseSemanticVersion.IsInRange(
            "2.0.14-rc.2",
            "2.0.14-rc.1",
            "2.0.14"));
        Assert.False(ClientReleaseSemanticVersion.IsInRange(
            "2.0.15",
            "2.0.14-rc.1",
            "2.0.14"));
    }

    [Fact]
    public void Aggregate_ShouldRejectInvalidVersionBeforeAddingRelease()
    {
        var component = ClientReleaseComponent.CreatePlugin(
            "AP",
            "AP",
            null,
            null,
            null,
            "stable",
            "win-x64");

        Assert.Throws<ArgumentException>(() => component.UpsertPluginVersion(
            "02.0.19",
            "2.0.0",
            "2.0.14",
            "2.0.14",
            "net10.0",
            "/edge-updates/plugins/stable/AP/02.0.19/AP.zip",
            new string('a', 64),
            1,
            "invalid",
            "[]",
            ClientReleaseStatus.Published,
            null,
            "tester"));

        Assert.Empty(component.Versions);
    }
}
