using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.Exceptions;
using IIoT.SharedKernel.Result;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIoT.CloudPlatform.WorkflowTests;

public sealed class ClientReleasePublishLeaseTests
{
    [Fact]
    public Task HostPublish_ShouldAuthorizeBeforeLeaseAndBodyRead()
        => AssertAuthorizationStopsBeforeLeaseAndUploadAsync<
            PublishEdgeReleaseBundleCommand,
            Result<EdgeReleaseBundlePublishResultDto>>(
                new PublishEdgeReleaseBundleCommand(),
                ClientReleaseUploadKind.HostBundle);

    [Fact]
    public Task PluginPublish_ShouldAuthorizeBeforeLeaseAndBodyRead()
        => AssertAuthorizationStopsBeforeLeaseAndUploadAsync<
            PublishEdgePluginPackageCommand,
            Result<EdgePluginPackagePublishResultDto>>(
                new PublishEdgePluginPackageCommand(),
                ClientReleaseUploadKind.PluginPackage);

    [Fact]
    public async Task HostAndPluginPublish_ShouldUseOneAsyncFiveSecondLeaseAcrossScopes()
    {
        var edgeRoot = CreateTempRoot();
        try
        {
            var pluginSource = new ClientReleaseUploadTestSource();
            pluginSource.LoadBytes([1, 2, 3]);
            var pluginCoordinator = ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, pluginSource);
            var lockService = new ControllableDistributedLockService();
            lockService.PauseNextAcquire();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IDistributedLockService>(lockService);
            services.AddScoped<DistributedLockBehavior<
                PublishEdgeReleaseBundleCommand,
                Result<EdgeReleaseBundlePublishResultDto>>>();
            services.AddScoped<DistributedLockBehavior<
                PublishEdgePluginPackageCommand,
                Result<EdgePluginPackagePublishResultDto>>>();
            using var provider = services.BuildServiceProvider();
            using var hostScope = provider.CreateScope();
            using var pluginScope = provider.CreateScope();
            var hostBehavior = hostScope.ServiceProvider.GetRequiredService<DistributedLockBehavior<
                PublishEdgeReleaseBundleCommand,
                Result<EdgeReleaseBundlePublishResultDto>>>();
            var pluginBehavior = pluginScope.ServiceProvider.GetRequiredService<DistributedLockBehavior<
                PublishEdgePluginPackageCommand,
                Result<EdgePluginPackagePublishResultDto>>>();
            var hostEntered = NewSignal();
            var releaseHost = NewSignal();

            var hostTask = hostBehavior.Handle(
                new PublishEdgeReleaseBundleCommand(),
                async cancellationToken =>
                {
                    hostEntered.TrySetResult();
                    await releaseHost.Task.WaitAsync(cancellationToken);
                    return default!;
                },
                CancellationToken.None);

            await lockService.AcquireStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(hostTask.IsCompleted);
            lockService.ContinueAcquire();
            await hostEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var pluginNextCalled = false;
            await Assert.ThrowsAsync<DistributedLockConflictException>(() => pluginBehavior.Handle(
                new PublishEdgePluginPackageCommand(),
                async cancellationToken =>
                {
                    pluginNextCalled = true;
                    using var session = pluginCoordinator.Begin(ClientReleaseUploadKind.PluginPackage);
                    await session.ReceiveAsync(cancellationToken);
                    return default!;
                },
                CancellationToken.None));

            Assert.False(pluginNextCalled);
            Assert.Equal(0, pluginSource.ReadCount);
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, ".staging")));
            Assert.Equal(2, lockService.Requests.Count);
            var resource = Assert.Single(
                lockService.Requests.Select(request => request.Resource).Distinct(StringComparer.Ordinal));
            Assert.Equal("iiot:lock:client-release:publish", resource);
            Assert.All(
                lockService.Requests,
                request => Assert.Equal(TimeSpan.FromSeconds(5), request.AcquireTimeout));

            releaseHost.TrySetResult();
            await hostTask.WaitAsync(TimeSpan.FromSeconds(2));

            var retryNextCalled = false;
            await pluginBehavior.Handle(
                new PublishEdgePluginPackageCommand(),
                _ =>
                {
                    retryNextCalled = true;
                    return Task.FromResult<Result<EdgePluginPackagePublishResultDto>>(default!);
                },
                CancellationToken.None);
            Assert.True(retryNextCalled);
        }
        finally
        {
            DeleteTempRoot(edgeRoot);
        }
    }

    [Fact]
    public async Task OwnershipLoss_ShouldCancelUploadCleanSessionAndSurfaceLeaseFailure()
    {
        var edgeRoot = CreateTempRoot();
        try
        {
            var source = new ClientReleaseUploadTestSource();
            source.LoadBytes([1, 2, 3]);
            var coordinator = ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, source);
            var lockService = new ControllableDistributedLockService();
            var behavior = CreateHostLockBehavior(lockService);
            string? stagingRoot = null;

            await Assert.ThrowsAsync<DistributedLockOwnershipLostException>(() => behavior.Handle(
                new PublishEdgeReleaseBundleCommand(),
                async cancellationToken =>
                {
                    using var session = coordinator.Begin(ClientReleaseUploadKind.HostBundle);
                    stagingRoot = session.StagingRoot;
                    lockService.SignalOwnershipLost();
                    await session.ReceiveAsync(cancellationToken);
                    return default!;
                },
                CancellationToken.None));

            Assert.NotNull(stagingRoot);
            Assert.False(Directory.Exists(stagingRoot));
        }
        finally
        {
            DeleteTempRoot(edgeRoot);
        }
    }

    [Fact]
    public async Task CallerCancellation_ShouldRemainCancellationAndCleanSession()
    {
        var edgeRoot = CreateTempRoot();
        try
        {
            var source = new ClientReleaseUploadTestSource();
            source.LoadBytes([1, 2, 3]);
            var coordinator = ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, source);
            var lockService = new ControllableDistributedLockService();
            var behavior = CreateHostLockBehavior(lockService);
            using var callerCancellation = new CancellationTokenSource();
            string? stagingRoot = null;

            await Assert.ThrowsAsync<OperationCanceledException>(() => behavior.Handle(
                new PublishEdgeReleaseBundleCommand(),
                async cancellationToken =>
                {
                    using var session = coordinator.Begin(ClientReleaseUploadKind.HostBundle);
                    stagingRoot = session.StagingRoot;
                    callerCancellation.Cancel();
                    await session.ReceiveAsync(cancellationToken);
                    return default!;
                },
                callerCancellation.Token));

            Assert.NotNull(stagingRoot);
            Assert.False(Directory.Exists(stagingRoot));
        }
        finally
        {
            DeleteTempRoot(edgeRoot);
        }
    }

    private static async Task AssertAuthorizationStopsBeforeLeaseAndUploadAsync<TRequest, TResponse>(
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
            var lockService = new ControllableDistributedLockService();
            var lockBehavior = new DistributedLockBehavior<TRequest, TResponse>(
                lockService,
                NullLogger<DistributedLockBehavior<TRequest, TResponse>>.Instance);
            var authorizationBehavior = new AuthorizationBehavior<TRequest, TResponse>(
                new UnauthorizedCurrentUser(),
                new EmptyPermissionProvider());
            var handlerCalled = false;

            await Assert.ThrowsAsync<ForbiddenException>(() => authorizationBehavior.Handle(
                request,
                cancellationToken => lockBehavior.Handle(
                    request,
                    async leaseCancellationToken =>
                    {
                        handlerCalled = true;
                        using var session = coordinator.Begin(kind);
                        await session.ReceiveAsync(leaseCancellationToken);
                        return default!;
                    },
                    cancellationToken),
                CancellationToken.None));

            Assert.False(handlerCalled);
            Assert.Empty(lockService.Requests);
            Assert.Equal(0, source.ReadCount);
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, ".staging")));
        }
        finally
        {
            DeleteTempRoot(edgeRoot);
        }
    }

    private static DistributedLockBehavior<
        PublishEdgeReleaseBundleCommand,
        Result<EdgeReleaseBundlePublishResultDto>> CreateHostLockBehavior(
            IDistributedLockService lockService)
        => new(
            lockService,
            NullLogger<DistributedLockBehavior<
                PublishEdgeReleaseBundleCommand,
                Result<EdgeReleaseBundlePublishResultDto>>>.Instance);

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"iiot-client-release-lease-{Guid.NewGuid():N}");
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

    private sealed class ControllableDistributedLockService : IDistributedLockService
    {
        private readonly object sync = new();
        private readonly TaskCompletionSource continueAcquire = NewSignal();
        private bool pauseNextAcquire;
        private TestLease? activeLease;

        public TaskCompletionSource AcquireStarted { get; } = NewSignal();

        public List<LockRequest> Requests { get; } = [];

        public void PauseNextAcquire()
        {
            pauseNextAcquire = true;
        }

        public void ContinueAcquire()
        {
            continueAcquire.TrySetResult();
        }

        public void SignalOwnershipLost()
        {
            lock (sync)
            {
                Assert.NotNull(activeLease);
                activeLease.SignalOwnershipLost();
            }
        }

        public async Task<IDistributedLockLease> AcquireAsync(
            string resource,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                Requests.Add(new LockRequest(resource, acquireTimeout));
            }

            if (pauseNextAcquire)
            {
                pauseNextAcquire = false;
                AcquireStarted.TrySetResult();
                await continueAcquire.Task.WaitAsync(cancellationToken);
            }
            else
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
            }

            lock (sync)
            {
                if (activeLease is not null)
                {
                    throw new DistributedLockConflictException();
                }

                activeLease = new TestLease(this);
                return activeLease;
            }
        }

        private void Release(TestLease lease)
        {
            lock (sync)
            {
                if (ReferenceEquals(activeLease, lease))
                {
                    activeLease = null;
                }
            }
        }

        public sealed record LockRequest(string Resource, TimeSpan? AcquireTimeout);

        private sealed class TestLease(ControllableDistributedLockService owner) : IDistributedLockLease
        {
            private readonly CancellationTokenSource ownershipLost = new();
            private int disposeStarted;

            public CancellationToken OwnershipLost => ownershipLost.Token;

            public void SignalOwnershipLost()
            {
                ownershipLost.Cancel();
            }

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref disposeStarted, 1) == 0)
                {
                    owner.Release(this);
                    ownershipLost.Dispose();
                }

                return ValueTask.CompletedTask;
            }
        }
    }
}
