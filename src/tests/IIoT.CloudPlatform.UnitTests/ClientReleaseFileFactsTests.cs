using IIoT.ProductionService.ClientReleases;
using Xunit;

namespace IIoT.CloudPlatform.UnitTests;

public sealed class ClientReleaseFileFactsTests
{
    [Fact]
    public void IsSha256_ShouldAcceptBothHexCasesAndRejectMalformedValues()
    {
        Assert.True(ClientReleaseFileFacts.IsSha256(new string('a', 64)));
        Assert.True(ClientReleaseFileFacts.IsSha256(new string('F', 64)));
        Assert.False(ClientReleaseFileFacts.IsSha256(new string('g', 64)));
        Assert.False(ClientReleaseFileFacts.IsSha256(new string('a', 63)));
        Assert.False(ClientReleaseFileFacts.IsSha256(null));
    }

    [Fact]
    public void IsStrictChildPath_ShouldRejectParentAndSiblingPrefix()
    {
        var root = Path.GetFullPath(Path.Combine("synthetic-root", "release"));

        Assert.True(ClientReleaseFileFacts.IsStrictChildPath(root, Path.Combine(root, "child")));
        Assert.False(ClientReleaseFileFacts.IsStrictChildPath(root, root));
        Assert.False(ClientReleaseFileFacts.IsStrictChildPath(root, $"{root}-sibling"));
    }

}
