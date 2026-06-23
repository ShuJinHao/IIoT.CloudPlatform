using System.Linq.Expressions;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.ProductionService.Commands.ClientVersions;
using IIoT.ProductionService.Validators;
using IIoT.ProductionService.Queries.ClientReleases;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Specification;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.ProductionService.Tests;

public sealed class ClientReleaseBehaviorTests
{
    [Fact]
    public void EdgeReleaseRetentionOptions_ShouldDefaultToThreeVersions()
    {
        var options = new EdgeReleaseRetentionOptions();

        Assert.Equal(3, options.MaxVersionsPerComponent);
    }

    [Fact]
    public void EdgeReleaseUploadOptions_ShouldDefaultToControlledLargeBundleUpload()
    {
        var options = new EdgeReleaseUploadOptions();

        Assert.Equal(100, options.MaxUploadMbps);
        Assert.Equal(EdgeReleaseUploadOptions.DefaultMaxBundleBytes, options.MaxBundleBytes);
        Assert.Equal(".staging", options.StagingDirectoryName);
    }

    [Fact]
    public void PublishEdgeReleaseBundleCommand_ShouldRequirePublishPermissionWithoutAdminOnly()
    {
        var permission = typeof(PublishEdgeReleaseBundleCommand)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
            .Cast<AuthorizeRequirementAttribute>()
            .Single();
        var pluginPackagePermission = typeof(PublishEdgePluginPackageCommand)
            .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
            .Cast<AuthorizeRequirementAttribute>()
            .Single();

        Assert.Equal(ClientReleasePermissions.Publish, permission.Permission);
        Assert.Equal(ClientReleasePermissions.Publish, pluginPackagePermission.Permission);
        Assert.Empty(typeof(PublishEdgeReleaseBundleCommand)
            .GetCustomAttributes(typeof(AdminOnlyAttribute), inherit: false));
        Assert.Empty(typeof(PublishEdgePluginPackageCommand)
            .GetCustomAttributes(typeof(AdminOnlyAttribute), inherit: false));
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldPublishFilesRowsAuditAndSummary()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle("1.2.0");
        try
        {
            var hostRepository = new InMemoryRepository<ClientHostRelease>();
            var pluginRepository = new InMemoryRepository<ClientPluginRelease>();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreatePublishHandler(edgeRoot, hostRepository, pluginRepository, new NoopRetentionService(), auditTrail);

            var result = await PublishBundleAsync(handler, bundle.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            Assert.NotNull(result.Value);
            Assert.Equal("1.2.0", result.Value!.Version);
            Assert.True(result.Value.CleanupSucceeded);
            Assert.Null(result.Value.CleanupWarning);
            Assert.True(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", "1.2.0")));
            Assert.True(File.Exists(Path.Combine(edgeRoot, "velopack", "stable", "releases.stable.json")));
            Assert.Single(hostRepository.Items);
            var pluginRelease = Assert.Single(pluginRepository.Items);
            Assert.StartsWith("/edge-updates/plugins/stable/Homogenization/1.0.0/", pluginRelease.DownloadUrl);
            var pluginPackage = Assert.Single(Directory.GetFiles(
                Path.Combine(edgeRoot, "plugins", "stable", "Homogenization", "1.0.0"),
                "*.zip"));
            Assert.Equal(HashFile(pluginPackage), pluginRelease.Sha256);
            Assert.Equal(new FileInfo(pluginPackage).Length, pluginRelease.PackageSize);
            Assert.Contains(auditTrail.Entries, entry => entry.Succeeded && entry.OperationType == "ClientRelease.Publish");
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldNotOverwriteExistingPluginRelease_WhenHostVersionChangesOnly()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle("1.2.5");
        try
        {
            var existingPlugin = new ClientPluginRelease(
                "Homogenization",
                "匀浆",
                null,
                null,
                null,
                "stable",
                "1.0.0",
                "1.0.0",
                "1.0.0",
                "99.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/plugins/stable/Homogenization/1.0.0/existing.zip",
                new string('e', 64),
                123,
                "existing plugin notes",
                "[]",
                ClientReleaseStatus.Published,
                null,
                "IIoT");
            var pluginRepository = new InMemoryRepository<ClientPluginRelease>();
            pluginRepository.Items.Add(existingPlugin);
            var handler = CreatePublishHandler(
                edgeRoot,
                new InMemoryRepository<ClientHostRelease>(),
                pluginRepository,
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            var result = await PublishBundleAsync(handler, bundle.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            Assert.Single(pluginRepository.Items);
            Assert.Equal("/edge-updates/plugins/stable/Homogenization/1.0.0/existing.zip", existingPlugin.DownloadUrl);
            Assert.Equal("existing plugin notes", existingPlugin.ReleaseNotes);
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "plugins", "stable", "Homogenization", "1.0.0")));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_ShouldPublishIndependentPluginZip()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper("Homogenization", "1.1.0");
        try
        {
            var pluginRepository = new InMemoryRepository<ClientPluginRelease>();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreatePluginPackageHandler(edgeRoot, pluginRepository, new NoopRetentionService(), auditTrail);

            var result = await PublishPluginPackageAsync(handler, wrapper.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            Assert.NotNull(result.Value);
            Assert.Equal("Homogenization", result.Value!.ModuleId);
            var release = Assert.Single(pluginRepository.Items);
            Assert.Equal("1.1.0", release.Version);
            Assert.Equal("独立插件更新", release.ReleaseNotes);
            Assert.StartsWith("/edge-updates/plugins/stable/Homogenization/1.1.0/", release.DownloadUrl);
            var package = Assert.Single(Directory.GetFiles(Path.Combine(edgeRoot, "plugins", "stable", "Homogenization", "1.1.0"), "*.zip"));
            Assert.Equal(HashFile(package), release.Sha256);
            Assert.Contains(auditTrail.Entries, entry => entry.Succeeded && entry.OperationType == "ClientRelease.PublishPlugin");
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_ShouldRejectDuplicatePluginVersion()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper("Homogenization", "1.1.1");
        try
        {
            var pluginRepository = new InMemoryRepository<ClientPluginRelease>();
            pluginRepository.Items.Add(new ClientPluginRelease(
                "Homogenization",
                "匀浆",
                null,
                null,
                null,
                "stable",
                "1.1.1",
                "1.0.0",
                "1.0.0",
                "99.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/plugins/stable/Homogenization/1.1.1/existing.zip",
                new string('f', 64),
                100,
                "old",
                "[]",
                ClientReleaseStatus.Published,
                null,
                "IIoT"));
            var handler = CreatePluginPackageHandler(
                edgeRoot,
                pluginRepository,
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            var result = await PublishPluginPackageAsync(handler, wrapper.ZipPath);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors ?? [], error => error.Contains("插件版本已存在", StringComparison.Ordinal));
            Assert.Single(pluginRepository.Items);
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "plugins", "stable", "Homogenization", "1.1.1")));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_ShouldRejectCloudApiSecretsInPluginZip()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper(
            "Homogenization",
            "1.1.2",
            packageRoot => WriteFile(
                Path.Combine(packageRoot, "appsettings.Production.json"),
                """
                {
                  "CloudApi": {
                    "ClientCode": "real-client-code",
                    "BootstrapSecret": "real-secret"
                  }
                }
                """));
        try
        {
            var pluginRepository = new InMemoryRepository<ClientPluginRelease>();
            var handler = CreatePluginPackageHandler(
                edgeRoot,
                pluginRepository,
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            var result = await PublishPluginPackageAsync(handler, wrapper.ZipPath);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors ?? [], error => error.Contains("CloudApi:ClientCode", StringComparison.Ordinal));
            Assert.Empty(pluginRepository.Items);
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "plugins", "stable", "Homogenization", "1.1.2")));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldRollbackFilesAndHideCatalog_WhenDatabaseCommitFails()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle("1.2.1");
        Directory.CreateDirectory(Path.Combine(edgeRoot, "velopack", "stable"));
        File.WriteAllText(Path.Combine(edgeRoot, "velopack", "stable", "releases.stable.json"), "old-manifest");
        File.WriteAllText(Path.Combine(edgeRoot, "velopack", "stable", "assets.stable.json"), "old-assets");
        File.WriteAllText(Path.Combine(edgeRoot, "velopack", "stable", "old-1.0.0.nupkg"), "old");
        try
        {
            var hostRepository = new InMemoryRepository<ClientHostRelease>
            {
                SaveChangesException = new InvalidOperationException("db unavailable")
            };
            var pluginRepository = new InMemoryRepository<ClientPluginRelease>();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreatePublishHandler(edgeRoot, hostRepository, pluginRepository, new NoopRetentionService(), auditTrail);

            var result = await PublishBundleAsync(handler, bundle.ZipPath);

            Assert.False(result.IsSuccess);
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", "1.2.1")));
            Assert.Equal("old-manifest", File.ReadAllText(Path.Combine(edgeRoot, "velopack", "stable", "releases.stable.json")));
            Assert.Equal("old-assets", File.ReadAllText(Path.Combine(edgeRoot, "velopack", "stable", "assets.stable.json")));
            Assert.False(File.Exists(Path.Combine(edgeRoot, "velopack", "stable", "IIoT.EdgeClient-1.2.1-full.nupkg")));

            var artifactCatalog = await new EdgeInstallerArtifactCatalogReader(
                    Options.Create(new EdgeInstallerArtifactOptions
                    {
                        RootPath = Path.Combine(edgeRoot, "installers")
                    }))
                .ReadAsync("stable", "win-x64");
            Assert.Empty(artifactCatalog.HostReleases);
            Assert.Contains(auditTrail.Entries, entry => !entry.Succeeded && entry.FailureReason?.Contains("db unavailable", StringComparison.Ordinal) == true);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldKeepPublishedVersion_WhenRetentionCleanupFails()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle("1.2.2");
        try
        {
            var hostRepository = new InMemoryRepository<ClientHostRelease>();
            var pluginRepository = new InMemoryRepository<ClientPluginRelease>();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreatePublishHandler(
                edgeRoot,
                hostRepository,
                pluginRepository,
                new ThrowingRetentionService("retention down"),
                auditTrail);

            var result = await PublishBundleAsync(handler, bundle.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            Assert.NotNull(result.Value);
            Assert.False(result.Value!.CleanupSucceeded);
            Assert.Contains("retention down", result.Value.CleanupWarning, StringComparison.Ordinal);
            Assert.True(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", "1.2.2")));
            Assert.Single(hostRepository.Items);
            Assert.Single(pluginRepository.Items);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldNotDeleteArchivedNupkgReferencedByCurrentVelopackManifest()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var oldNupkg = Path.Combine(edgeRoot, "velopack", "stable", "IIoT.EdgeClient-1.0.0-full.nupkg");
        Directory.CreateDirectory(Path.GetDirectoryName(oldNupkg)!);
        File.WriteAllText(oldNupkg, "old nupkg");
        Directory.CreateDirectory(Path.Combine(edgeRoot, "installers", "stable", "1.0.0"));
        File.WriteAllText(Path.Combine(edgeRoot, "installers", "stable", "1.0.0", "installer-artifact.json"), "{}");

        var bundle = CreateEdgeReleaseBundle(
            "1.2.4",
            mutateVelopackRoot: velopackRoot =>
            {
                WriteFile(
                    Path.Combine(velopackRoot, "releases.stable.json"),
                    """{"packages":["IIoT.EdgeClient-1.0.0-full.nupkg","IIoT.EdgeClient-1.2.4-full.nupkg"]}""");
                WriteFile(
                    Path.Combine(velopackRoot, "assets.stable.json"),
                    """{"assets":["IIoT.EdgeClient-1.0.0-full.nupkg","IIoT.EdgeClient-1.2.4-full.nupkg"]}""");
            });
        try
        {
            var hostRepository = new InMemoryRepository<ClientHostRelease>();
            hostRepository.Items.Add(new ClientHostRelease(
                "stable",
                "1.0.0",
                "1.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
                new string('c', 64),
                1024,
                "old",
                ClientReleaseStatus.Archived,
                null,
                "IIoT"));

            var pluginRepository = new InMemoryRepository<ClientPluginRelease>();
            var handler = CreatePublishHandler(
                edgeRoot,
                hostRepository,
                pluginRepository,
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            var result = await PublishBundleAsync(handler, bundle.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", "1.0.0")));
            Assert.True(File.Exists(oldNupkg));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldRejectForbiddenFilesWithoutLeavingInstaller()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle(
            "1.2.3",
            installerRoot => File.WriteAllText(Path.Combine(installerRoot, "launcher", "launcher.accounts.json"), "{}"));
        try
        {
            var handler = CreatePublishHandler(
                edgeRoot,
                new InMemoryRepository<ClientHostRelease>(),
                new InMemoryRepository<ClientPluginRelease>(),
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            var result = await PublishBundleAsync(handler, bundle.ZipPath);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors ?? [], error => error.Contains("禁止上传", StringComparison.Ordinal));
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", "1.2.3")));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldRejectZipTraversal()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundlePath = Path.Combine(CreateTempDirectory("iiot-edge-upload-bundle"), "bundle.zip");
        try
        {
            using (var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../evil.txt");
                await using var stream = entry.Open();
                await stream.WriteAsync("evil"u8.ToArray());
            }

            var handler = CreatePublishHandler(
                edgeRoot,
                new InMemoryRepository<ClientHostRelease>(),
                new InMemoryRepository<ClientPluginRelease>(),
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            var result = await PublishBundleAsync(handler, bundlePath);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors ?? [], error => error.Contains("非法 zip 路径", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            TryDeleteDirectory(Path.GetDirectoryName(bundlePath)!);
        }
    }

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
    public void UpsertClientReleaseValidators_ShouldRejectPublishedReleaseWithoutNotes()
    {
        var hostResult = new UpsertClientHostReleaseCommandValidator().Validate(
            new UpsertClientHostReleaseCommand(
                "stable",
                "1.2.0",
                "1.0.0",
                "win-x64",
                "net10.0",
                "https://example.test/releases/host.zip",
                new string('a', 64),
                1024,
                null,
                "Published",
                null,
                "IIoT"));

        Assert.False(hostResult.IsValid);
        Assert.Contains(hostResult.Errors, error => error.PropertyName == nameof(UpsertClientHostReleaseCommand.ReleaseNotes));

        var pluginResult = new UpsertClientPluginReleaseCommandValidator().Validate(
            new UpsertClientPluginReleaseCommand(
                "Homogenization",
                "匀浆",
                null,
                null,
                null,
                "stable",
                "1.2.0",
                "1.0.0",
                "1.0.0",
                "99.0.0",
                "win-x64",
                "net10.0",
                "https://example.test/releases/host.zip#moduleId=Homogenization",
                new string('b', 64),
                1024,
                " ",
                "[]",
                "Published",
                null,
                "IIoT"));

        Assert.False(pluginResult.IsValid);
        Assert.Contains(pluginResult.Errors, error => error.PropertyName == nameof(UpsertClientPluginReleaseCommand.ReleaseNotes));
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
            Assert.True(pluginVersion.PackageSize > 0);
        }
        finally
        {
            Directory.Delete(Directory.GetParent(artifactRoot)!.FullName, recursive: true);
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
            Directory.Delete(Directory.GetParent(artifactRoot)!.FullName, recursive: true);
        }
    }

    private static PublishEdgeReleaseBundleHandler CreatePublishHandler(
        string edgeRoot,
        InMemoryRepository<ClientHostRelease> hostRepository,
        InMemoryRepository<ClientPluginRelease> pluginRepository,
        IClientReleaseRetentionService retentionService,
        RecordingAuditTrailService auditTrail)
    {
        return new PublishEdgeReleaseBundleHandler(
            Options.Create(new EdgeInstallerArtifactOptions
            {
                RootPath = Path.Combine(edgeRoot, "installers")
            }),
            Options.Create(new EdgeReleaseUploadOptions
            {
                MaxUploadMbps = 1000,
                MaxBundleBytes = EdgeReleaseUploadOptions.DefaultMaxBundleBytes,
                StagingDirectoryName = ".staging"
            }),
            hostRepository,
            pluginRepository,
            retentionService,
            new TestCurrentUser(),
            auditTrail);
    }

    private static PublishEdgePluginPackageHandler CreatePluginPackageHandler(
        string edgeRoot,
        InMemoryRepository<ClientPluginRelease> pluginRepository,
        IClientReleaseRetentionService retentionService,
        RecordingAuditTrailService auditTrail)
    {
        return new PublishEdgePluginPackageHandler(
            Options.Create(new EdgeInstallerArtifactOptions
            {
                RootPath = Path.Combine(edgeRoot, "installers")
            }),
            Options.Create(new EdgeReleaseUploadOptions
            {
                MaxUploadMbps = 1000,
                MaxBundleBytes = EdgeReleaseUploadOptions.DefaultMaxBundleBytes,
                StagingDirectoryName = ".staging"
            }),
            pluginRepository,
            retentionService,
            new TestCurrentUser(),
            auditTrail);
    }

    private static async Task<IIoT.SharedKernel.Result.Result<EdgeReleaseBundlePublishResultDto>> PublishBundleAsync(
        PublishEdgeReleaseBundleHandler handler,
        string bundlePath)
    {
        await using var stream = File.OpenRead(bundlePath);
        return await handler.Handle(
            new PublishEdgeReleaseBundleCommand(
                stream,
                new FileInfo(bundlePath).Length,
                "application/zip",
                "127.0.0.1"),
            CancellationToken.None);
    }

    private static async Task<IIoT.SharedKernel.Result.Result<EdgePluginPackagePublishResultDto>> PublishPluginPackageAsync(
        PublishEdgePluginPackageHandler handler,
        string wrapperPath)
    {
        await using var stream = File.OpenRead(wrapperPath);
        return await handler.Handle(
            new PublishEdgePluginPackageCommand(
                stream,
                new FileInfo(wrapperPath).Length,
                "application/zip",
                "127.0.0.1"),
            CancellationToken.None);
    }

    private static EdgeReleaseBundleFixture CreateEdgeReleaseBundle(
        string version,
        Action<string>? mutateInstallerRoot = null,
        Action<string>? mutateVelopackRoot = null)
    {
        var workingRoot = CreateTempDirectory("iiot-edge-upload-bundle");
        var bundleRoot = Path.Combine(workingRoot, "bundle");
        var installerRoot = Path.Combine(bundleRoot, "installer");
        var velopackRoot = Path.Combine(bundleRoot, "velopack");
        Directory.CreateDirectory(installerRoot);
        Directory.CreateDirectory(velopackRoot);

        WriteFile(Path.Combine(installerRoot, "IIoT.Edge.Setup.exe"), $"setup {version}");
        WriteFile(Path.Combine(installerRoot, "launcher", "launcher.txt"), "launcher");
        WriteFile(Path.Combine(installerRoot, "host", "host.dll"), $"host {version}");
        WriteFile(
            Path.Combine(installerRoot, "plugins", "Homogenization", "plugin.json"),
            """
            {
              "moduleId": "Homogenization",
              "displayName": "匀浆",
              "version": "1.0.0",
              "hostApiVersion": "1.0.0",
              "minHostVersion": "1.0.0",
              "maxHostVersion": "99.0.0",
              "entryAssembly": "plugin.dll"
            }
            """);
        WriteFile(Path.Combine(installerRoot, "plugins", "Homogenization", "plugin.dll"), $"plugin {version}");
        WriteFile(Path.Combine(installerRoot, "velopack", "IIoT.Edge.Setup.exe"), $"velopack setup {version}");

        WriteFile(Path.Combine(velopackRoot, $"IIoT.EdgeClient-{version}-full.nupkg"), $"nupkg {version}");
        WriteFile(Path.Combine(velopackRoot, "releases.stable.json"), $$"""{"packages":["IIoT.EdgeClient-{{version}}-full.nupkg"]}""");
        WriteFile(Path.Combine(velopackRoot, "assets.stable.json"), $$"""{"assets":["IIoT.EdgeClient-{{version}}-full.nupkg"]}""");

        mutateInstallerRoot?.Invoke(installerRoot);
        mutateVelopackRoot?.Invoke(velopackRoot);

        var setupPath = Path.Combine(installerRoot, "IIoT.Edge.Setup.exe");
        var hostRoot = Path.Combine(installerRoot, "host");
        var velopackSetupPath = Path.Combine(installerRoot, "velopack", "IIoT.Edge.Setup.exe");
        var pluginRoot = Path.Combine(installerRoot, "plugins", "Homogenization");
        var manifest = new
        {
            schemaVersion = ClientReleaseCatalogSchema.Version,
            channel = "stable",
            version,
            hostApiVersion = "1.0.0",
            targetRuntime = "win-x64",
            targetFramework = "net10.0",
            generatedAtUtc = DateTime.UtcNow,
            sourceCommit = new string('a', 40),
            previousVersion = "1.1.9",
            previousSourceCommit = new string('b', 40),
            releaseNotes = $"release {version}\n- changed",
            installerStubFile = "IIoT.Edge.Setup.exe",
            installerStubSha256 = HashFile(setupPath),
            installerStubSize = new FileInfo(setupPath).Length,
            launcherDirectory = "launcher",
            hostDirectory = "host",
            hostDirectorySha256 = HashDirectory(hostRoot),
            hostDirectorySize = GetDirectorySize(hostRoot),
            pluginsRoot = "plugins",
            velopackSetupFile = "velopack/IIoT.Edge.Setup.exe",
            velopackSetupSha256 = HashFile(velopackSetupPath),
            velopackSetupSize = new FileInfo(velopackSetupPath).Length,
            modules = new[]
            {
                new
                {
                    moduleId = "Homogenization",
                    displayName = "匀浆",
                    description = "匀浆工序插件",
                    version = "1.0.0",
                    hostApiVersion = "1.0.0",
                    minHostVersion = "1.0.0",
                    maxHostVersion = "99.0.0",
                    pluginDirectory = "Homogenization",
                    pluginSha256 = HashDirectory(pluginRoot),
                    pluginSize = GetDirectorySize(pluginRoot)
                }
            }
        };
        File.WriteAllText(
            Path.Combine(installerRoot, "installer-artifact.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new UTF8Encoding(false));

        var zipPath = Path.Combine(workingRoot, "bundle.zip");
        ZipFile.CreateFromDirectory(bundleRoot, zipPath);
        return new EdgeReleaseBundleFixture(workingRoot, zipPath);
    }

    private static EdgeReleaseBundleFixture CreatePluginReleaseWrapper(
        string moduleId,
        string version,
        Action<string>? mutatePackageRoot = null)
    {
        var workingRoot = CreateTempDirectory("iiot-edge-plugin-wrapper");
        var packageRoot = Path.Combine(workingRoot, "package-root");
        WriteFile(
            Path.Combine(packageRoot, "plugin.json"),
            $$"""
            {
              "moduleId": "{{moduleId}}",
              "displayName": "匀浆",
              "version": "{{version}}",
              "hostApiVersion": "1.0.0",
              "minHostVersion": "1.0.0",
              "maxHostVersion": "99.0.0",
              "entryAssembly": "{{moduleId}}.dll"
            }
            """);
        WriteFile(Path.Combine(packageRoot, $"{moduleId}.dll"), $"plugin {version}");
        mutatePackageRoot?.Invoke(packageRoot);

        var packageFileName = $"IIoT.EdgePlugin.{moduleId}-{version}-win-x64.zip";
        var packagePath = Path.Combine(workingRoot, packageFileName);
        ZipFile.CreateFromDirectory(packageRoot, packagePath);
        var wrapperRoot = Path.Combine(workingRoot, "wrapper");
        Directory.CreateDirectory(Path.Combine(wrapperRoot, "plugin"));
        File.Copy(packagePath, Path.Combine(wrapperRoot, "plugin", packageFileName));
        var manifest = new
        {
            packageSchemaVersion = 1,
            channel = "stable",
            moduleId,
            processType = "homogenization",
            displayName = "匀浆",
            version,
            hostApiVersion = "1.0.0",
            minHostVersion = "1.0.0",
            maxHostVersion = "99.0.0",
            dependencies = Array.Empty<string>(),
            targetRuntime = "win-x64",
            targetFramework = "net10.0",
            packageFileName,
            packageSize = new FileInfo(packagePath).Length,
            sha256 = HashFile(packagePath),
            signature = "",
            publisher = "IIoT",
            releaseNotes = "独立插件更新",
            createdAtUtc = DateTime.UtcNow
        };
        File.WriteAllText(
            Path.Combine(wrapperRoot, "plugin-release.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new UTF8Encoding(false));

        var zipPath = Path.Combine(workingRoot, "wrapper.zip");
        ZipFile.CreateFromDirectory(wrapperRoot, zipPath);
        return new EdgeReleaseBundleFixture(workingRoot, zipPath);
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static string HashFile(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string HashDirectory(string directory)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(directory, file).Replace('\\', '/');
            hasher.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hasher.AppendData([0]);
            using var stream = File.OpenRead(file);
            stream.CopyTo(new HashAppendStream(hasher));
            hasher.AppendData([10]);
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    private static long GetDirectorySize(string directory)
        => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record EdgeReleaseBundleFixture(string WorkingRoot, string ZipPath) : IDisposable
    {
        public void Dispose()
        {
            TryDeleteDirectory(WorkingRoot);
        }
    }

    private sealed class RecordingAuditTrailService : IAuditTrailService
    {
        public List<AuditTrailEntry> Entries { get; } = [];

        public Task TryWriteAsync(AuditTrailEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public string? Id { get; init; } = Guid.NewGuid().ToString();

        public string? UserName { get; init; } = "tester";

        public string? Role { get; init; } = "Administrator";

        public Guid? DeviceId => null;

        public bool IsAuthenticated => true;
    }

    private sealed class ThrowingRetentionService(string message) : IClientReleaseRetentionService
    {
        public Task<int> GetMaxVersionsPerComponentAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(3);
        }

        public Task ApplyHostPolicyAsync(
            string channel,
            string targetRuntime,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(message);
        }

        public Task ApplyPluginPolicyAsync(
            string moduleId,
            string channel,
            string targetRuntime,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class HashAppendStream(IncrementalHash hasher) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => hasher.AppendData(buffer.AsSpan(offset, count));
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
        var edgeRoot = Path.Combine(Path.GetTempPath(), $"iiot-edge-artifacts-{Guid.NewGuid():N}");
        var artifactRoot = Path.Combine(edgeRoot, "installers");
        var versionDirectory = Path.Combine(artifactRoot, channel, version);
        Directory.CreateDirectory(versionDirectory);
        var pluginStagingDirectory = Path.Combine(edgeRoot, ".plugin-staging", moduleId);
        WriteFile(
            Path.Combine(pluginStagingDirectory, "plugin.json"),
            $$"""
            {
              "moduleId": "{{moduleId}}",
              "displayName": "{{displayName}}",
              "version": "1.0.0",
              "hostApiVersion": "1.0.0",
              "minHostVersion": "1.0.0",
              "maxHostVersion": "99.0.0",
              "entryAssembly": "{{moduleId}}.dll"
            }
            """);
        WriteFile(Path.Combine(pluginStagingDirectory, $"{moduleId}.dll"), "plugin");
        var pluginPackageDirectory = Path.Combine(edgeRoot, "plugins", channel, moduleId, "1.0.0");
        Directory.CreateDirectory(pluginPackageDirectory);
        ZipFile.CreateFromDirectory(
            pluginStagingDirectory,
            Path.Combine(pluginPackageDirectory, $"IIoT.EdgePlugin.{moduleId}-1.0.0-{targetRuntime}.zip"));
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

        public Exception? SaveChangesException { get; init; }

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
            if (SaveChangesException is not null)
            {
                throw SaveChangesException;
            }

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
