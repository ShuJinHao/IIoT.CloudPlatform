using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Auditing;
using IIoT.EntityFrameworkCore.ClientReleases;
using IIoT.Services.Contracts.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

/// <summary>
/// 永久删除操作的 PostgreSQL 持久化映射验证：文件事实（类型/SHA256/大小）、管理员身份、
/// 两阶段状态与文件集合加载（GetByChannelAsync 必须 Include(Files)，否则 catalog 防护不生效）。
/// 同时验证“删除组件 + 写删除操作”同一事务：成功时组件消失且操作存在；提交失败时组件保留且操作不存在。
/// </summary>
[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class ClientReleaseComponentDeletionPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
    [Fact]
    public async Task EfAuditTrailService_ShouldPersistOneExactRecordPerIdempotencyKey()
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var deletionId = Guid.NewGuid();
        var key = $"client-release-hard-delete-completed:{deletionId:N}";
        var executedAtUtc = DateTime.UtcNow;
        executedAtUtc = new DateTime(
            executedAtUtc.Ticks - executedAtUtc.Ticks % 10,
            DateTimeKind.Utc);
        var entry = new AuditTrailEntry(
            Guid.NewGuid(),
            "admin-tester",
            "ClientRelease.HardDeleteComponent",
            "ClientRelease",
            deletionId.ToString(),
            executedAtUtc,
            true,
            "{\"deleted\":1,\"pathsDigest\":\"abcdef0123456789\"}",
            null,
            key);
        var service = new EfAuditTrailService(
            options,
            NullLogger<EfAuditTrailService>.Instance);

        Assert.True(await service.TryWriteConfirmedAsync(entry));
        Assert.True(await service.TryWriteConfirmedAsync(entry));
        Assert.False(await service.TryWriteConfirmedAsync(entry with { Summary = "{\"deleted\":2}" }));

        await using var verify = new IIoTDbContext(options);
        var stored = await verify.AuditTrails
            .AsNoTracking()
            .Where(record => record.IdempotencyKey == key)
            .ToListAsync();
        var audit = Assert.Single(stored);
        Assert.Equal(entry.Summary, audit.Summary);
        Assert.Equal(entry.ExecutedAtUtc, audit.ExecutedAtUtc);
    }

    [Fact]
    public async Task SameTransaction_ShouldCommitComponentDeleteAndOperationTogether()
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var channel = $"pgtxn-ok-{Guid.NewGuid():N}"[..16];
        var component = CreatePluginComponent(channel);
        var componentId = component.Id;

        // 先落库组件。
        await using (var seed = new IIoTDbContext(options))
        {
            seed.ClientReleaseComponents.Add(component);
            await seed.SaveChangesAsync();
        }

        Guid deletionId;
        // 同一 DbContext / 同一事务：删除组件元数据 + 写删除操作。
        await using (var tx = new IIoTDbContext(options))
        {
            var tracked = await tx.ClientReleaseComponents.SingleAsync(c => c.Id == componentId);
            var deletion = BuildDeletion(componentId, channel);
            deletionId = deletion.Id;
            tx.ClientReleaseComponentDeletions.Add(deletion);
            tx.ClientReleaseComponents.Remove(tracked);
            await tx.SaveChangesAsync();
        }

        // 全新观察：组件消失且操作存在。
        await using (var verify = new IIoTDbContext(options))
        {
            Assert.False(await verify.ClientReleaseComponents.AnyAsync(c => c.Id == componentId));
            var operation = await verify.ClientReleaseComponentDeletions
                .Include(d => d.Files)
                .SingleOrDefaultAsync(d => d.Id == deletionId);
            Assert.NotNull(operation);
            Assert.Equal(componentId, operation.ComponentId);
            Assert.Single(operation.Files);
        }
    }

    [Fact]
    public async Task SameTransaction_ShouldRollbackComponentDeleteAndOperation_WhenCommitFails()
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var channel = $"pgtxn-fail-{Guid.NewGuid():N}"[..16];
        var component = CreatePluginComponent(channel);
        var componentId = component.Id;

        await using (var seed = new IIoTDbContext(options))
        {
            seed.ClientReleaseComponents.Add(component);
            await seed.SaveChangesAsync();
        }

        // 同一事务里删除组件 + 写操作，但用拦截器在提交时抛错 → 整体回滚。
        var failOptions = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(new ThrowOnSaveInterceptor())
            .Options;
        var deletion = BuildDeletion(componentId, channel);
        await using (var tx = new IIoTDbContext(failOptions))
        {
            var tracked = await tx.ClientReleaseComponents.SingleAsync(c => c.Id == componentId);
            tx.ClientReleaseComponentDeletions.Add(deletion);
            tx.ClientReleaseComponents.Remove(tracked);
            await Assert.ThrowsAnyAsync<Exception>(() => tx.SaveChangesAsync());
        }

        // 全新观察：组件保留且操作不存在（事务整体回滚，不是只回滚一半）。
        await using (var verify = new IIoTDbContext(options))
        {
            Assert.True(await verify.ClientReleaseComponents.AnyAsync(c => c.Id == componentId));
            Assert.False(await verify.ClientReleaseComponentDeletions.AnyAsync(d => d.Id == deletion.Id));
            Assert.False(await verify.Set<ClientReleaseComponentDeletionFile>()
                .AnyAsync(f => f.ClientReleaseComponentDeletionId == deletion.Id));
        }
    }

    private static ClientReleaseComponent CreatePluginComponent(string channel)
    {
        var component = ClientReleaseComponent.CreatePlugin(
            $"PgTxn{Guid.NewGuid():N}"[..20],
            "PG 事务插件",
            null,
            null,
            null,
            channel,
            "win-x64");
        component.UpsertPluginVersion(
            "1.0.0",
            "1.0.0",
            "1.0.0",
            "99.0.0",
            "net10.0",
            $"/edge-updates/plugins/{channel}/m/1.0.0/p.zip",
            new string('a', 64),
            128,
            "pg txn",
            "[]",
            ClientReleaseStatus.Published,
            null,
            "IIoT",
            artifacts:
            [
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PackageFile,
                    $"plugins/{channel}/m/1.0.0/p.zip",
                    new string('a', 64),
                    128)
            ]);
        return component;
    }

    private static ClientReleaseComponentDeletion BuildDeletion(Guid componentId, string channel)
        => new(
            componentId,
            "Plugin",
            "PgTxn",
            channel,
            "win-x64",
            ["1.0.0"],
            "pg 同事务验证",
            Guid.NewGuid(),
            "admin-tester",
            [
                new ClientReleaseComponentDeletionFileTarget(
                    $"plugins/{channel}/m/1.0.0/p.zip",
                    "PackageFile",
                    new string('a', 64),
                    128)
            ]);

    private sealed class ThrowOnSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("模拟提交失败");
    }

    [Fact]
    public async Task EfStore_ShouldRoundTripFileFactsAndLoadFilesByChannel()
    {
        var connectionString = await fixture.GetConnectionStringAsync();
        var options = new DbContextOptionsBuilder<IIoTDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var unique = Guid.NewGuid().ToString("N");
        var channel = $"pgdel-{unique[..8]}";
        var componentId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var sha256 = new string('b', 64);

        var deletion = new ClientReleaseComponentDeletion(
            componentId,
            "Host",
            "win-x64",
            channel,
            "win-x64",
            ["2.0.0", "1.0.0"],
            "发布错误，管理员永久删除",
            requestedByUserId,
            "admin-tester",
            [
                new ClientReleaseComponentDeletionFileTarget(
                    "installers/stable/1.0.0/installer-artifact.json",
                    "ManifestFile",
                    sha256,
                    1234),
                new ClientReleaseComponentDeletionFileTarget(
                    $"velopack/{channel}/IIoT.EdgeClient-1.0.0-full.nupkg",
                    "VelopackFile",
                    new string('c', 64),
                    5678)
            ]);
        deletion.MarkCleanupCompleted(
            ["velopack/stable/obsolete.nupkg"],
            ["velopack/stable/shared.nupkg"],
            manifestChanged: true);

        await using (var writeContext = new IIoTDbContext(options))
        {
            var store = new EfClientReleaseComponentDeletionStore(writeContext);
            store.Add(deletion);
            await store.SaveChangesAsync();
        }

        // 全新 DbContext 读回：验证 jsonb/列映射与文件集合加载，不受内存跟踪影响。
        await using (var readContext = new IIoTDbContext(options))
        {
            var store = new EfClientReleaseComponentDeletionStore(readContext);
            var byId = await store.GetByIdAsync(deletion.Id);
            Assert.NotNull(byId);
            Assert.Equal(componentId, byId.ComponentId);
            Assert.Equal("Host", byId.ComponentKind);
            Assert.Equal("win-x64", byId.ComponentKey);
            Assert.Equal(channel, byId.Channel);
            Assert.Equal("发布错误，管理员永久删除", byId.Reason);
            Assert.Equal(requestedByUserId, byId.RequestedByUserId);
            Assert.Equal("admin-tester", byId.RequestedByUserName);
            Assert.Equal(ClientReleaseComponentDeletionStatus.CleanupCompleted, byId.Status);
            Assert.NotNull(byId.CleanupCompletedAtUtc);
            Assert.True(byId.TryGetCleanupResult(out var cleanupResult));
            Assert.NotNull(cleanupResult);
            Assert.Equal(["velopack/stable/obsolete.nupkg"], cleanupResult.DeletedPaths);
            Assert.Equal(["velopack/stable/shared.nupkg"], cleanupResult.SkippedPaths);
            Assert.True(cleanupResult.ManifestChanged);
            Assert.Equal(2, byId.Files.Count);
            var manifest = Assert.Single(
                byId.Files,
                file => file.ArtifactKind == "ManifestFile");
            Assert.Equal(sha256, manifest.Sha256);
            Assert.Equal(1234, manifest.SizeBytes);
            var nupkg = Assert.Single(
                byId.Files,
                file => file.ArtifactKind == "VelopackFile");
            Assert.Equal(new string('c', 64), nupkg.Sha256);
            Assert.Equal(5678, nupkg.SizeBytes);
            // 版本列表按序落 jsonb（jsonb 规范化空白，按内容比较）。
            var versions = System.Text.Json.JsonSerializer.Deserialize<string[]>(byId.VersionsJson)
                           ?? throw new InvalidDataException("versions_json must contain a JSON array.");
            Assert.Equal(
                ["1.0.0", "2.0.0"],
                versions);

            // catalog 防护依赖 GetByChannelAsync 返回的文件集合：必须 Include(Files)。
            var byChannel = await store.GetByChannelAsync(channel);
            var loaded = Assert.Single(byChannel);
            Assert.Equal(deletion.Id, loaded.Id);
            Assert.Equal(2, loaded.Files.Count);

            // 状态流转映射：CleanupCompleted -> Failed 带 failureCode 与 retryCount。
            loaded.MarkFailed("ManifestRebuildFailed");
            await store.SaveChangesAsync();
        }

        await using (var verifyContext = new IIoTDbContext(options))
        {
            var store = new EfClientReleaseComponentDeletionStore(verifyContext);
            var failed = await store.GetByIdAsync(deletion.Id);
            Assert.NotNull(failed);
            Assert.Equal(ClientReleaseComponentDeletionStatus.Failed, failed.Status);
            Assert.Equal("ManifestRebuildFailed", failed.FailureCode);
            Assert.Equal(1, failed.RetryCount);

            // 移除后级联删除文件行。
            store.Remove(failed);
            await store.SaveChangesAsync();
            Assert.Null(await store.GetByIdAsync(deletion.Id));
            Assert.Equal(
                0,
                await verifyContext.Set<ClientReleaseComponentDeletionFile>()
                    .CountAsync(file => file.ClientReleaseComponentDeletionId == deletion.Id));
        }
    }
}
