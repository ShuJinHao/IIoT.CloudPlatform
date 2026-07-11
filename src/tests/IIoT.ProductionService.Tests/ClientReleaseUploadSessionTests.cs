using IIoT.ProductionService.ClientReleases;
using Xunit;

namespace IIoT.ProductionService.Tests;

public sealed class ClientReleaseUploadSessionTests
{
    [Theory]
    [InlineData(ClientReleaseUploadKind.HostBundle)]
    [InlineData(ClientReleaseUploadKind.PluginPackage)]
    public async Task ReceiveAsync_ShouldRejectDeclaredLengthMismatchAndCleanSession(
        ClientReleaseUploadKind kind)
    {
        var edgeRoot = CreateTempRoot();
        try
        {
            var source = new ClientReleaseUploadTestSource();
            source.LoadBytes([1, 2, 3], declaredLength: 4);
            var coordinator = ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, source);
            var session = coordinator.Begin(kind);
            var stagingRoot = session.StagingRoot;

            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => session.ReceiveAsync(CancellationToken.None));

            Assert.Contains("声明长度不一致", exception.Message, StringComparison.Ordinal);
            session.Dispose();
            Assert.False(Directory.Exists(stagingRoot));
        }
        finally
        {
            DeleteTempRoot(edgeRoot);
        }
    }

    [Theory]
    [InlineData(ClientReleaseUploadKind.HostBundle)]
    [InlineData(ClientReleaseUploadKind.PluginPackage)]
    public async Task ReceiveAsync_ShouldEnforceMaximumBytesAndCleanSession(
        ClientReleaseUploadKind kind)
    {
        var edgeRoot = CreateTempRoot();
        try
        {
            var source = new ClientReleaseUploadTestSource();
            source.LoadBytes([1, 2, 3, 4, 5]);
            var coordinator = ClientReleaseUploadTestSupport.CreateCoordinator(
                edgeRoot,
                source,
                maxBundleBytes: 4);
            var session = coordinator.Begin(kind);
            var stagingRoot = session.StagingRoot;

            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => session.ReceiveAsync(CancellationToken.None));

            Assert.Contains("超过最大限制", exception.Message, StringComparison.Ordinal);
            session.Dispose();
            Assert.False(Directory.Exists(stagingRoot));
        }
        finally
        {
            DeleteTempRoot(edgeRoot);
        }
    }

    [Theory]
    [InlineData(ClientReleaseUploadKind.HostBundle)]
    [InlineData(ClientReleaseUploadKind.PluginPackage)]
    public async Task ReceiveAsync_ShouldPropagateCancellationAndCleanSession(
        ClientReleaseUploadKind kind)
    {
        var edgeRoot = CreateTempRoot();
        try
        {
            var source = new ClientReleaseUploadTestSource { CancelOnRead = true };
            source.LoadBytes([1, 2, 3]);
            var coordinator = ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, source);
            var session = coordinator.Begin(kind);
            var stagingRoot = session.StagingRoot;

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => session.ReceiveAsync(CancellationToken.None));
            session.Dispose();

            Assert.False(Directory.Exists(stagingRoot));
        }
        finally
        {
            DeleteTempRoot(edgeRoot);
        }
    }

    [Fact]
    public async Task ReceiveAsync_ShouldPreserveAuditSourceAndDeleteStagingOnDispose()
    {
        var edgeRoot = CreateTempRoot();
        try
        {
            var source = new ClientReleaseUploadTestSource();
            source.LoadBytes([1, 2, 3], auditSource: "release-gateway");
            var coordinator = ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, source);
            var session = coordinator.Begin(ClientReleaseUploadKind.HostBundle);

            var received = await session.ReceiveAsync(CancellationToken.None);

            Assert.Equal(3, received);
            Assert.Equal("release-gateway", session.AuditSource);
            Assert.True(File.Exists(session.UploadedFilePath));
            var stagingRoot = session.StagingRoot;
            session.Dispose();
            session.Dispose();
            Assert.False(Directory.Exists(stagingRoot));
        }
        finally
        {
            DeleteTempRoot(edgeRoot);
        }
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"iiot-client-release-upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
