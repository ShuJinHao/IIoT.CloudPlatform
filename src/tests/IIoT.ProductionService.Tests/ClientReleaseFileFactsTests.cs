using IIoT.ProductionService.ClientReleases;
using Xunit;

namespace IIoT.ProductionService.Tests;

public sealed class ClientReleaseFileFactsTests
{
    [Fact]
    public void IsStrictChildPath_ShouldRejectParentAndSiblingPrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), $"iiot-file-facts-{Guid.NewGuid():N}");

        Assert.True(ClientReleaseFileFacts.IsStrictChildPath(root, Path.Combine(root, "child")));
        Assert.False(ClientReleaseFileFacts.IsStrictChildPath(root, root));
        Assert.False(ClientReleaseFileFacts.IsStrictChildPath(root, $"{root}-sibling"));
    }

    [Fact]
    public void FileHashAndExactFact_ShouldUseLowercaseShaAndRejectWrongSize()
    {
        var path = Path.Combine(Path.GetTempPath(), $"iiot-file-facts-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllText(path, "abc");

            const string expectedSha = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
            Assert.Equal(expectedSha, ClientReleaseFileFacts.ComputeSha256(path));
            Assert.True(ClientReleaseFileFacts.IsExactRegularFile(
                path,
                expectedSha.ToUpperInvariant(),
                new FileInfo(path).Length));
            Assert.False(ClientReleaseFileFacts.IsExactRegularFile(
                path,
                expectedSha,
                new FileInfo(path).Length + 1));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
