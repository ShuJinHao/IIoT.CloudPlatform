using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.Exceptions;
using MediatR;
using Xunit;

namespace IIoT.ProductionService.Tests;

public sealed class ClientReleaseUploadBehaviorTests
{
    [Fact]
    public Task PublishHostBundle_ShouldAuthorizeBeforeOpeningUploadSession()
        => AssertAuthorizationStopsBeforeUploadAsync<
            PublishEdgeReleaseBundleCommand,
            IIoT.SharedKernel.Result.Result<EdgeReleaseBundlePublishResultDto>>(
                new PublishEdgeReleaseBundleCommand(),
                ClientReleaseUploadKind.HostBundle);

    [Fact]
    public Task PublishPluginPackage_ShouldAuthorizeBeforeOpeningUploadSession()
        => AssertAuthorizationStopsBeforeUploadAsync<
            PublishEdgePluginPackageCommand,
            IIoT.SharedKernel.Result.Result<EdgePluginPackagePublishResultDto>>(
                new PublishEdgePluginPackageCommand(),
                ClientReleaseUploadKind.PluginPackage);

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
            var session = await coordinator.TryBeginAsync(kind);
            Assert.NotNull(session);
            var stagingRoot = session.StagingRoot;

            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => session.ReceiveAsync(CancellationToken.None));

            Assert.Contains("声明长度不一致", exception.Message, StringComparison.Ordinal);
            await session.DisposeAsync();
            Assert.False(Directory.Exists(stagingRoot));
            Assert.False(File.Exists(GetLockPath(edgeRoot)));
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
            var session = await coordinator.TryBeginAsync(kind);
            Assert.NotNull(session);
            var stagingRoot = session.StagingRoot;

            var exception = await Assert.ThrowsAnyAsync<Exception>(
                () => session.ReceiveAsync(CancellationToken.None));

            Assert.Contains("超过最大限制", exception.Message, StringComparison.Ordinal);
            await session.DisposeAsync();
            Assert.False(Directory.Exists(stagingRoot));
            Assert.False(File.Exists(GetLockPath(edgeRoot)));
        }
        finally
        {
            DeleteTempRoot(edgeRoot);
        }
    }

    [Theory]
    [InlineData(ClientReleaseUploadKind.HostBundle)]
    [InlineData(ClientReleaseUploadKind.PluginPackage)]
    public async Task ReceiveAsync_ShouldReleaseLockAndCleanSessionWhenCancelled(
        ClientReleaseUploadKind kind)
    {
        var edgeRoot = CreateTempRoot();
        try
        {
            var source = new ClientReleaseUploadTestSource { CancelOnRead = true };
            source.LoadBytes([1, 2, 3]);
            var coordinator = ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, source);
            var session = await coordinator.TryBeginAsync(kind);
            Assert.NotNull(session);
            var stagingRoot = session.StagingRoot;

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => session.ReceiveAsync(CancellationToken.None));
            await session.DisposeAsync();

            Assert.False(Directory.Exists(stagingRoot));
            Assert.False(File.Exists(GetLockPath(edgeRoot)));
            var retrySession = await coordinator.TryBeginAsync(kind);
            Assert.NotNull(retrySession);
            await retrySession.DisposeAsync();
        }
        finally
        {
            DeleteTempRoot(edgeRoot);
        }
    }

    [Fact]
    public async Task HostAndPluginUploads_ShouldShareOneExclusiveLock()
    {
        var edgeRoot = CreateTempRoot();
        try
        {
            var hostSource = new ClientReleaseUploadTestSource();
            var pluginSource = new ClientReleaseUploadTestSource();
            var hostCoordinator = ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, hostSource);
            var pluginCoordinator = ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, pluginSource);
            var hostSession = await hostCoordinator.TryBeginAsync(ClientReleaseUploadKind.HostBundle);
            Assert.NotNull(hostSession);

            var blockedPluginSession = await pluginCoordinator.TryBeginAsync(
                ClientReleaseUploadKind.PluginPackage);

            Assert.Null(blockedPluginSession);
            Assert.Equal(0, pluginSource.ReadCount);
            await hostSession.DisposeAsync();

            var pluginSession = await pluginCoordinator.TryBeginAsync(
                ClientReleaseUploadKind.PluginPackage);
            Assert.NotNull(pluginSession);
            await pluginSession.DisposeAsync();
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
            var session = await coordinator.TryBeginAsync(ClientReleaseUploadKind.HostBundle);
            Assert.NotNull(session);

            var received = await session.ReceiveAsync(CancellationToken.None);

            Assert.Equal(3, received);
            Assert.Equal("release-gateway", session.AuditSource);
            Assert.True(File.Exists(session.UploadedFilePath));
            var stagingRoot = session.StagingRoot;
            await session.DisposeAsync();
            Assert.False(Directory.Exists(stagingRoot));
            Assert.False(File.Exists(GetLockPath(edgeRoot)));
        }
        finally
        {
            DeleteTempRoot(edgeRoot);
        }
    }

    private static async Task AssertAuthorizationStopsBeforeUploadAsync<TRequest, TResponse>(
        TRequest request,
        ClientReleaseUploadKind kind)
        where TRequest : IRequest<TResponse>
    {
        var edgeRoot = CreateTempRoot();
        try
        {
            var source = new ClientReleaseUploadTestSource();
            source.LoadBytes([1, 2, 3]);
            var coordinator = ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, source);
            var nextCalled = false;
            var behavior = new AuthorizationBehavior<TRequest, TResponse>(
                new UnauthorizedCurrentUser(),
                new EmptyPermissionProvider());

            await Assert.ThrowsAsync<ForbiddenException>(() => behavior.Handle(
                request,
                async cancellationToken =>
                {
                    nextCalled = true;
                    await using var session = await coordinator.TryBeginAsync(kind);
                    Assert.NotNull(session);
                    await session.ReceiveAsync(cancellationToken);
                    return default!;
                },
                CancellationToken.None));

            Assert.False(nextCalled);
            Assert.Equal(0, source.ReadCount);
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, ".staging")));
            Assert.False(File.Exists(GetLockPath(edgeRoot)));
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

    private static string GetLockPath(string edgeRoot)
        => Path.Combine(edgeRoot, ".edge-release-upload.lock");

    private sealed class UnauthorizedCurrentUser : ICurrentUser
    {
        public string? Id { get; } = Guid.NewGuid().ToString();

        public string? UserName => "unauthorized-upload-user";

        public IReadOnlyCollection<string> Roles => [];

        public string? ActorType => IIoTClaimTypes.HumanActor;

        public IReadOnlyCollection<string> Permissions => [];

        public Guid? DeviceId => null;

        public bool IsAuthenticated => true;
    }

    private sealed class EmptyPermissionProvider : IPermissionProvider
    {
        public Task<IList<string>> GetPermissionsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IList<string>>([]);
    }
}
