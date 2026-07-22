using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.ClientReleases;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IIoT.CloudPlatform.Persistence.PostgresTests;

/// <summary>
/// 永久删除操作的 PostgreSQL 持久化映射验证：文件事实（类型/SHA256/大小）、管理员身份、
/// 两阶段状态与文件集合加载（GetByChannelAsync 必须 Include(Files)，否则 catalog 防护不生效）。
/// </summary>
[Collection(PostgresPersistenceIntegrationCollection.Name)]
public sealed class ClientReleaseComponentDeletionPostgresTests(
    ClientReleaseCommitRecoveryPostgresFixture fixture)
{
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
        deletion.MarkCleanupCompleted();

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
            Assert.Equal(
                ["1.0.0", "2.0.0"],
                System.Text.Json.JsonSerializer.Deserialize<string[]>(byId.VersionsJson));

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
