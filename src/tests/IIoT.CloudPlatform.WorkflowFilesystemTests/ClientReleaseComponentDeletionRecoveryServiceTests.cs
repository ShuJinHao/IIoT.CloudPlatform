using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.HttpApi.Infrastructure;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIoT.CloudPlatform.WorkflowTests;

/// <summary>
/// 启动恢复服务并发锁验证（直接驱动生产 <see cref="ClientReleaseComponentDeletionRecoveryService"/>）：
/// 与发布/硬删除/管理员重试共享同一把发布分布式锁，待清理操作必须在锁内重新读取；
/// 取锁失败由宿主兜底（ExecuteAsync 吞掉），不得阻断宿主启动。
/// </summary>
public sealed class ClientReleaseComponentDeletionRecoveryServiceTests
{
    [Fact]
    public async Task Recovery_ShouldAcquirePublishLockAndReadPendingInsideLock()
    {
        var deletion = new ClientReleaseComponentDeletion(
            Guid.NewGuid(),
            "Plugin",
            "DieCutting",
            "stable",
            "win-x64",
            ["1.0.0"],
            "recovery",
            Guid.NewGuid(),
            "admin",
            []);
        var store = new StubDeletionStore(deletion);
        var lockService = new RecordingDistributedLockService();
        var processor = new RecordingProcessor();
        var service = CreateService(store, lockService, processor);

        await service.ExecuteRecoveryAsync(CancellationToken.None);

        // 同一把发布分布式锁（资源名与超时与发布/硬删除一致）。
        var acquisition = Assert.Single(lockService.Acquisitions);
        Assert.Equal(ClientReleasePublishLock.Resource, acquisition.Resource);
        Assert.Equal(
            TimeSpan.FromSeconds(ClientReleasePublishLock.AcquireTimeoutSeconds),
            acquisition.Timeout);
        // 锁内重新读取操作：GetPending 必须发生在 Acquire 之后。
        Assert.True(
            store.GetPendingCallIndex > acquisition.Index,
            $"pending read index {store.GetPendingCallIndex} must be after lock acquire index {acquisition.Index}");
        // 锁内逐个处理待清理操作。
        Assert.Equal([deletion.Id], processor.ProcessedIds);
        Assert.True(acquisition.Lease.Disposed);
    }

    [Fact]
    public async Task Recovery_ShouldNotReadOrProcess_WhenLockAcquisitionFails()
    {
        var store = new StubDeletionStore();
        var lockService = new RecordingDistributedLockService
        {
            AcquireException = new InvalidOperationException("另一实例持有发布锁")
        };
        var processor = new RecordingProcessor();
        var service = CreateService(store, lockService, processor);

        // ExecuteAsync 是宿主入口：取锁失败必须吞掉，不阻断启动。
        await service.StartAsync(CancellationToken.None);
        if (service.ExecuteTask is not null)
        {
            await service.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await service.StopAsync(CancellationToken.None);

        Assert.Equal(0, store.GetPendingCallIndex);
        Assert.Empty(processor.ProcessedIds);
    }

    private static ClientReleaseComponentDeletionRecoveryService CreateService(
        StubDeletionStore store,
        RecordingDistributedLockService lockService,
        RecordingProcessor processor)
    {
        var provider = new ServiceCollection()
            .AddSingleton<IClientReleaseComponentDeletionStore>(store)
            .AddSingleton<IClientReleaseComponentDeletionProcessor>(processor)
            .BuildServiceProvider();
        return new ClientReleaseComponentDeletionRecoveryService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            lockService,
            NullLogger<ClientReleaseComponentDeletionRecoveryService>.Instance);
    }

    private sealed class StubDeletionStore(params ClientReleaseComponentDeletion[] pending)
        : IClientReleaseComponentDeletionStore
    {
        public int GetPendingCallIndex { get; private set; }

        public Task<ClientReleaseComponentDeletion?> GetByIdAsync(
            Guid deletionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(pending.SingleOrDefault(deletion => deletion.Id == deletionId));

        public Task<IReadOnlyList<ClientReleaseComponentDeletion>> GetPendingAsync(
            CancellationToken cancellationToken = default)
        {
            GetPendingCallIndex = RecordingDistributedLockService.NextCallIndex();
            return Task.FromResult<IReadOnlyList<ClientReleaseComponentDeletion>>(pending.ToList());
        }

        public Task<IReadOnlyList<ClientReleaseComponentDeletion>> GetByChannelAsync(
            string channel,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ClientReleaseComponentDeletion>>(pending.ToList());

        public void Add(ClientReleaseComponentDeletion deletion)
        {
        }

        public void Remove(ClientReleaseComponentDeletion deletion)
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(1);
    }

    private sealed class RecordingProcessor : IClientReleaseComponentDeletionProcessor
    {
        public List<Guid> ProcessedIds { get; } = [];

        public Task<ClientReleaseComponentDeletionOutcome> ProcessAsync(
            ClientReleaseComponentDeletion deletion,
            CancellationToken cancellationToken)
        {
            ProcessedIds.Add(deletion.Id);
            return Task.FromResult(new ClientReleaseComponentDeletionOutcome(
                true,
                [],
                [],
                null,
                false));
        }
    }

    private sealed class RecordingDistributedLockService : IDistributedLockService
    {
        private static int callIndex;

        public static int NextCallIndex() => Interlocked.Increment(ref callIndex);

        public List<(string Resource, TimeSpan? Timeout, int Index, StubLease Lease)> Acquisitions { get; } = [];

        public Exception? AcquireException { get; init; }

        public Task<IDistributedLockLease> AcquireAsync(
            string resource,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default)
        {
            var index = NextCallIndex();
            if (AcquireException is not null)
            {
                throw AcquireException;
            }

            var lease = new StubLease();
            Acquisitions.Add((resource, acquireTimeout, index, lease));
            return Task.FromResult<IDistributedLockLease>(lease);
        }
    }

    private sealed class StubLease : IDistributedLockLease
    {
        public CancellationToken OwnershipLost { get; } = CancellationToken.None;

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
