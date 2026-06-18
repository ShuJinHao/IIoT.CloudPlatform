using System.Linq.Expressions;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.ProductionService.Commands.ClientVersions;
using IIoT.ProductionService.Queries.ClientReleases;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Specification;
using Xunit;

namespace IIoT.ProductionService.Tests;

public sealed class ClientReleaseBehaviorTests
{
    [Fact]
    public async Task UpsertClientHostReleaseHandler_ShouldCreateReleaseRecord()
    {
        var repository = new InMemoryRepository<ClientHostRelease>();
        var handler = new UpsertClientHostReleaseHandler(repository, new NoopRetentionService());

        var result = await handler.Handle(
            new UpsertClientHostReleaseCommand(
                "stable",
                "1.2.0",
                "1.0.0",
                "win-x64",
                "net10.0",
                "https://example.test/releases/host.zip",
                new string('a', 64),
                1024,
                "release notes",
                "Published",
                null,
                "IIoT"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.AddedEntity);
        Assert.Equal("1.2.0", repository.AddedEntity!.Version);
        Assert.Equal(ClientReleaseStatus.Published, repository.AddedEntity.Status);
        Assert.NotNull(repository.AddedEntity.PublishedAtUtc);
    }

    [Fact]
    public async Task ReportDeviceClientVersionHandler_ShouldRejectMismatchedClientCode()
    {
        var deviceId = Guid.NewGuid();
        var repository = new InMemoryRepository<DeviceClientVersionSnapshot>();
        var handler = new ReportDeviceClientVersionHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-001")),
            repository);

        var result = await handler.Handle(
            new ReportDeviceClientVersionCommand(
                deviceId,
                "DEV-OTHER",
                "1.2.0",
                "1.0.0",
                [],
                [],
                "stable",
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(repository.AddedEntity);
    }

    [Fact]
    public async Task ReportDeviceClientVersionHandler_ShouldStoreLatestPluginSnapshot()
    {
        var deviceId = Guid.NewGuid();
        var repository = new InMemoryRepository<DeviceClientVersionSnapshot>();
        var handler = new ReportDeviceClientVersionHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-001")),
            repository);

        var result = await handler.Handle(
            new ReportDeviceClientVersionCommand(
                deviceId,
                "DEV-001",
                "1.2.0",
                "1.0.0",
                [new DeviceClientPluginVersionReportItem("Homogenization", "匀浆", "2.0.0", "1.0.0")],
                ["Homogenization"],
                "stable",
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var snapshot = Assert.IsType<DeviceClientVersionSnapshot>(repository.AddedEntity);
        Assert.Equal(deviceId, snapshot.DeviceId);
        Assert.Equal("DEV-001", snapshot.ClientCode);
        var plugin = Assert.Single(snapshot.InstalledPlugins);
        Assert.Equal("Homogenization", plugin.ModuleId);
        Assert.True(plugin.Enabled);
    }

    [Fact]
    public async Task GetPublicClientDownloadsHandler_ShouldExposeOnlyPublishedHostAndPluginCatalog()
    {
        var hostRepository = new InMemoryRepository<ClientHostRelease>();
        hostRepository.Items.Add(new ClientHostRelease(
            "stable",
            "99.0.0",
            "1.0.0",
            "win-x64",
            "net10.0",
            "https://download.example.test/host-draft.zip",
            new string('a', 64),
            1024,
            null,
            ClientReleaseStatus.Draft,
            "draft-signature",
            "IIoT"));
        hostRepository.Items.Add(new ClientHostRelease(
            "stable",
            "1.1.0",
            "1.0.0",
            "win-x64",
            "net10.0",
            "https://download.example.test/host-1.1.0.zip",
            new string('b', 64),
            2048,
            "host release",
            ClientReleaseStatus.Published,
            "host-signature",
            "IIoT"));

        var pluginRepository = new InMemoryRepository<ClientPluginRelease>();
        pluginRepository.Items.Add(new ClientPluginRelease(
            "Injection",
            "注液",
            "注液工序插件",
            "Droplets",
            "#10b981",
            "stable",
            "2.0.0",
            "1.0.0",
            "1.0.0",
            "2.0.0",
            "win-x64",
            "net10.0",
            "https://download.example.test/plugins/injection-2.0.0.zip",
            new string('c', 64),
            4096,
            "plugin release",
            """[{"moduleId":"Core","version":"1.0.0"}]""",
            ClientReleaseStatus.Published,
            "plugin-signature",
            "IIoT"));
        pluginRepository.Items.Add(new ClientPluginRelease(
            "Welding",
            "焊接",
            "焊接工序插件",
            "Wrench",
            "#f59e0b",
            "stable",
            "99.0.0",
            "1.0.0",
            "1.0.0",
            "2.0.0",
            "win-x64",
            "net10.0",
            "https://download.example.test/plugins/welding-draft.zip",
            new string('d', 64),
            8192,
            null,
            "[]",
            ClientReleaseStatus.Draft,
            "draft-plugin-signature",
            "IIoT"));

        var handler = new GetPublicClientDownloadsHandler(
            hostRepository,
            pluginRepository,
            new FixedRetentionPolicyReader(),
            new StubArtifactCatalogReader());

        var result = await handler.Handle(
            new GetPublicClientDownloadsQuery("stable", "win-x64"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        var hostVersion = Assert.Single(result.Value!.Host.Versions);
        Assert.Equal("1.1.0", hostVersion.Version);
        var publicHostJson = System.Text.Json.JsonSerializer.Serialize(hostVersion);
        Assert.DoesNotContain("DownloadUrl", publicHostJson, StringComparison.Ordinal);
        Assert.DoesNotContain("https://download.example.test/host-1.1.0.zip", publicHostJson, StringComparison.Ordinal);

        var plugin = Assert.Single(result.Value.Plugins);
        Assert.Equal("Injection", plugin.ModuleId);
        Assert.Equal("注液", plugin.DisplayName);
        var pluginVersion = Assert.Single(plugin.Versions);
        Assert.Equal(4096, pluginVersion.PackageSize);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, pluginVersion.Dependencies.ValueKind);

        var publicPluginJson = System.Text.Json.JsonSerializer.Serialize(pluginVersion);
        Assert.DoesNotContain("DownloadUrl", publicPluginJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Sha256", publicPluginJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Signature", publicPluginJson, StringComparison.Ordinal);
        Assert.DoesNotContain("https://download.example.test/plugins/injection-2.0.0.zip", publicPluginJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPublicClientDownloadsHandler_ShouldExposeInstallerArtifactsFromDirectory()
    {
        var artifactRoot = CreateArtifactRoot(
            "ci",
            "0.0.189-ci",
            "win-x64",
            "Homogenization",
            "匀浆");
        try
        {
            var handler = new GetPublicClientDownloadsHandler(
                new InMemoryRepository<ClientHostRelease>(),
                new InMemoryRepository<ClientPluginRelease>(),
                new FixedRetentionPolicyReader(),
                new EdgeInstallerArtifactCatalogReader(
                    Microsoft.Extensions.Options.Options.Create(new EdgeInstallerArtifactOptions { RootPath = artifactRoot })));

            var result = await handler.Handle(
                new GetPublicClientDownloadsQuery("ci", "win-x64"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            var hostVersion = Assert.Single(result.Value!.Host.Versions);
            Assert.Equal("0.0.189-ci", hostVersion.Version);
            Assert.Equal("win-x64", hostVersion.TargetRuntime);
            Assert.Equal(new string('a', 64), hostVersion.Sha256);

            var plugin = Assert.Single(result.Value.Plugins);
            Assert.Equal("Homogenization", plugin.ModuleId);
            Assert.Equal("匀浆", plugin.DisplayName);
            var pluginVersion = Assert.Single(plugin.Versions);
            Assert.Equal("1.0.0", pluginVersion.Version);
            Assert.Equal(2048, pluginVersion.PackageSize);
        }
        finally
        {
            Directory.Delete(artifactRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GetPublicClientDownloadsHandler_ShouldLetDatabaseStatusSuppressDirectoryArtifact()
    {
        var artifactRoot = CreateArtifactRoot(
            "ci",
            "0.0.189-ci",
            "win-x64",
            "Homogenization",
            "匀浆");
        try
        {
            var hostRepository = new InMemoryRepository<ClientHostRelease>();
            hostRepository.Items.Add(new ClientHostRelease(
                "ci",
                "0.0.189-ci",
                "1.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/installers/ci/0.0.189-ci/installer-artifact.json",
                new string('a', 64),
                4096,
                null,
                ClientReleaseStatus.Archived,
                null,
                "IIoT"));

            var pluginRepository = new InMemoryRepository<ClientPluginRelease>();
            pluginRepository.Items.Add(new ClientPluginRelease(
                "Homogenization",
                "匀浆",
                null,
                null,
                null,
                "ci",
                "1.0.0",
                "1.0.0",
                "1.0.0",
                "99.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/installers/ci/0.0.189-ci/installer-artifact.json#moduleId=Homogenization",
                new string('b', 64),
                2048,
                null,
                "[]",
                ClientReleaseStatus.Archived,
                null,
                "IIoT"));

            var handler = new GetPublicClientDownloadsHandler(
                hostRepository,
                pluginRepository,
                new FixedRetentionPolicyReader(),
                new EdgeInstallerArtifactCatalogReader(
                    Microsoft.Extensions.Options.Options.Create(new EdgeInstallerArtifactOptions { RootPath = artifactRoot })));

            var result = await handler.Handle(
                new GetPublicClientDownloadsQuery("ci", "win-x64"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Empty(result.Value!.Host.Versions);
            Assert.Empty(result.Value.Plugins);
        }
        finally
        {
            Directory.Delete(artifactRoot, recursive: true);
        }
    }

    private sealed class StubDeviceIdentityQueryService(DeviceIdentitySnapshot? snapshot) : IDeviceIdentityQueryService
    {
        public Task<DeviceIdentitySnapshot?> GetByDeviceIdAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot?.DeviceId == deviceId ? snapshot : null);
        }

        public Task<bool> ExistsAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot?.DeviceId == deviceId);
        }
    }

    private sealed class FixedRetentionPolicyReader : IClientReleaseRetentionPolicyReader
    {
        public Task<int> GetMaxVersionsPerComponentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(5);
        }
    }

    private sealed class StubArtifactCatalogReader(
        EdgeInstallerArtifactCatalogSnapshot? snapshot = null)
        : IEdgeInstallerArtifactCatalogReader
    {
        public Task<EdgeInstallerArtifactCatalogSnapshot> ReadAsync(
            string channel,
            string? targetRuntime,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(snapshot ?? EdgeInstallerArtifactCatalogSnapshot.Empty);
        }
    }

    private static string CreateArtifactRoot(
        string channel,
        string version,
        string targetRuntime,
        string moduleId,
        string displayName)
    {
        var artifactRoot = Path.Combine(Path.GetTempPath(), $"iiot-edge-artifacts-{Guid.NewGuid():N}");
        var versionDirectory = Path.Combine(artifactRoot, channel, version);
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(
            Path.Combine(versionDirectory, "installer-artifact.json"),
            $$"""
            {
              "schemaVersion": 2,
              "channel": "{{channel}}",
              "version": "{{version}}",
              "hostApiVersion": "1.0.0",
              "targetRuntime": "{{targetRuntime}}",
              "targetFramework": "net10.0",
              "generatedAtUtc": "2026-06-18T00:00:00Z",
              "installerStubSha256": "{{new string('c', 64)}}",
              "installerStubSize": 1024,
              "hostDirectorySha256": "{{new string('a', 64)}}",
              "hostDirectorySize": 4096,
              "modules": [
                {
                  "moduleId": "{{moduleId}}",
                  "displayName": "{{displayName}}",
                  "version": "1.0.0",
                  "hostApiVersion": "1.0.0",
                  "minHostVersion": "1.0.0",
                  "maxHostVersion": "99.0.0",
                  "pluginSha256": "{{new string('b', 64)}}",
                  "pluginSize": 2048
                }
              ]
            }
            """);

        return artifactRoot;
    }

    private sealed class NoopRetentionService : IClientReleaseRetentionService
    {
        public Task<int> GetMaxVersionsPerComponentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(5);
        }

        public Task ApplyHostPolicyAsync(
            string channel,
            string targetRuntime,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ApplyPluginPolicyAsync(
            string moduleId,
            string channel,
            string targetRuntime,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryRepository<T> : IRepository<T>
        where T : class, IEntity, IAggregateRoot
    {
        public List<T> Items { get; } = [];

        public T? AddedEntity { get; private set; }

        public T Add(T entity)
        {
            AddedEntity = entity;
            Items.Add(entity);
            return entity;
        }

        public void Update(T entity)
        {
        }

        public void Delete(T entity)
        {
            Items.Remove(entity);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(1);
        }

        public Task<List<T>> GetListAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApplySpecification(specification).ToList());
        }

        public Task<T?> GetSingleOrDefaultAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApplySpecification(specification).SingleOrDefault());
        }

        public Task<int> CountAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApplySpecification(specification).Count());
        }

        public Task<bool> AnyAsync(
            ISpecification<T>? specification = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApplySpecification(specification).Any());
        }

        public Task<bool> AnyAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().Any(predicate));
        }

        public Task<int> CountAsync(
            Expression<Func<T, bool>> predicate,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Items.AsQueryable().Count(predicate));
        }

        private IEnumerable<T> ApplySpecification(ISpecification<T>? specification)
        {
            IEnumerable<T> query = Items;

            if (specification?.FilterCondition is not null)
            {
                query = query.Where(specification.FilterCondition.Compile());
            }

            return query;
        }
    }
}
