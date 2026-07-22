using System.Linq.Expressions;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.ProductionService.Commands.ClientVersions;
using IIoT.ProductionService.Validators;
using IIoT.ProductionService.Queries.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.SharedKernel.Domain;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using IIoT.SharedKernel.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.CloudPlatform.WorkflowTests;

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

        Assert.Equal(1000, options.MaxUploadMbps);
        Assert.Equal(EdgeReleaseUploadOptions.DefaultMaxBundleBytes, options.MaxBundleBytes);
        Assert.Equal(".staging", options.StagingDirectoryName);
    }

    [Fact]
    public void ClientReleaseComponent_ShouldNotTouchHostComponent_WhenAppendingNewVersion()
    {
        var component = CreateHostComponent(
            "stable",
            "1.0.0",
            "1",
            "win-x64",
            "net10.0",
            "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
            new string('a', 64),
            100,
            "initial",
            ClientReleaseStatus.Published);
        var updatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SetUpdatedAtUtc(component, updatedAt);

        component.UpdateHostMetadata();
        component.UpsertHostVersion(
            "1.0.1",
            "1",
            "net10.0",
            "/edge-updates/installers/stable/1.0.1/installer-artifact.json",
            new string('b', 64),
            101,
            "second",
            ClientReleaseStatus.Published,
            null,
            "IIoT");

        Assert.Equal(updatedAt, component.UpdatedAtUtc);
        Assert.NotNull(component.FindVersion("1.0.1"));
    }

    [Fact]
    public void ClientReleaseComponent_ShouldTouchHostComponent_WhenUpdatingExistingVersion()
    {
        var component = CreateHostComponent(
            "stable",
            "1.0.0",
            "1",
            "win-x64",
            "net10.0",
            "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
            new string('a', 64),
            100,
            "initial",
            ClientReleaseStatus.Published);
        var updatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SetUpdatedAtUtc(component, updatedAt);

        component.UpsertHostVersion(
            "1.0.0",
            "1",
            "net10.0",
            "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
            new string('b', 64),
            101,
            "updated",
            ClientReleaseStatus.Published,
            null,
            "IIoT");

        Assert.True(component.UpdatedAtUtc > updatedAt);
    }

    [Fact]
    public void ClientReleaseComponent_ShouldTouchPluginComponentOnlyForMetadataOrExistingVersionChanges()
    {
        var component = CreatePluginComponent(
            "Homogenization",
            "匀浆",
            "stable",
            "1.0.0",
            "1",
            "1.0.0",
            "99.0.0",
            "win-x64",
            "net10.0",
            "/edge-updates/plugins/stable/Homogenization/1.0.0/plugin.zip",
            new string('a', 64),
            100,
            "initial",
            ClientReleaseStatus.Published);
        var updatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SetUpdatedAtUtc(component, updatedAt);

        component.UpdatePluginMetadata("匀浆", null, null, null);
        component.UpsertPluginVersion(
            "1.0.1",
            "1",
            "1.0.0",
            "99.0.0",
            "net10.0",
            "/edge-updates/plugins/stable/Homogenization/1.0.1/plugin.zip",
            new string('b', 64),
            101,
            "second",
            "[]",
            ClientReleaseStatus.Published,
            null,
            "IIoT");

        Assert.Equal(updatedAt, component.UpdatedAtUtc);

        component.UpdatePluginMetadata("匀浆插件", null, null, null);
        var metadataUpdatedAt = component.UpdatedAtUtc;

        Assert.True(metadataUpdatedAt > updatedAt);

        var existingVersionUpdateBaseline = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SetUpdatedAtUtc(component, existingVersionUpdateBaseline);
        component.UpsertPluginVersion(
            "1.0.1",
            "1",
            "1.0.0",
            "99.0.0",
            "net10.0",
            "/edge-updates/plugins/stable/Homogenization/1.0.1/plugin-v2.zip",
            new string('c', 64),
            102,
            "updated",
            "[]",
            ClientReleaseStatus.Published,
            null,
            "IIoT");

        Assert.True(component.UpdatedAtUtc > existingVersionUpdateBaseline);
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
    public void HumanClientReleaseRequests_ShouldUseDedicatedPermissionsWithoutAdminOnly()
    {
        var expectedPermissions = new Dictionary<Type, string>
        {
            [typeof(GetClientReleaseCatalogQuery)] = ClientReleasePermissions.Read,
            [typeof(GetDeviceClientVersionInventoryQuery)] = ClientReleasePermissions.Read,
            [typeof(GetClientReleaseRetentionPolicyQuery)] = ClientReleasePermissions.Read,
            [typeof(GenerateEdgeInstallerPackageCommand)] = ClientReleasePermissions.GenerateInstaller,
            [typeof(UpsertClientHostReleaseCommand)] = ClientReleasePermissions.Manage,
            [typeof(UpsertClientPluginReleaseCommand)] = ClientReleasePermissions.Manage,
            [typeof(ArchiveClientReleaseCommand)] = ClientReleasePermissions.Manage,
            [typeof(UpdateClientReleaseStatusCommand)] = ClientReleasePermissions.Manage,
            [typeof(UpdateClientReleaseRetentionPolicyCommand)] = ClientReleasePermissions.Manage
        };

        foreach (var (requestType, expectedPermission) in expectedPermissions)
        {
            var permission = requestType
                .GetCustomAttributes(typeof(AuthorizeRequirementAttribute), inherit: false)
                .Cast<AuthorizeRequirementAttribute>()
                .Single();

            Assert.Equal(expectedPermission, permission.Permission);
            Assert.Empty(requestType.GetCustomAttributes(typeof(AdminOnlyAttribute), inherit: false));
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldPropagateCancellationAndReleaseUploadSession()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-cancel");
        try
        {
            var auditTrail = new RecordingAuditTrailService();
            var publisher = CreatePublishHandler(
                edgeRoot,
                new InMemoryRepository<ClientReleaseComponent>(),
                new NoopRetentionService(),
                auditTrail);
            publisher.Source.LoadBytes([1, 2, 3]);
            publisher.Source.CancelOnRead = true;

            await Assert.ThrowsAsync<OperationCanceledException>(() => publisher.Handler.Handle(
                new PublishEdgeReleaseBundleCommand(),
                CancellationToken.None));

            Assert.Empty(auditTrail.Entries);
            AssertUploadSessionCleaned(
                edgeRoot,
                "edge-release-bundles");
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_ShouldPropagateCancellationAndReleaseUploadSession()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-cancel");
        try
        {
            var auditTrail = new RecordingAuditTrailService();
            var publisher = CreatePluginPackageHandler(
                edgeRoot,
                new InMemoryRepository<ClientReleaseComponent>(),
                new NoopRetentionService(),
                auditTrail);
            publisher.Source.LoadBytes([1, 2, 3]);
            publisher.Source.CancelOnRead = true;

            await Assert.ThrowsAsync<OperationCanceledException>(() => publisher.Handler.Handle(
                new PublishEdgePluginPackageCommand(),
                CancellationToken.None));

            Assert.Empty(auditTrail.Entries);
            AssertUploadSessionCleaned(
                edgeRoot,
                "edge-plugin-packages");
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldPublishFilesRowsAuditAndSummary()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle("1.2.0");
        try
        {
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreatePublishHandler(edgeRoot, componentRepository, new NoopRetentionService(), auditTrail);

            var result = await PublishBundleAsync(handler, bundle.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            Assert.NotNull(result.Value);
            Assert.Equal("1.2.0", result.Value!.Version);
            Assert.True(result.Value.CleanupSucceeded);
            Assert.Null(result.Value.CleanupWarning);
            Assert.True(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", "1.2.0")));
            Assert.True(File.Exists(Path.Combine(edgeRoot, "velopack", "stable", "releases.stable.json")));
            Assert.True(File.Exists(Path.Combine(edgeRoot, "velopack", "stable", "RELEASES")));
            AssertGatewayReadableDirectory(edgeRoot);
            AssertGatewayReadableDirectory(Path.Combine(edgeRoot, "installers", "stable", "1.2.0"));
            AssertGatewayReadableFile(Path.Combine(edgeRoot, "installers", "stable", "1.2.0", "installer-artifact.json"));
            AssertGatewayReadableDirectory(Path.Combine(edgeRoot, "velopack", "stable"));
            AssertGatewayReadableFile(Path.Combine(edgeRoot, "velopack", "stable", "RELEASES"));
            AssertGatewayReadableFile(Path.Combine(edgeRoot, "velopack", "stable", "releases.stable.json"));
            Assert.Equal(2, componentRepository.Items.Count);
            var pluginRelease = SingleVersion(SingleComponent(
                componentRepository,
                ClientReleaseComponentKind.Plugin,
                "Homogenization"));
            Assert.StartsWith("/edge-updates/plugins/stable/Homogenization/1.0.0/", pluginRelease.DownloadUrl);
            var pluginPackage = Assert.Single(Directory.GetFiles(
                Path.Combine(edgeRoot, "plugins", "stable", "Homogenization", "1.0.0"),
                "*.zip"));
            AssertGatewayReadableDirectory(Path.GetDirectoryName(pluginPackage)!);
            AssertGatewayReadableFile(pluginPackage);
            Assert.Equal(ClientReleaseFileFacts.ComputeSha256(pluginPackage), pluginRelease.Sha256);
            Assert.Equal(new FileInfo(pluginPackage).Length, pluginRelease.PackageSize);
            var hostRelease = SingleVersion(SingleComponent(
                componentRepository,
                ClientReleaseComponentKind.Host,
                ClientReleaseComponent.HostComponentKey));
            var installerStub = Path.Combine(
                edgeRoot,
                "installers",
                "stable",
                "1.2.0",
                "IIoT.Edge.Setup.exe");
            Assert.EndsWith("installer-artifact.json", hostRelease.DownloadUrl, StringComparison.Ordinal);
            Assert.Equal(ClientReleaseFileFacts.ComputeSha256(installerStub), hostRelease.Sha256);
            Assert.Equal(new FileInfo(installerStub).Length, hostRelease.PackageSize);
            foreach (var fileArtifact in hostRelease.Artifacts.Where(artifact =>
                         artifact.ArtifactKind is ClientReleaseArtifactKind.ManifestFile
                             or ClientReleaseArtifactKind.PackageFile
                             or ClientReleaseArtifactKind.VelopackFile))
            {
                var fullPath = Path.Combine(
                    edgeRoot,
                    fileArtifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Assert.NotNull(fileArtifact.Sha256);
                Assert.NotNull(fileArtifact.Size);
                Assert.Equal(ClientReleaseFileFacts.ComputeSha256(fullPath), fileArtifact.Sha256);
                Assert.Equal(new FileInfo(fullPath).Length, fileArtifact.Size);
            }

            Assert.Contains(auditTrail.Entries, entry => entry.Succeeded && entry.OperationType == "ClientRelease.Publish");
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Theory]
    [InlineData("duplicate-id", "重复的插件 moduleId")]
    [InlineData("duplicate-directory", "重复的插件目录")]
    [InlineData("modules-null", "manifest 不完整")]
    [InlineData("module-null", "非法插件声明")]
    [InlineData("module-version-traversal", "非法插件声明")]
    public async Task PublishEdgeReleaseBundleHandler_ShouldRejectInvalidModuleOwnershipBeforePublishing(
        string invalidCase,
        string expectedError)
    {
        const string version = "1.2.1";
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-duplicate-module");
        var bundle = CreateEdgeReleaseBundle(
            version,
            mutateManifest: manifest =>
            {
                var modules = manifest["modules"]!.AsArray();
                switch (invalidCase)
                {
                    case "duplicate-id":
                        AddDuplicateModule(modules, " homogenization ", "SecondPlugin");
                        break;
                    case "duplicate-directory":
                        AddDuplicateModule(modules, "DieCutting", "homogenization/");
                        break;
                    case "modules-null":
                        manifest["modules"] = null;
                        break;
                    case "module-null":
                        modules.Clear();
                        modules.Add(null);
                        break;
                    case "module-version-traversal":
                        modules[0]!["version"] = "../escape";
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown invalid manifest case: {invalidCase}");
                }
            });
        try
        {
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            var publisher = CreatePublishHandler(
                edgeRoot,
                componentRepository,
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            var result = await PublishBundleAsync(publisher, bundle.ZipPath);

            Assert.False(result.IsSuccess);
            Assert.Contains(
                result.Errors ?? [],
                error => error.Contains(expectedError, StringComparison.Ordinal));
            Assert.Empty(componentRepository.Items);
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "installers")));
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "velopack")));
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "plugins")));
            AssertUploadSessionCleaned(edgeRoot, "edge-release-bundles");
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    private static void AddDuplicateModule(
        JsonArray modules,
        string moduleId,
        string pluginDirectory)
    {
        var duplicate = JsonNode.Parse(modules[0]!.ToJsonString())!.AsObject();
        duplicate["moduleId"] = moduleId;
        duplicate["version"] = "2.0.0";
        duplicate["pluginDirectory"] = pluginDirectory;
        modules.Add(duplicate);
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldNotOverwriteExistingPluginRelease_WhenHostVersionChangesOnly()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle("1.2.5");
        try
        {
            var existingPluginComponent = CreatePluginComponent(
                "Homogenization",
                "匀浆",
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
                ClientReleaseStatus.Published);
            var existingPlugin = SingleVersion(existingPluginComponent);
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(existingPluginComponent);
            var handler = CreatePublishHandler(
                edgeRoot,
                componentRepository,
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            var result = await PublishBundleAsync(handler, bundle.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            Assert.Equal(2, componentRepository.Items.Count);
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
    public async Task PublishEdgeReleaseBundleHandler_ShouldRejectPublishedHost_WhenCurrentVersionNupkgIsMissingFromVelopackManifests()
    {
        const string version = "1.2.6";
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle(
            version,
            mutateVelopackRoot: velopackRoot =>
            {
                File.Delete(Path.Combine(velopackRoot, $"IIoT.EdgeClient-{version}-full.nupkg"));
                WriteFile(Path.Combine(velopackRoot, "IIoT.EdgeClient-9.9.9-full.nupkg"), "wrong nupkg");
                WriteFile(Path.Combine(velopackRoot, "RELEASES-stable"), "hash IIoT.EdgeClient-9.9.9-full.nupkg 1024");
                WriteFile(Path.Combine(velopackRoot, "releases.stable.json"), """{"packages":["IIoT.EdgeClient-9.9.9-full.nupkg"]}""");
                WriteFile(Path.Combine(velopackRoot, "assets.stable.json"), """{"assets":["IIoT.EdgeClient-9.9.9-full.nupkg"]}""");
            });
        try
        {
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreatePublishHandler(edgeRoot, componentRepository, new NoopRetentionService(), auditTrail);

            var result = await PublishBundleAsync(handler, bundle.ZipPath);

            Assert.False(result.IsSuccess);
            Assert.Contains(
                result.Errors ?? [],
                error => error.Contains("缺少被 Velopack manifests 引用的 .nupkg", StringComparison.Ordinal));
            Assert.Empty(componentRepository.Items);
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", version)));
            Assert.Contains(
                auditTrail.Entries,
                entry => !entry.Succeeded
                         && entry.FailureReason?.Contains("拒绝置为 Published", StringComparison.Ordinal) == true);
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
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreatePluginPackageHandler(edgeRoot, componentRepository, new NoopRetentionService(), auditTrail);

            var result = await PublishPluginPackageAsync(handler, wrapper.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            Assert.NotNull(result.Value);
            Assert.Equal("Homogenization", result.Value!.ModuleId);
            var release = SingleVersion(SingleComponent(
                componentRepository,
                ClientReleaseComponentKind.Plugin,
                "Homogenization"));
            Assert.Equal("1.1.0", release.Version);
            Assert.Equal("独立插件更新", release.ReleaseNotes);
            Assert.StartsWith("/edge-updates/plugins/stable/Homogenization/1.1.0/", release.DownloadUrl);
            var package = Assert.Single(Directory.GetFiles(Path.Combine(edgeRoot, "plugins", "stable", "Homogenization", "1.1.0"), "*.zip"));
            AssertGatewayReadableDirectory(edgeRoot);
            AssertGatewayReadableDirectory(Path.GetDirectoryName(package)!);
            AssertGatewayReadableFile(package);
            Assert.Equal(ClientReleaseFileFacts.ComputeSha256(package), release.Sha256);
            Assert.Contains(auditTrail.Entries, entry => entry.Succeeded && entry.OperationType == "ClientRelease.PublishPlugin");
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_ShouldRejectWrapperZipTraversal()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapperRoot = CreateTempDirectory("iiot-edge-plugin-upload-wrapper");
        var wrapperPath = Path.Combine(wrapperRoot, "wrapper.zip");
        try
        {
            using (var archive = ZipFile.Open(wrapperPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../evil.txt");
                await using var stream = entry.Open();
                await stream.WriteAsync("evil"u8.ToArray());
            }

            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            var handler = CreatePluginPackageHandler(
                edgeRoot,
                componentRepository,
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            var result = await PublishPluginPackageAsync(handler, wrapperPath);

            Assert.False(result.IsSuccess);
            Assert.Contains(
                result.Errors ?? [],
                error => error.Contains("非法 zip 路径", StringComparison.Ordinal));
            Assert.Empty(componentRepository.Items);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            TryDeleteDirectory(wrapperRoot);
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_ShouldRejectDuplicatePluginVersion()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper("Homogenization", "1.1.1");
        try
        {
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(CreatePluginComponent(
                "Homogenization",
                "匀浆",
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
                ClientReleaseStatus.Published));
            var handler = CreatePluginPackageHandler(
                edgeRoot,
                componentRepository,
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            await Assert.ThrowsAsync<ClientReleasePublishConflictException>(
                () => PublishPluginPackageAsync(handler, wrapper.ZipPath));
            Assert.Single(componentRepository.Items);
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
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            var handler = CreatePluginPackageHandler(
                edgeRoot,
                componentRepository,
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            var result = await PublishPluginPackageAsync(handler, wrapper.ZipPath);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors ?? [], error => error.Contains("CloudApi:ClientCode", StringComparison.Ordinal));
            Assert.Empty(componentRepository.Items);
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "plugins", "stable", "Homogenization", "1.1.2")));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_ShouldRejectCreatedAtWithoutUtcOrOffset()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper(
            "Homogenization",
            "1.1.9",
            createdAtUtcText: "2026-07-11T12:00:00");
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>();
            var publisher = CreatePluginPackageHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            var result = await PublishPluginPackageAsync(publisher, wrapper.ZipPath);

            Assert.False(result.IsSuccess);
            Assert.Contains(
                result.Errors ?? [],
                error => error.Contains("createdAtUtc", StringComparison.Ordinal));
            Assert.Empty(repository.Items);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_ShouldNormalizeCreatedAtOffsetToUtc()
    {
        const string createdAtWithOffset = "2026-07-11T12:00:00+08:00";
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper(
            "Homogenization",
            "1.1.10",
            createdAtUtcText: createdAtWithOffset);
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>();
            var publisher = CreatePluginPackageHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            var result = await PublishPluginPackageAsync(publisher, wrapper.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            var publishedAt = SingleVersion(SingleComponent(
                repository,
                ClientReleaseComponentKind.Plugin,
                "Homogenization")).PublishedAtUtc;
            Assert.NotNull(publishedAt);
            Assert.Equal(DateTimeKind.Utc, publishedAt!.Value.Kind);
            Assert.Equal(
                DateTimeOffset.Parse(createdAtWithOffset).UtcDateTime,
                publishedAt.Value);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldRollbackExactOwnedFiles_WhenPreSaveLookupFails()
    {
        const string sensitiveFailure = "/private/release/SECRET-db-unavailable";
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle("1.2.1");
        Directory.CreateDirectory(Path.Combine(edgeRoot, "velopack", "stable"));
        File.WriteAllText(Path.Combine(edgeRoot, "velopack", "stable", "releases.stable.json"), "old-manifest");
        File.WriteAllText(Path.Combine(edgeRoot, "velopack", "stable", "assets.stable.json"), "old-assets");
        File.WriteAllText(Path.Combine(edgeRoot, "velopack", "stable", "old-1.0.0.nupkg"), "old");
        try
        {
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>
            {
                AnyAsyncPredicateOverride = (_, _) =>
                    Task.FromException<bool>(new InvalidOperationException(sensitiveFailure))
            };
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreatePublishHandler(edgeRoot, componentRepository, new NoopRetentionService(), auditTrail);

            await AssertPublishUnavailableAsync(handler, bundle.ZipPath, auditTrail, sensitiveFailure);

            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", "1.2.1")));
            Assert.Equal("old-manifest", File.ReadAllText(Path.Combine(edgeRoot, "velopack", "stable", "releases.stable.json")));
            Assert.Equal("old-assets", File.ReadAllText(Path.Combine(edgeRoot, "velopack", "stable", "assets.stable.json")));
            Assert.False(File.Exists(Path.Combine(edgeRoot, "velopack", "stable", "IIoT.EdgeClient-1.2.1-full.nupkg")));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldPreservePrimaryFailure_WhenPreSaveRollbackFailsClosed()
    {
        const string version = "1.2.11";
        const string sensitiveFailure = "/private/release/SECRET-primary-db-unavailable";
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle(version);
        var installerTarget = Path.Combine(edgeRoot, "installers", "stable", version);
        var foreignFile = Path.Combine(installerTarget, "competitor-owned.txt");
        try
        {
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>
            {
                AnyAsyncPredicateOverride = (_, _) =>
                {
                    Directory.CreateDirectory(installerTarget);
                    File.WriteAllText(foreignFile, "keep");
                    return Task.FromException<bool>(new InvalidOperationException(sensitiveFailure));
                }
            };
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreatePublishHandler(edgeRoot, componentRepository, new NoopRetentionService(), auditTrail);

            await AssertPublishUnavailableAsync(handler, bundle.ZipPath, auditTrail, sensitiveFailure);

            Assert.True(Directory.Exists(installerTarget));
            Assert.Equal("keep", File.ReadAllText(foreignFile));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_SaveResponseFailure_ShouldPreservePublishedFilesAndReturnUnknown()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle("1.2.8");
        try
        {
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesAsyncOverride = _ => Task.FromException<int>(new IOException("post-commit-response-lost"))
            };
            var handler = CreatePublishHandler(
                edgeRoot,
                componentRepository,
                new NoopRetentionService(),
                new RecordingAuditTrailService());

            await Assert.ThrowsAsync<ClientReleaseCommitUnknownException>(
                () => PublishBundleAsync(handler, bundle.ZipPath));

            Assert.True(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", "1.2.8")));
            Assert.True(File.Exists(Path.Combine(
                edgeRoot,
                "velopack",
                "stable",
                "IIoT.EdgeClient-1.2.8-full.nupkg")));
            Assert.Single(Directory.GetFiles(
                Path.Combine(edgeRoot, "plugins", "stable", "Homogenization", "1.0.0"),
                "*.zip"));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_SaveResponseFailure_ShouldRecoverExactHostAndGeneratedPluginBatch()
    {
        const string version = "1.2.9";
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle(version);
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesAsyncOverride = _ => Task.FromException<int>(new IOException("save-response-lost"))
            };
            var auditTrail = new RecordingAuditTrailService();
            var observationReader = new RepositoryBackedReleaseReader(repository);
            var publisher = CreatePublishHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                auditTrail,
                observationReader);

            var result = await PublishBundleAsync(publisher, bundle.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            Assert.Contains("保留/清理旧版本未执行", result.Value!.CleanupWarning, StringComparison.Ordinal);
            Assert.Equal(1, observationReader.Calls);
            Assert.Equal(2, observationReader.LastIdentities.Count);
            Assert.Contains(observationReader.LastIdentities, identity =>
                identity.ComponentKind == ClientReleaseComponentKind.Host
                && identity.Version == version);
            Assert.Contains(observationReader.LastIdentities, identity =>
                identity.ComponentKind == ClientReleaseComponentKind.Plugin
                && identity.ComponentKey == "Homogenization"
                && identity.Version == "1.0.0");
            Assert.False(File.Exists(Path.Combine(
                edgeRoot,
                "installers",
                "stable",
                version,
                ".iiot-host-publish-owner")));
            Assert.False(File.Exists(Path.Combine(
                edgeRoot,
                "plugins",
                "stable",
                "Homogenization",
                "1.0.0",
                ".iiot-plugin-publish-owner")));
            var audit = Assert.Single(
                auditTrail.Entries,
                entry => entry.OperationType == "ClientRelease.Publish.CommitRecovered");
            Assert.True(audit.Succeeded);
            Assert.Null(audit.FailureReason);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_SaveResponseFailureWithPersistedMismatch_ShouldConflictWithoutDeletingFiles()
    {
        const string version = "1.3.0";
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle(version);
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesAsyncOverride = _ => Task.FromException<int>(new IOException("save-response-lost"))
            };
            var auditTrail = new RecordingAuditTrailService();
            var observationReader = new RepositoryBackedReleaseReader(
                repository,
                observation => observation with { ReleaseNotes = "different-persisted-state" });
            var publisher = CreatePublishHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                auditTrail,
                observationReader);

            await Assert.ThrowsAsync<ClientReleasePublishConflictException>(
                () => PublishBundleAsync(publisher, bundle.ZipPath));

            Assert.Equal(1, observationReader.Calls);
            Assert.True(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", version)));
            Assert.True(File.Exists(Path.Combine(
                edgeRoot,
                "velopack",
                "stable",
                $"IIoT.EdgeClient-{version}-full.nupkg")));
            Assert.Single(Directory.GetFiles(Path.Combine(
                edgeRoot,
                "plugins",
                "stable",
                "Homogenization",
                "1.0.0"), "*.zip"));
            var audit = Assert.Single(
                auditTrail.Entries,
                entry => entry.OperationType == "ClientRelease.Publish.CommitConflict");
            Assert.False(audit.Succeeded);
            Assert.Equal("persisted-state-mismatch", audit.FailureReason);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_SaveResponseFailureWithStaticMismatch_ShouldRemainUnknownWithoutDeletingFiles()
    {
        const string version = "1.3.1";
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle(version);
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesAsyncOverride = _ => Task.FromException<int>(new IOException("save-response-lost"))
            };
            var auditTrail = new RecordingAuditTrailService();
            var installerFile = Path.Combine(
                edgeRoot,
                "installers",
                "stable",
                version,
                "IIoT.Edge.Setup.exe");
            var observationReader = new RepositoryBackedReleaseReader(
                repository,
                beforeRead: () => File.AppendAllText(installerFile, "tampered"));
            var publisher = CreatePublishHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                auditTrail,
                observationReader);

            await Assert.ThrowsAsync<ClientReleaseCommitUnknownException>(
                () => PublishBundleAsync(publisher, bundle.ZipPath));

            Assert.Equal(1, observationReader.Calls);
            Assert.True(File.Exists(installerFile));
            Assert.EndsWith("tampered", File.ReadAllText(installerFile), StringComparison.Ordinal);
            var audit = Assert.Single(
                auditTrail.Entries,
                entry => entry.OperationType == "ClientRelease.Publish.CommitUnknown");
            Assert.False(audit.Succeeded);
            Assert.Equal("commit-state-not-observed", audit.FailureReason);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_SaveCancellation_ShouldObserveAuditAndRethrowWithoutDeletingFiles()
    {
        const string version = "1.3.2";
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle(version);
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesAsyncOverride = _ =>
                    Task.FromException<int>(new OperationCanceledException("lease-lost"))
            };
            var auditTrail = new RecordingAuditTrailService();
            var observationReader = new RepositoryBackedReleaseReader(repository);
            var publisher = CreatePublishHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                auditTrail,
                observationReader);

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => PublishBundleAsync(publisher, bundle.ZipPath));

            Assert.Equal(1, observationReader.Calls);
            Assert.True(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", version)));
            Assert.False(File.Exists(Path.Combine(
                edgeRoot,
                "installers",
                "stable",
                version,
                ".iiot-host-publish-owner")));
            var audit = Assert.Single(
                auditTrail.Entries,
                entry => entry.OperationType == "ClientRelease.Publish.CommittedResponseCancelled");
            Assert.True(audit.Succeeded);
            Assert.Null(audit.FailureReason);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_SaveInvalidDataResponseFailure_ShouldRemainUnknownAndPreserveFiles()
    {
        const string sensitiveFailure = "/private/release/SECRET-invalid-data";
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle("1.2.7");
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesAsyncOverride = _ => Task.FromException<int>(new InvalidDataException(sensitiveFailure))
            };
            var auditTrail = new RecordingAuditTrailService();
            var publisher = CreatePublishHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                auditTrail);

            await Assert.ThrowsAsync<ClientReleaseCommitUnknownException>(
                () => PublishBundleAsync(publisher, bundle.ZipPath));

            var failureAudit = Assert.Single(
                auditTrail.Entries,
                entry => entry.OperationType == "ClientRelease.Publish.CommitUnknown");
            Assert.Equal("commit-state-not-observed", failureAudit.FailureReason);
            Assert.DoesNotContain(sensitiveFailure, failureAudit.FailureReason!, StringComparison.Ordinal);
            Assert.DoesNotContain(sensitiveFailure, failureAudit.Summary, StringComparison.Ordinal);
            Assert.True(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", "1.2.7")));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_SaveResponseFailureWithoutObservation_ShouldPreservePackageAndReturnUnknown()
    {
        const string sensitiveFailure = "/private/release/SECRET-plugin-io";
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper("Homogenization", "1.1.3");
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesAsyncOverride = _ => Task.FromException<int>(new IOException(sensitiveFailure))
            };
            var auditTrail = new RecordingAuditTrailService();
            var publisher = CreatePluginPackageHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                auditTrail);

            await Assert.ThrowsAsync<ClientReleaseCommitUnknownException>(
                () => PublishPluginPackageAsync(publisher, wrapper.ZipPath));

            var failureAudit = Assert.Single(
                auditTrail.Entries,
                entry => entry.OperationType == "ClientRelease.PublishPlugin.CommitUnknown");
            Assert.False(failureAudit.Succeeded);
            Assert.DoesNotContain(sensitiveFailure, failureAudit.FailureReason!, StringComparison.Ordinal);
            Assert.DoesNotContain(sensitiveFailure, failureAudit.Summary, StringComparison.Ordinal);
            Assert.Single(Directory.GetFiles(Path.Combine(
                edgeRoot,
                "plugins",
                "stable",
                "Homogenization",
                "1.1.3"), "*.zip"));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_PostCommitExceptionSimulation_ShouldRecoverExactUppercaseHash()
    {
        const string sensitiveFailure = "/private/release/SECRET-post-commit-response";
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper("Homogenization", "1.1.5", uppercaseSha256: true);
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesAsyncOverride = _ => Task.FromException<int>(new IOException(sensitiveFailure))
            };
            var auditTrail = new RecordingAuditTrailService();
            var observationReader = new RepositoryBackedReleaseReader(
                repository,
                observation => observation with
                {
                    Sha256 = observation.Sha256.ToLowerInvariant(),
                    Artifacts = observation.Artifacts
                        .Select(artifact => artifact with { Sha256 = artifact.Sha256?.ToLowerInvariant() })
                        .ToList()
                });
            var publisher = CreatePluginPackageHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                auditTrail,
                observationReader);

            var result = await PublishPluginPackageAsync(publisher, wrapper.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            Assert.Contains("保留/清理旧版本未执行", result.Value!.CleanupWarning, StringComparison.Ordinal);
            var targetDirectory = Path.Combine(
                edgeRoot,
                "plugins",
                "stable",
                "Homogenization",
                "1.1.5");
            Assert.Single(Directory.GetFiles(targetDirectory, "*.zip"));
            Assert.False(File.Exists(Path.Combine(targetDirectory, ".iiot-plugin-publish-owner")));
            var audit = Assert.Single(
                auditTrail.Entries,
                entry => entry.OperationType == "ClientRelease.PublishPlugin.CommitRecovered");
            Assert.True(audit.Succeeded);
            Assert.DoesNotContain(sensitiveFailure, audit.Summary, StringComparison.Ordinal);
            Assert.Null(audit.FailureReason);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_PostCommitExceptionSimulation_Mismatch_ShouldReturnConflictWithoutDeletingPackage()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper("Homogenization", "1.1.6");
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesAsyncOverride = _ => Task.FromException<int>(new IOException("db-response-lost"))
            };
            var auditTrail = new RecordingAuditTrailService();
            var publisher = CreatePluginPackageHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                auditTrail,
                new RepositoryBackedReleaseReader(
                    repository,
                    observation => observation with { ReleaseNotes = "different-persisted-state" }));

            await Assert.ThrowsAsync<ClientReleasePublishConflictException>(
                () => PublishPluginPackageAsync(publisher, wrapper.ZipPath));

            var targetDirectory = Path.Combine(
                edgeRoot,
                "plugins",
                "stable",
                "Homogenization",
                "1.1.6");
            Assert.Single(Directory.GetFiles(targetDirectory, "*.zip"));
            Assert.True(File.Exists(Path.Combine(targetDirectory, ".iiot-plugin-publish-owner")));
            var audit = Assert.Single(
                auditTrail.Entries,
                entry => entry.OperationType == "ClientRelease.PublishPlugin.CommitConflict");
            Assert.False(audit.Succeeded);
            Assert.Equal("persisted-state-mismatch", audit.FailureReason);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_PostCommitCancellationSimulation_ShouldRethrowAfterSuccessfulAudit()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper("Homogenization", "1.1.7");
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesAsyncOverride = _ => Task.FromException<int>(new OperationCanceledException("lease-lost"))
            };
            var auditTrail = new RecordingAuditTrailService();
            var observationReader = new RepositoryBackedReleaseReader(repository);
            var publisher = CreatePluginPackageHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                auditTrail,
                observationReader);

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => PublishPluginPackageAsync(publisher, wrapper.ZipPath));

            var targetDirectory = Path.Combine(
                edgeRoot,
                "plugins",
                "stable",
                "Homogenization",
                "1.1.7");
            Assert.Single(Directory.GetFiles(targetDirectory, "*.zip"));
            Assert.False(File.Exists(Path.Combine(targetDirectory, ".iiot-plugin-publish-owner")));
            var audit = Assert.Single(
                auditTrail.Entries,
                entry => entry.OperationType == "ClientRelease.PublishPlugin.CommittedResponseCancelled");
            Assert.True(audit.Succeeded);
            Assert.Null(audit.FailureReason);
            Assert.Equal(1, observationReader.Calls);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_CancellationObservedAfterMarkerCleanup_ShouldKeepCleanupIdempotent()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper("Homogenization", "1.1.8");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesAsyncOverride = _ =>
                {
                    cancellation.Cancel();
                    return Task.FromResult(1);
                }
            };
            var auditTrail = new RecordingAuditTrailService();
            var observationReader = new RepositoryBackedReleaseReader(repository);
            var publisher = CreatePluginPackageHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                auditTrail,
                observationReader);

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => PublishPluginPackageAsync(publisher, wrapper.ZipPath, cancellation.Token));

            var targetDirectory = Path.Combine(
                edgeRoot,
                "plugins",
                "stable",
                "Homogenization",
                "1.1.8");
            Assert.Single(Directory.GetFiles(targetDirectory, "*.zip"));
            Assert.False(File.Exists(Path.Combine(targetDirectory, ".iiot-plugin-publish-owner")));
            var audit = Assert.Single(
                auditTrail.Entries,
                entry => entry.OperationType == "ClientRelease.PublishPlugin.CommittedResponseCancelled");
            Assert.True(audit.Succeeded);
            Assert.Equal(1, observationReader.Calls);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_ShouldUseSafeRetentionCleanupWarning()
    {
        const string sensitiveFailure = "/private/release/SECRET-plugin-retention";
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper("Homogenization", "1.1.4");
        try
        {
            var auditTrail = new RecordingAuditTrailService();
            var publisher = CreatePluginPackageHandler(
                edgeRoot,
                new InMemoryRepository<ClientReleaseComponent>(),
                new ThrowingRetentionService(sensitiveFailure),
                auditTrail);

            var result = await PublishPluginPackageAsync(publisher, wrapper.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            Assert.NotNull(result.Value);
            Assert.Equal("插件发布成功，但保留/清理旧版本未完成。", result.Value!.CleanupWarning);
            Assert.DoesNotContain(sensitiveFailure, result.Value.CleanupWarning!, StringComparison.Ordinal);
            Assert.DoesNotContain(
                auditTrail.Entries,
                entry => entry.Summary.Contains(sensitiveFailure, StringComparison.Ordinal)
                         || entry.FailureReason?.Contains(sensitiveFailure, StringComparison.Ordinal) == true);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            wrapper.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldKeepPublishedVersion_WhenRetentionCleanupFails()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle("1.2.2");
        try
        {
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreatePublishHandler(
                edgeRoot,
                componentRepository,
                new ThrowingRetentionService("retention down"),
                auditTrail);

            var result = await PublishBundleAsync(handler, bundle.ZipPath);

            Assert.True(result.IsSuccess, string.Join("; ", result.Errors ?? []));
            Assert.NotNull(result.Value);
            Assert.False(result.Value!.CleanupSucceeded);
            Assert.Equal("Edge 发布成功，但保留/清理旧版本未完成。", result.Value.CleanupWarning);
            Assert.DoesNotContain("retention down", result.Value.CleanupWarning!, StringComparison.Ordinal);
            Assert.DoesNotContain(
                auditTrail.Entries,
                entry => entry.Summary.Contains("retention down", StringComparison.Ordinal)
                         || entry.FailureReason?.Contains("retention down", StringComparison.Ordinal) == true);
            Assert.True(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", "1.2.2")));
            Assert.Equal(2, componentRepository.Items.Count);
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
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(CreateHostComponent(
                "stable",
                "1.0.0",
                "1.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
                new string('c', 64),
                1024,
                "old",
                ClientReleaseStatus.Archived));

            var handler = CreatePublishHandler(
                edgeRoot,
                componentRepository,
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
                new InMemoryRepository<ClientReleaseComponent>(),
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
                new InMemoryRepository<ClientReleaseComponent>(),
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
        var repository = new InMemoryRepository<ClientReleaseComponent>();
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
        var version = SingleVersion(repository.AddedEntity!);
        Assert.Equal("1.2.0", version.Version);
        Assert.Equal("release notes", version.ReleaseNotes);
        Assert.Equal(ClientReleaseStatus.Published, version.Status);
        Assert.NotNull(version.PublishedAtUtc);
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
        var clientStateStore = new InMemoryDeviceClientStateStore();
        var handler = new ReportDeviceClientVersionHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-001")),
            clientStateStore);

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
        Assert.Empty(clientStateStore.VersionSnapshots);
        Assert.Empty(clientStateStore.States);
    }

    [Fact]
    public async Task ReportDeviceClientVersionHandler_ShouldStoreLatestPluginSnapshot()
    {
        var deviceId = Guid.NewGuid();
        var clientStateStore = new InMemoryDeviceClientStateStore();
        var handler = new ReportDeviceClientVersionHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-001")),
            clientStateStore);

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
        var snapshot = Assert.Single(clientStateStore.VersionSnapshots);
        Assert.Equal(deviceId, snapshot.DeviceId);
        Assert.Equal("DEV-001", snapshot.ClientCode);
        var plugin = Assert.Single(snapshot.InstalledPlugins);
        Assert.Equal("Homogenization", plugin.ModuleId);
        Assert.True(plugin.Enabled);
        var state = Assert.Single(clientStateStore.States);
        Assert.Equal(deviceId, state.DeviceId);
        Assert.Equal("DEV-001", state.ClientCode);
        Assert.Equal("1.2.0", state.HostVersion);
        Assert.NotNull(state.VersionReceivedAtUtc);
    }

    [Fact]
    public async Task ReportDeviceRuntimeHeartbeatHandler_ShouldUpsertLatestRuntimeState()
    {
        var deviceId = Guid.NewGuid();
        var utcNow = new DateTimeOffset(2026, 7, 10, 6, 0, 0, TimeSpan.Zero);
        var clientStateStore = new InMemoryDeviceClientStateStore();
        var handler = new ReportDeviceRuntimeHeartbeatHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-001")),
            clientStateStore,
            new FixedTimeProvider(utcNow));

        var first = await handler.Handle(
            new ReportDeviceRuntimeHeartbeatCommand(
                deviceId,
                "DEV-001",
                "runtime-a",
                "LineA",
                "1.0.20",
                "1.0.0",
                "Starting",
                utcNow.UtcDateTime.AddMinutes(-1),
                utcNow.UtcDateTime.AddSeconds(-2)),
            CancellationToken.None);
        var second = await handler.Handle(
            new ReportDeviceRuntimeHeartbeatCommand(
                deviceId,
                "DEV-001",
                "runtime-a",
                "LineA",
                "1.0.20",
                "1.0.0",
                "Running",
                utcNow.UtcDateTime.AddMinutes(-1),
                utcNow.UtcDateTime.AddSeconds(-1),
                ["10.0.0.8"]),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        var heartbeat = Assert.Single(clientStateStore.RuntimeHeartbeats);
        Assert.Equal("Running", heartbeat.Status);
        Assert.Equal(["10.0.0.8"], heartbeat.GetLocalIpAddresses());
        var state = Assert.Single(clientStateStore.States);
        Assert.Equal("Running", state.RuntimeStatus);
        Assert.Equal(["10.0.0.8"], state.GetRuntimeLocalIpAddresses());
        Assert.NotNull(state.LastRuntimeHeartbeatAtUtc);
    }

    [Fact]
    public async Task ReportDeviceRuntimeHeartbeatHandler_ShouldRejectStaleAndConflictingReports()
    {
        var deviceId = Guid.NewGuid();
        var utcNow = new DateTimeOffset(2026, 7, 10, 6, 0, 0, TimeSpan.Zero);
        var clientStateStore = new InMemoryDeviceClientStateStore();
        var handler = new ReportDeviceRuntimeHeartbeatHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-001")),
            clientStateStore,
            new FixedTimeProvider(utcNow));
        var acceptedAt = utcNow.UtcDateTime.AddSeconds(-1);

        var accepted = await handler.Handle(
            CreateRuntimeHeartbeatCommand(deviceId, "Running", acceptedAt),
            CancellationToken.None);
        var stale = await handler.Handle(
            CreateRuntimeHeartbeatCommand(deviceId, "Stopped", acceptedAt.AddSeconds(-1)),
            CancellationToken.None);
        var conflict = await handler.Handle(
            CreateRuntimeHeartbeatCommand(deviceId, "Stopped", acceptedAt),
            CancellationToken.None);

        Assert.True(accepted.IsSuccess);
        Assert.False(stale.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, stale.Status);
        Assert.False(conflict.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, conflict.Status);
        var heartbeat = Assert.Single(clientStateStore.RuntimeHeartbeats);
        Assert.Equal("Running", heartbeat.Status);
        Assert.Equal(acceptedAt, heartbeat.LastHeartbeatAtUtc);
        Assert.Equal("Running", Assert.Single(clientStateStore.States).RuntimeStatus);
    }

    [Fact]
    public async Task ReportDeviceRuntimeHeartbeatHandler_ShouldTreatExactDuplicateAsIdempotent()
    {
        var deviceId = Guid.NewGuid();
        var utcNow = new DateTimeOffset(2026, 7, 10, 6, 0, 0, TimeSpan.Zero);
        var clientStateStore = new InMemoryDeviceClientStateStore();
        var handler = new ReportDeviceRuntimeHeartbeatHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-001")),
            clientStateStore,
            new FixedTimeProvider(utcNow));
        var command = CreateRuntimeHeartbeatCommand(deviceId, "Running", utcNow.UtcDateTime.AddSeconds(-1));

        var first = await handler.Handle(command, CancellationToken.None);
        var duplicate = await handler.Handle(command, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(duplicate.IsSuccess);
        Assert.Equal(1, clientStateStore.SaveChangesCalls);
        Assert.Single(clientStateStore.RuntimeHeartbeats);
        Assert.Single(clientStateStore.States);
    }

    [Fact]
    public async Task ReportDeviceRuntimeHeartbeatHandler_ShouldRejectInvalidTimeRelationships()
    {
        var deviceId = Guid.NewGuid();
        var utcNow = new DateTimeOffset(2026, 7, 10, 6, 0, 0, TimeSpan.Zero);
        var clientStateStore = new InMemoryDeviceClientStateStore();
        var handler = new ReportDeviceRuntimeHeartbeatHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-001")),
            clientStateStore,
            new FixedTimeProvider(utcNow));

        var startedAfterReport = await handler.Handle(
            CreateRuntimeHeartbeatCommand(
                deviceId,
                "Running",
                utcNow.UtcDateTime,
                startedAtUtc: utcNow.UtcDateTime.AddSeconds(1)),
            CancellationToken.None);
        var futureBoundary = await handler.Handle(
            CreateRuntimeHeartbeatCommand(
                deviceId,
                "Running",
                utcNow.UtcDateTime.Add(DeviceClientSoftwareStatusResolver.MaximumFutureClockSkew)),
            CancellationToken.None);
        var beyondFutureBoundary = await handler.Handle(
            CreateRuntimeHeartbeatCommand(
                deviceId,
                "Running",
                utcNow.UtcDateTime.Add(DeviceClientSoftwareStatusResolver.MaximumFutureClockSkew).AddTicks(1)),
            CancellationToken.None);

        Assert.False(startedAfterReport.IsSuccess);
        Assert.True(futureBoundary.IsSuccess);
        Assert.False(beyondFutureBoundary.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, beyondFutureBoundary.Status);
    }

    [Fact]
    public void ReportDeviceRuntimeHeartbeatCommand_ShouldUseDeviceScopedDistributedLock()
    {
        var attribute = Assert.IsType<DistributedLockAttribute>(Attribute.GetCustomAttribute(
            typeof(ReportDeviceRuntimeHeartbeatCommand),
            typeof(DistributedLockAttribute)));

        Assert.Equal("iiot:lock:device-runtime-heartbeat:{DeviceId}", attribute.KeyTemplate);
    }

    [Fact]
    public async Task ReportDeviceRuntimeHeartbeatPipeline_ShouldKeepNewerStateUnderConcurrentDelivery()
    {
        var deviceId = Guid.NewGuid();
        var utcNow = new DateTimeOffset(2026, 7, 10, 6, 0, 0, TimeSpan.Zero);
        var clientStateStore = new InMemoryDeviceClientStateStore();
        var handler = new ReportDeviceRuntimeHeartbeatHandler(
            new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-001")),
            clientStateStore,
            new FixedTimeProvider(utcNow));
        var lockService = new InMemoryKeyedDistributedLockService();
        var behavior = new DistributedLockBehavior<
            ReportDeviceRuntimeHeartbeatCommand,
            Result<DeviceRuntimeHeartbeatResultDto>>(
                lockService,
                NullLogger<DistributedLockBehavior<
                    ReportDeviceRuntimeHeartbeatCommand,
                    Result<DeviceRuntimeHeartbeatResultDto>>>.Instance);
        var older = CreateRuntimeHeartbeatCommand(
            deviceId,
            "Starting",
            utcNow.UtcDateTime.AddSeconds(-2));
        var newer = CreateRuntimeHeartbeatCommand(
            deviceId,
            "Running",
            utcNow.UtcDateTime.AddSeconds(-1));

        async Task<Result<DeviceRuntimeHeartbeatResultDto>> InvokeAsync(
            ReportDeviceRuntimeHeartbeatCommand command)
        {
            return await behavior.Handle(
                command,
                cancellationToken => handler.Handle(command, cancellationToken),
                CancellationToken.None);
        }

        var results = await Task.WhenAll(
            Task.Run(() => InvokeAsync(older)),
            Task.Run(() => InvokeAsync(newer)));

        Assert.True(results[1].IsSuccess);
        var heartbeat = Assert.Single(clientStateStore.RuntimeHeartbeats);
        Assert.Equal(newer.ReportedAtUtc, heartbeat.LastHeartbeatAtUtc);
        Assert.Equal("Running", heartbeat.Status);
        Assert.Equal("Running", Assert.Single(clientStateStore.States).RuntimeStatus);
        Assert.All(
            lockService.AcquiredResources,
            resource => Assert.Equal($"iiot:lock:device-runtime-heartbeat:{deviceId}", resource));
    }

    private static ReportDeviceRuntimeHeartbeatCommand CreateRuntimeHeartbeatCommand(
        Guid deviceId,
        string status,
        DateTime reportedAtUtc,
        DateTime? startedAtUtc = null)
    {
        return new ReportDeviceRuntimeHeartbeatCommand(
            deviceId,
            "DEV-001",
            "runtime-a",
            "LineA",
            "1.0.20",
            "1.0.0",
            status,
            startedAtUtc ?? reportedAtUtc.AddMinutes(-1),
            reportedAtUtc,
            ["10.0.0.8"]);
    }

    [Fact]
    public async Task DeviceInventory_ShouldSeparateInstallStatusFromRuntimeHeartbeat()
    {
        var processId = Guid.NewGuid();
        var device = new Device("正极模切", "DEV-001", processId);
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Items.Add(device);
        var clientStateStore = new InMemoryDeviceClientStateStore();
        var snapshot = new DeviceClientVersionSnapshot(
            device.Id,
            device.Code,
            "1.0.0",
            "1.0.0",
            "stable",
            DateTime.UtcNow,
            []);
        clientStateStore.VersionSnapshots.Add(snapshot);
        var state = new DeviceClientState(device.Id, device.Code);
        state.ApplyVersionReport(snapshot);
        clientStateStore.States.Add(state);
        var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
        componentRepository.Items.Add(CreateHostComponent(
            "stable",
            "1.0.0",
            "1.0.0",
            "win-x64",
            "net10.0",
            "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
            new string('a', 64),
            1024,
            "host",
            ClientReleaseStatus.Published));

        var handler = new GetDeviceClientVersionInventoryHandler(
            new StubCurrentUserDeviceAccessService(),
            deviceRepository,
            clientStateStore,
            componentRepository);

        var result = await handler.Handle(
            new GetDeviceClientVersionInventoryQuery("stable", "win-x64"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!);
        Assert.Equal("Normal", row.InstallStatus);
        Assert.Equal("MissingRuntimeHeartbeat", row.SoftwareStatus);
        Assert.NotEqual("Offline", row.InstallStatus);
        Assert.Equal("客户端尚未上报运行心跳。", row.Issue);
    }

    [Fact]
    public async Task DeleteClientReleasePackageHandler_ShouldRejectHostReleaseStillInUse()
    {
        var edgeRoot = CreateTempDirectory("iiot-delete-release-root");
        try
        {
            var hostDirectory = Path.Combine(edgeRoot, "installers", "stable", "1.0.0");
            Directory.CreateDirectory(hostDirectory);
            File.WriteAllText(Path.Combine(hostDirectory, "installer-artifact.json"), "{}");
            var component = CreateHostComponent(
                "stable",
                "1.0.0",
                "1.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
                new string('a', 64),
                1024,
                "host",
                ClientReleaseStatus.Published);
            var hostRelease = SingleVersion(component);
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(component);
            var deviceId = Guid.NewGuid();
            var clientStateStore = new InMemoryDeviceClientStateStore();
            clientStateStore.VersionSnapshots.Add(new DeviceClientVersionSnapshot(
                deviceId,
                "DEV-001",
                "1.0.0",
                "1.0.0",
                "stable",
                DateTime.UtcNow,
                []));
            var auditTrail = new RecordingAuditTrailService();
            var handler = new DeleteClientReleasePackageHandler(
                Options.Create(new EdgeInstallerArtifactOptions { RootPath = Path.Combine(edgeRoot, "installers") }),
                componentRepository,
                clientStateStore,
                new TestCurrentUser(),
                auditTrail,
                NullLogger<DeleteClientReleasePackageHandler>.Instance);

            var result = await handler.Handle(
                new DeleteClientReleasePackageCommand(hostRelease.Id),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(ClientReleaseStatus.Published, hostRelease.Status);
            Assert.True(Directory.Exists(hostDirectory));
            Assert.Contains(auditTrail.Entries, entry =>
                entry.OperationType == "ClientRelease.DeletePackage"
                && !entry.Succeeded);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task HardDeleteClientReleaseComponentHandler_ShouldDeletePluginMetadataAndFiles()
    {
        var edgeRoot = CreateTempDirectory("iiot-hard-delete-plugin-root");
        try
        {
            const string moduleId = "DieCutting";
            var moduleDirectory = Path.Combine(edgeRoot, "plugins", "stable", moduleId);
            var packagePath = Path.Combine(moduleDirectory, "1.0.0", "die-cutting.zip");
            WriteFile(packagePath, "plugin-package");
            var component = CreatePluginComponent(
                moduleId,
                "模切",
                "stable",
                "1.0.0",
                "1.0.0",
                "1.0.0",
                "2.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/plugins/stable/DieCutting/1.0.0/die-cutting.zip",
                new string('a', 64),
                1024,
                "错误工序，管理员永久删除",
                ClientReleaseStatus.Published);
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(component);
            var deletionStore = new InMemoryClientReleaseComponentDeletionStore();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreateHardDeleteHandler(
                edgeRoot,
                componentRepository,
                deletionStore,
                auditTrail);

            var result = await handler.Handle(
                new HardDeleteClientReleaseComponentCommand(component.Id, "错误工序"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Empty(componentRepository.Items);
            Assert.Empty(deletionStore.Items);
            Assert.False(Directory.Exists(moduleDirectory));
            Assert.Contains(auditTrail.Entries, entry =>
                entry.OperationType == "ClientRelease.HardDeleteComponent"
                && entry.Succeeded);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task HardDeleteClientReleaseComponentHandler_ShouldRejectPluginStillInUse()
    {
        var edgeRoot = CreateTempDirectory("iiot-hard-delete-in-use-root");
        try
        {
            const string moduleId = "DieCutting";
            var moduleDirectory = Path.Combine(edgeRoot, "plugins", "stable", moduleId, "1.0.0");
            WriteFile(Path.Combine(moduleDirectory, "die-cutting.zip"), "plugin-package");
            var component = CreatePluginComponent(
                moduleId,
                "模切",
                "stable",
                "1.0.0",
                "1.0.0",
                "1.0.0",
                "2.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/plugins/stable/DieCutting/1.0.0/die-cutting.zip",
                new string('a', 64),
                1024,
                "仍在使用",
                ClientReleaseStatus.Published);
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(component);
            var deletionStore = new InMemoryClientReleaseComponentDeletionStore();
            var clientStateStore = new InMemoryDeviceClientStateStore();
            clientStateStore.VersionSnapshots.Add(new DeviceClientVersionSnapshot(
                Guid.NewGuid(),
                "DEV-001",
                "1.0.0",
                "1.0.0",
                "stable",
                DateTime.UtcNow,
                [new DeviceClientPluginVersion(moduleId, "模切", "1.0.0", "1.0.0", true)]));
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreateHardDeleteHandler(
                edgeRoot,
                componentRepository,
                deletionStore,
                auditTrail,
                clientStateStore);

            var result = await handler.Handle(
                new HardDeleteClientReleaseComponentCommand(component.Id),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Single(componentRepository.Items);
            Assert.Empty(deletionStore.Items);
            Assert.True(Directory.Exists(moduleDirectory));
            Assert.Contains(auditTrail.Entries, entry =>
                entry.OperationType == "ClientRelease.HardDeleteComponent"
                && !entry.Succeeded);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task GetPublicClientDownloadsHandler_ShouldExposeOnlyPublishedHostAndPluginCatalog()
    {
        var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
        var hostComponent = CreateHostComponent(
            "stable",
            "99.0.0",
            "1.0.0",
            "win-x64",
            "net10.0",
            "https://download.example.test/host-draft.zip",
            new string('a', 64),
            1024,
            null,
            ClientReleaseStatus.Draft);
        hostComponent.UpsertHostVersion(
            "1.1.0",
            "1.0.0",
            "net10.0",
            "https://download.example.test/host-1.1.0.zip",
            new string('b', 64),
            2048,
            "host release",
            ClientReleaseStatus.Published,
            "host-signature",
            "IIoT",
            artifacts:
            [
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.ManifestFile,
                    "installers/stable/1.1.0/installer-artifact.json",
                    new string('b', 64),
                    2048)
            ]);
        componentRepository.Items.Add(hostComponent);

        componentRepository.Items.Add(CreatePluginComponent(
            "Injection",
            "注液",
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
            ClientReleaseStatus.Published));
        componentRepository.Items.Add(CreatePluginComponent(
            "Welding",
            "焊接",
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
            ClientReleaseStatus.Draft));

        var handler = new GetPublicClientDownloadsHandler(
            componentRepository,
            new FixedRetentionPolicyReader());

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
    public async Task HardDeleteClientReleaseComponentHandler_ShouldRebuildHostVelopackManifestsAndRemoveOrphanNupkg()
    {
        var edgeRoot = CreateTempDirectory("iiot-hard-delete-host-manifest-root");
        try
        {
            var channel = "stable";
            var velopackRoot = Path.Combine(edgeRoot, "velopack", channel);
            Directory.CreateDirectory(velopackRoot);

            var component = ClientReleaseComponent.CreateHost(channel, "win-x64");
            component.UpsertHostVersion(
                "1.0.0",
                "1.0.0",
                "net10.0",
                "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
                new string('a', 64),
                100,
                "old",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                artifacts:
                [
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.InstallerDirectory,
                        "installers/stable/1.0.0"),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.ManifestFile,
                        "installers/stable/1.0.0/installer-artifact.json",
                        new string('a', 64),
                        100),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.VelopackFile,
                        "velopack/stable/IIoT.EdgeClient-1.0.0-full.nupkg",
                        new string('a', 64),
                        100)
                ]);
            component.UpsertHostVersion(
                "1.1.0",
                "1.0.0",
                "net10.0",
                "/edge-updates/installers/stable/1.1.0/installer-artifact.json",
                new string('b', 64),
                200,
                "current",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                artifacts:
                [
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.InstallerDirectory,
                        "installers/stable/1.1.0"),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.ManifestFile,
                        "installers/stable/1.1.0/installer-artifact.json",
                        new string('b', 64),
                        200),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.VelopackFile,
                        "velopack/stable/IIoT.EdgeClient-1.1.0-full.nupkg",
                        new string('b', 64),
                        200)
                ]);

            WriteFile(
                Path.Combine(velopackRoot, "IIoT.EdgeClient-1.0.0-full.nupkg"),
                "nupkg-1.0.0");
            WriteFile(
                Path.Combine(velopackRoot, "IIoT.EdgeClient-1.1.0-full.nupkg"),
                "nupkg-1.1.0");
            WriteFile(
                Path.Combine(velopackRoot, "releases.stable.json"),
                """
                {"packages":["IIoT.EdgeClient-1.0.0-full.nupkg","IIoT.EdgeClient-1.1.0-full.nupkg"]}
                """);
            WriteFile(
                Path.Combine(velopackRoot, "assets.stable.json"),
                """
                {"assets":["IIoT.EdgeClient-1.0.0-full.nupkg","IIoT.EdgeClient-1.1.0-full.nupkg"]}
                """);
            WriteFile(
                Path.Combine(velopackRoot, "RELEASES"),
                "hash1 IIoT.EdgeClient-1.0.0-full.nupkg 100\nhash2 IIoT.EdgeClient-1.1.0-full.nupkg 200");

            var installer100 = Path.Combine(edgeRoot, "installers", channel, "1.0.0");
            var installer110 = Path.Combine(edgeRoot, "installers", channel, "1.1.0");
            Directory.CreateDirectory(installer100);
            Directory.CreateDirectory(installer110);
            WriteFile(Path.Combine(installer100, "installer-artifact.json"), "{}");
            WriteFile(Path.Combine(installer110, "installer-artifact.json"), "{}");

            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(component);
            var deletionStore = new InMemoryClientReleaseComponentDeletionStore();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreateHardDeleteHandler(
                edgeRoot,
                componentRepository,
                deletionStore,
                auditTrail);

            var result = await handler.Handle(
                new HardDeleteClientReleaseComponentCommand(component.Id, "wrong channel"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Empty(componentRepository.Items);
            Assert.Empty(deletionStore.Items);
            Assert.False(Directory.Exists(installer100));
            Assert.False(Directory.Exists(installer110));
            Assert.False(File.Exists(Path.Combine(velopackRoot, "IIoT.EdgeClient-1.0.0-full.nupkg")));
            Assert.False(File.Exists(Path.Combine(velopackRoot, "IIoT.EdgeClient-1.1.0-full.nupkg")));

            var releasesStable = File.ReadAllText(Path.Combine(velopackRoot, "releases.stable.json"));
            Assert.DoesNotContain("IIoT.EdgeClient-1.0.0-full.nupkg", releasesStable);
            Assert.DoesNotContain("IIoT.EdgeClient-1.1.0-full.nupkg", releasesStable);
            var assetsStable = File.ReadAllText(Path.Combine(velopackRoot, "assets.stable.json"));
            Assert.DoesNotContain("IIoT.EdgeClient-1.0.0-full.nupkg", assetsStable);
            Assert.DoesNotContain("IIoT.EdgeClient-1.1.0-full.nupkg", assetsStable);
            var releases = File.ReadAllText(Path.Combine(velopackRoot, "RELEASES"));
            Assert.DoesNotContain("IIoT.EdgeClient-1.0.0-full.nupkg", releases);
            Assert.DoesNotContain("IIoT.EdgeClient-1.1.0-full.nupkg", releases);

            Assert.Contains(auditTrail.Entries, entry =>
                entry.OperationType == "ClientRelease.HardDeleteComponent"
                && entry.Succeeded);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task HardDeleteClientReleaseComponentHandler_ShouldKeepNupkgStillUsedByAnotherComponent()
    {
        var edgeRoot = CreateTempDirectory("iiot-hard-delete-host-true-shared-nupkg-root");
        try
        {
            var channel = "stable";
            var velopackRoot = Path.Combine(edgeRoot, "velopack", channel);
            Directory.CreateDirectory(velopackRoot);

            var target = ClientReleaseComponent.CreateHost(channel, "win-x64");
            target.UpsertHostVersion(
                "1.0.0",
                "1.0.0",
                "net10.0",
                "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
                new string('a', 64),
                100,
                "target",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                artifacts:
                [
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.InstallerDirectory,
                        "installers/stable/1.0.0"),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.ManifestFile,
                        "installers/stable/1.0.0/installer-artifact.json",
                        new string('a', 64),
                        100),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.VelopackFile,
                        "velopack/stable/IIoT.EdgeClient-1.0.0-full.nupkg",
                        new string('a', 64),
                        100)
                ]);

            var survivor = ClientReleaseComponent.CreatePlugin(
                "Homogenization",
                "匀浆",
                null,
                null,
                null,
                channel,
                "win-x64");
            survivor.UpsertPluginVersion(
                "2.0.0",
                "1.0.0",
                "1.0.0",
                "99.0.0",
                "net10.0",
                "/edge-updates/plugins/stable/Homogenization/2.0.0/homogenization.zip",
                new string('b', 64),
                200,
                "survivor",
                "[]",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                artifacts:
                [
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.PluginPackageDirectory,
                        "plugins/stable/Homogenization/2.0.0"),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.PackageFile,
                        "plugins/stable/Homogenization/2.0.0/homogenization.zip",
                        new string('b', 64),
                        200),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.VelopackFile,
                        "velopack/stable/IIoT.EdgeClient-1.0.0-full.nupkg",
                        new string('a', 64),
                        100)
                ]);

            WriteFile(
                Path.Combine(velopackRoot, "IIoT.EdgeClient-1.0.0-full.nupkg"),
                "shared-nupkg");
            WriteFile(
                Path.Combine(velopackRoot, "releases.stable.json"),
                """
                {"packages":["IIoT.EdgeClient-1.0.0-full.nupkg"]}
                """);

            var installer100 = Path.Combine(edgeRoot, "installers", channel, "1.0.0");
            Directory.CreateDirectory(installer100);
            WriteFile(Path.Combine(installer100, "installer-artifact.json"), "{}");

            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(target);
            componentRepository.Items.Add(survivor);
            var deletionStore = new InMemoryClientReleaseComponentDeletionStore();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreateHardDeleteHandler(
                edgeRoot,
                componentRepository,
                deletionStore,
                auditTrail);

            var result = await handler.Handle(
                new HardDeleteClientReleaseComponentCommand(target.Id),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(Directory.Exists(installer100));
            Assert.True(File.Exists(Path.Combine(velopackRoot, "IIoT.EdgeClient-1.0.0-full.nupkg")));
            Assert.Contains(
                "IIoT.EdgeClient-1.0.0-full.nupkg",
                File.ReadAllText(Path.Combine(velopackRoot, "releases.stable.json")));
            Assert.Single(componentRepository.Items, survivor);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task HardDeleteClientReleaseComponentHandler_ShouldRemoveNupkgReferenceFromManifest_WhenNoOtherComponentUsesIt()
    {
        var edgeRoot = CreateTempDirectory("iiot-hard-delete-host-shared-nupkg-root");
        try
        {
            var channel = "stable";
            var velopackRoot = Path.Combine(edgeRoot, "velopack", channel);
            Directory.CreateDirectory(velopackRoot);

            var component = ClientReleaseComponent.CreateHost(channel, "win-x64");
            component.UpsertHostVersion(
                "1.0.0",
                "1.0.0",
                "net10.0",
                "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
                new string('a', 64),
                100,
                "old",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                artifacts:
                [
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.InstallerDirectory,
                        "installers/stable/1.0.0"),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.ManifestFile,
                        "installers/stable/1.0.0/installer-artifact.json",
                        new string('a', 64),
                        100),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.VelopackFile,
                        "velopack/stable/IIoT.EdgeClient-1.0.0-full.nupkg",
                        new string('a', 64),
                        100)
                ]);

            WriteFile(
                Path.Combine(velopackRoot, "IIoT.EdgeClient-1.0.0-full.nupkg"),
                "shared-nupkg");
            WriteFile(
                Path.Combine(velopackRoot, "releases.stable.json"),
                """
                {"packages":["IIoT.EdgeClient-1.0.0-full.nupkg"]}
                """);

            var installer100 = Path.Combine(edgeRoot, "installers", channel, "1.0.0");
            Directory.CreateDirectory(installer100);
            WriteFile(Path.Combine(installer100, "installer-artifact.json"), "{}");

            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(component);
            var deletionStore = new InMemoryClientReleaseComponentDeletionStore();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreateHardDeleteHandler(
                edgeRoot,
                componentRepository,
                deletionStore,
                auditTrail);

            var result = await handler.Handle(
                new HardDeleteClientReleaseComponentCommand(component.Id),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(Directory.Exists(installer100));
            Assert.False(File.Exists(Path.Combine(velopackRoot, "IIoT.EdgeClient-1.0.0-full.nupkg")));
            Assert.DoesNotContain(
                "IIoT.EdgeClient-1.0.0-full.nupkg",
                File.ReadAllText(Path.Combine(velopackRoot, "releases.stable.json")));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task HardDeleteClientReleaseComponentHandler_ShouldReturnManifestRebuildFailure_WhenManifestIsInvalidJson()
    {
        var edgeRoot = CreateTempDirectory("iiot-hard-delete-host-invalid-manifest-root");
        try
        {
            var channel = "stable";
            var velopackRoot = Path.Combine(edgeRoot, "velopack", channel);
            Directory.CreateDirectory(velopackRoot);

            var component = ClientReleaseComponent.CreateHost(channel, "win-x64");
            component.UpsertHostVersion(
                "1.0.0",
                "1.0.0",
                "net10.0",
                "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
                new string('a', 64),
                100,
                "old",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                artifacts:
                [
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.InstallerDirectory,
                        "installers/stable/1.0.0"),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.ManifestFile,
                        "installers/stable/1.0.0/installer-artifact.json",
                        new string('a', 64),
                        100)
                ]);

            WriteFile(Path.Combine(velopackRoot, "releases.stable.json"), "not json");

            var installer100 = Path.Combine(edgeRoot, "installers", channel, "1.0.0");
            Directory.CreateDirectory(installer100);
            WriteFile(Path.Combine(installer100, "installer-artifact.json"), "{}");

            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(component);
            var deletionStore = new InMemoryClientReleaseComponentDeletionStore();
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreateHardDeleteHandler(
                edgeRoot,
                componentRepository,
                deletionStore,
                auditTrail);

            var result = await handler.Handle(
                new HardDeleteClientReleaseComponentCommand(component.Id),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains(
                ClientReleaseComponentDeletionExecutor.FailureManifestRebuild,
                result.Errors?.First() ?? string.Empty);
            Assert.True(File.Exists(Path.Combine(velopackRoot, "releases.stable.json")));
            // 组件元数据已删除，持久化删除操作保持 Failed，可修复 manifest 后按操作 ID 重试。
            Assert.Empty(componentRepository.Items);
            var operation = Assert.Single(deletionStore.Items);
            Assert.Equal(ClientReleaseComponentDeletionStatus.Failed, operation.Status);
            Assert.Equal(ClientReleaseComponentDeletionExecutor.FailureManifestRebuild, operation.FailureCode);
            Assert.Equal(component.Id, operation.ComponentId);
            Assert.Contains(auditTrail.Entries, entry =>
                entry.OperationType == "ClientRelease.HardDeleteComponent"
                && !entry.Succeeded
                && entry.FailureReason == ClientReleaseComponentDeletionExecutor.FailureManifestRebuild);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task RetryClientReleaseComponentDeletionHandler_ShouldRecoverFromPersistedOperation_AfterOriginalHandlerIsGone()
    {
        var edgeRoot = CreateTempDirectory("iiot-hard-delete-retry-root");
        try
        {
            const string moduleId = "DieCutting";
            var moduleDirectory = Path.Combine(edgeRoot, "plugins", "stable", moduleId);
            var packagePath = Path.Combine(moduleDirectory, "1.0.0", "die-cutting.zip");
            WriteFile(packagePath, "plugin-package");

            var component = CreatePluginComponent(
                moduleId,
                "模切",
                "stable",
                "1.0.0",
                "1.0.0",
                "1.0.0",
                "2.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/plugins/stable/DieCutting/1.0.0/die-cutting.zip",
                new string('a', 64),
                1024,
                "错误工序，管理员永久删除",
                ClientReleaseStatus.Published);

            // 第一次提交在元数据事务里持久化删除操作，随后模拟进程中断：
            // 清理处理器一启动就“崩溃”，请求失败但操作已留在 store（Requested）。
            var committedComponentRepository = new InMemoryRepository<ClientReleaseComponent>();
            committedComponentRepository.Items.Add(component);
            var deletionStore = new InMemoryClientReleaseComponentDeletionStore();
            var auditTrail = new RecordingAuditTrailService();
            var crashingProcessor = new CancellationAfterCommitProcessor(
                CreateDeletionProcessor(edgeRoot, committedComponentRepository, deletionStore),
                () => throw new InvalidOperationException("模拟进程中断"));
            var firstHandler = new HardDeleteClientReleaseComponentHandler(
                Options.Create(new EdgeInstallerArtifactOptions { RootPath = Path.Combine(edgeRoot, "installers") }),
                committedComponentRepository,
                new InMemoryDeviceClientStateStore(),
                deletionStore,
                crashingProcessor,
                new TestCurrentUser(),
                auditTrail,
                NullLogger<HardDeleteClientReleaseComponentHandler>.Instance);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                firstHandler.Handle(
                    new HardDeleteClientReleaseComponentCommand(component.Id, "错误工序"),
                    CancellationToken.None));
            Assert.Empty(committedComponentRepository.Items);
            var operation = Assert.Single(deletionStore.Items);
            Assert.Equal(ClientReleaseComponentDeletionStatus.Requested, operation.Status);

            // 中断后现场：数据库事务已提交（组件没了、操作还在），但文件清理没执行完。
            Assert.True(Directory.Exists(moduleDirectory));

            // 新“进程”：全新 repository/processor/handler，只按操作 ID 重试，拿不到旧组件内存对象。
            var recoveryComponentRepository = new InMemoryRepository<ClientReleaseComponent>();
            var recoveryProcessor = CreateDeletionProcessor(
                edgeRoot,
                recoveryComponentRepository,
                deletionStore);
            var retryHandler = CreateRetryHandler(deletionStore, recoveryProcessor, auditTrail);

            var retry = await retryHandler.Handle(
                new RetryClientReleaseComponentDeletionCommand(operation.Id),
                CancellationToken.None);

            Assert.True(
                retry.IsSuccess,
                $"status={retry.Status} errors={string.Join("|", retry.Errors ?? [])} valueSucceeded={retry.Value?.Succeeded} failureCode={retry.Value?.FailureCode} storeCount={deletionStore.Items.Count}");
            Assert.True(retry.Value!.Succeeded);
            Assert.False(Directory.Exists(moduleDirectory));
            Assert.Empty(deletionStore.Items);
            Assert.Contains(auditTrail.Entries, entry =>
                entry.OperationType == "ClientRelease.RetryHardDeleteComponent"
                && entry.Succeeded);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task HardDeleteClientReleaseComponentHandler_ShouldLeavePendingOperation_WhenCancelledAfterCommit()
    {
        var edgeRoot = CreateTempDirectory("iiot-hard-delete-cancel-after-commit-root");
        try
        {
            const string moduleId = "DieCutting";
            var moduleDirectory = Path.Combine(edgeRoot, "plugins", "stable", moduleId);
            WriteFile(Path.Combine(moduleDirectory, "1.0.0", "die-cutting.zip"), "plugin-package");
            var component = CreatePluginComponent(
                moduleId,
                "模切",
                "stable",
                "1.0.0",
                "1.0.0",
                "1.0.0",
                "2.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/plugins/stable/DieCutting/1.0.0/die-cutting.zip",
                new string('a', 64),
                1024,
                "提交后取消",
                ClientReleaseStatus.Published);

            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(component);
            var deletionStore = new InMemoryClientReleaseComponentDeletionStore();
            var committed = false;
            var auditTrail = new RecordingAuditTrailService();
            var processorOptions = Options.Create(
                new EdgeInstallerArtifactOptions { RootPath = Path.Combine(edgeRoot, "installers") });
            var commitCount = 0;
            var cancellation = new CancellationTokenSource();
            var processor = new ClientReleaseComponentDeletionProcessor(
                processorOptions,
                componentRepository,
                deletionStore,
                NullLogger<ClientReleaseComponentDeletionProcessor>.Instance);
            var handler = new HardDeleteClientReleaseComponentHandler(
                processorOptions,
                componentRepository,
                new InMemoryDeviceClientStateStore(),
                deletionStore,
                new CancellationAfterCommitProcessor(processor, () =>
                {
                    commitCount += 1;
                    if (commitCount == 1)
                    {
                        committed = true;
                        cancellation.Cancel();
                    }
                }),
                new TestCurrentUser(),
                auditTrail,
                NullLogger<HardDeleteClientReleaseComponentHandler>.Instance);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                handler.Handle(
                    new HardDeleteClientReleaseComponentCommand(component.Id, "提交后取消"),
                    cancellation.Token));

            // 数据库提交发生在取消之前：组件已删、操作已持久化，文件清理被中断，等启动恢复或管理员重试。
            Assert.True(committed);
            Assert.Empty(componentRepository.Items);
            var operation = Assert.Single(deletionStore.Items);
            Assert.Equal(ClientReleaseComponentDeletionStatus.Requested, operation.Status);

            var recoveryProcessor = CreateDeletionProcessor(
                edgeRoot,
                new InMemoryRepository<ClientReleaseComponent>(),
                deletionStore);
            var retry = await CreateRetryHandler(deletionStore, recoveryProcessor, auditTrail).Handle(
                new RetryClientReleaseComponentDeletionCommand(operation.Id),
                CancellationToken.None);

            Assert.True(retry.IsSuccess);
            Assert.True(retry.Value!.Succeeded);
            Assert.False(Directory.Exists(moduleDirectory));
            Assert.Empty(deletionStore.Items);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task RetryClientReleaseComponentDeletionHandler_ShouldConverge_AfterFixingInvalidManifest()
    {
        var edgeRoot = CreateTempDirectory("iiot-hard-delete-fix-manifest-retry-root");
        try
        {
            var channel = "stable";
            var velopackRoot = Path.Combine(edgeRoot, "velopack", channel);
            Directory.CreateDirectory(velopackRoot);
            var component = ClientReleaseComponent.CreateHost(channel, "win-x64");
            component.UpsertHostVersion(
                "1.0.0",
                "1.0.0",
                "net10.0",
                "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
                new string('a', 64),
                100,
                "old",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                artifacts:
                [
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.InstallerDirectory,
                        "installers/stable/1.0.0"),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.ManifestFile,
                        "installers/stable/1.0.0/installer-artifact.json",
                        new string('a', 64),
                        100)
                ]);
            var installer100 = Path.Combine(edgeRoot, "installers", channel, "1.0.0");
            Directory.CreateDirectory(installer100);
            WriteFile(Path.Combine(installer100, "installer-artifact.json"), "{}");
            WriteFile(Path.Combine(velopackRoot, "releases.stable.json"), "not json");

            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(component);
            var deletionStore = new InMemoryClientReleaseComponentDeletionStore();
            var auditTrail = new RecordingAuditTrailService();
            var firstHandler = CreateHardDeleteHandler(
                edgeRoot,
                componentRepository,
                deletionStore,
                auditTrail);
            var first = await firstHandler.Handle(
                new HardDeleteClientReleaseComponentCommand(component.Id),
                CancellationToken.None);
            Assert.False(first.IsSuccess);
            var operation = Assert.Single(deletionStore.Items);
            Assert.Equal(ClientReleaseComponentDeletionStatus.Failed, operation.Status);
            Assert.Equal(1, operation.RetryCount);

            // 管理员修好 manifest 后按操作 ID 重试（全新 repository/handler）。
            WriteFile(
                Path.Combine(velopackRoot, "releases.stable.json"),
                """
                {"packages":["IIoT.EdgeClient-1.0.0-full.nupkg"]}
                """);
            var retry = await CreateRetryHandler(
                    deletionStore,
                    CreateDeletionProcessor(
                        edgeRoot,
                        new InMemoryRepository<ClientReleaseComponent>(),
                        deletionStore),
                    auditTrail)
                .Handle(
                    new RetryClientReleaseComponentDeletionCommand(operation.Id),
                    CancellationToken.None);

            Assert.True(retry.IsSuccess);
            Assert.True(retry.Value!.Succeeded);
            Assert.Empty(deletionStore.Items);
            // 组件没有存活版本引用该 nupkg，manifest 按白名单重建后不再保留它。
            Assert.DoesNotContain(
                "IIoT.EdgeClient-1.0.0-full.nupkg",
                File.ReadAllText(Path.Combine(velopackRoot, "releases.stable.json")));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task RetryClientReleaseComponentDeletionHandler_ShouldReturnNotFound_WhenOperationCompleted()
    {
        var deletionStore = new InMemoryClientReleaseComponentDeletionStore();
        var auditTrail = new RecordingAuditTrailService();
        var handler = CreateRetryHandler(
            deletionStore,
            CreateDeletionProcessor(
                CreateTempDirectory("iiot-retry-not-found-root"),
                new InMemoryRepository<ClientReleaseComponent>(),
                deletionStore),
            auditTrail);

        var result = await handler.Handle(
            new RetryClientReleaseComponentDeletionCommand(Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public void ClientReleaseComponentRelativePaths_ShouldRejectReparsePoint_WhenCollectingPluginDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var edgeRoot = CreateTempDirectory("iiot-collect-reparse-root");
        try
        {
            var outside = Path.Combine(edgeRoot, "..", $"iiot-collect-outside-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outside);
            WriteFile(Path.Combine(outside, "payload.txt"), "outside");
            var moduleDirectory = Path.Combine(edgeRoot, "plugins", "stable", "DieCutting");
            Directory.CreateDirectory(moduleDirectory);
            File.CreateSymbolicLink(Path.Combine(moduleDirectory, "linked"), outside);

            var component = CreatePluginComponent(
                "DieCutting",
                "模切",
                "stable",
                "1.0.0",
                "1.0.0",
                "1.0.0",
                "2.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/plugins/stable/DieCutting/1.0.0/die-cutting.zip",
                new string('a', 64),
                1024,
                "含 symlink",
                ClientReleaseStatus.Published);

            Assert.Throws<InvalidOperationException>(() =>
                ClientReleaseComponentRelativePaths.Collect(edgeRoot, component));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            var outsideRoot = Path.Combine(edgeRoot, "..");
            foreach (var directory in Directory.EnumerateDirectories(outsideRoot, "iiot-collect-outside-*"))
            {
                TryDeleteDirectory(directory);
            }
        }
    }

    [Fact]
    public async Task GetClientReleaseCatalogHandler_ShouldHideVersionsWhoseFilesWereRemoved()
    {
        var edgeRoot = CreateTempDirectory("iiot-catalog-missing-files-root");
        try
        {
            var channel = "stable";
            var installerDirectory = Path.Combine(edgeRoot, "installers", channel, "1.0.0");
            Directory.CreateDirectory(installerDirectory);
            WriteFile(Path.Combine(installerDirectory, "installer-artifact.json"), "{}");

            var component = ClientReleaseComponent.CreateHost(channel, "win-x64");
            component.UpsertHostVersion(
                "1.0.0",
                "1.0.0",
                "net10.0",
                "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
                new string('a', 64),
                100,
                "deleted files",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                artifacts:
                [
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.InstallerDirectory,
                        "installers/stable/1.0.0"),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.ManifestFile,
                        "installers/stable/1.0.0/installer-artifact.json",
                        new string('a', 64),
                        100)
                ]);
            component.UpsertHostVersion(
                "1.1.0",
                "1.0.0",
                "net10.0",
                "/edge-updates/installers/stable/1.1.0/installer-artifact.json",
                new string('b', 64),
                200,
                "still on disk",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                artifacts:
                [
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.InstallerDirectory,
                        "installers/stable/1.1.0"),
                    new ClientReleaseArtifact(
                        ClientReleaseArtifactKind.ManifestFile,
                        "installers/stable/1.1.0/installer-artifact.json",
                        new string('b', 64),
                        200)
                ]);

            var installer110 = Path.Combine(edgeRoot, "installers", channel, "1.1.0");
            Directory.CreateDirectory(installer110);
            WriteFile(Path.Combine(installer110, "installer-artifact.json"), "{}");

            // 模拟硬删除已删文件但数据库还残留 1.0.0 元数据。
            Directory.Delete(installerDirectory, recursive: true);

            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(component);
            var handler = new GetClientReleaseCatalogHandler(
                componentRepository,
                new InMemoryClientReleaseComponentDeletionStore(),
                Options.Create(new EdgeInstallerArtifactOptions { RootPath = Path.Combine(edgeRoot, "installers") }));

            var result = await handler.Handle(
                new GetClientReleaseCatalogQuery(channel, "win-x64"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            var host = result.Value!.Host;
            var entry = Assert.Single(host.Versions);
            Assert.Equal("1.1.0", entry.Version);
            Assert.True(entry.FilesPresent);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task GetEdgeClientReleaseCatalogHandler_ShouldHideVersionsWhoseFilesWereRemoved()
    {
        var edgeRoot = CreateTempDirectory("iiot-edge-catalog-missing-files-root");
        try
        {
            var channel = "stable";
            var component = CreateHostComponent(
                channel,
                "1.0.0",
                "1.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
                new string('a', 64),
                100,
                "deleted files",
                ClientReleaseStatus.Published);

            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(component);
            var deviceId = Guid.NewGuid();
            var handler = new GetEdgeClientReleaseCatalogHandler(
                new StubDeviceIdentityQueryService(new DeviceIdentitySnapshot(deviceId, "DEV-001")),
                componentRepository,
                new FixedRetentionPolicyReader(),
                new InMemoryClientReleaseComponentDeletionStore(),
                Options.Create(new EdgeInstallerArtifactOptions { RootPath = Path.Combine(edgeRoot, "installers") }));

            var result = await handler.Handle(
                new GetEdgeClientReleaseCatalogQuery(deviceId, channel, "win-x64"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value!.Host.Versions);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task GetClientReleaseCatalogHandler_ShouldHideArtifactsCoveredByPendingDeletion()
    {
        var edgeRoot = CreateTempDirectory("iiot-catalog-pending-deletion-root");
        try
        {
            var channel = "stable";
            var component = CreateHostComponent(
                channel,
                "1.0.0",
                "1.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/installers/stable/1.0.0/installer-artifact.json",
                new string('a', 64),
                100,
                "pending deletion",
                ClientReleaseStatus.Published);
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(component);
            var deletionStore = new InMemoryClientReleaseComponentDeletionStore();
            deletionStore.Add(new ClientReleaseComponentDeletion(
                Guid.NewGuid(),
                "Host",
                ClientReleaseComponent.HostComponentKey,
                channel,
                "win-x64",
                ["1.0.0"],
                "待清理",
                ["installers/stable/1.0.0/installer-artifact.json"]));
            var handler = new GetClientReleaseCatalogHandler(
                componentRepository,
                deletionStore,
                Options.Create(new EdgeInstallerArtifactOptions { RootPath = Path.Combine(edgeRoot, "installers") }));

            var result = await handler.Handle(
                new GetClientReleaseCatalogQuery(channel, "win-x64"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value!.Host.Versions);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
        }
    }

    [Fact]
    public async Task GetPublicClientDownloadsHandler_ShouldNotExposeDirectoryArtifactsWithoutReleaseRows()
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
                new InMemoryRepository<ClientReleaseComponent>(),
                new FixedRetentionPolicyReader());

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
            var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
            componentRepository.Items.Add(CreateHostComponent(
                "ci",
                "0.0.189-ci",
                "1.0.0",
                "win-x64",
                "net10.0",
                "/edge-updates/installers/ci/0.0.189-ci/installer-artifact.json",
                new string('a', 64),
                4096,
                null,
                ClientReleaseStatus.Archived));

            componentRepository.Items.Add(CreatePluginComponent(
                "Homogenization",
                "匀浆",
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
                ClientReleaseStatus.Archived));

            var handler = new GetPublicClientDownloadsHandler(
                componentRepository,
                new FixedRetentionPolicyReader());

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

    private static ClientReleaseComponent CreateHostComponent(
        string channel,
        string version,
        string hostApiVersion,
        string targetRuntime,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        ClientReleaseStatus status)
    {
        var component = ClientReleaseComponent.CreateHost(channel, targetRuntime);
        component.UpsertHostVersion(
            version,
            hostApiVersion,
            targetFramework,
            downloadUrl,
            sha256,
            packageSize,
            releaseNotes,
            status,
            null,
            "IIoT",
            artifacts:
            [
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.InstallerDirectory,
                    $"installers/{channel}/{version}"),
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.ManifestFile,
                    $"installers/{channel}/{version}/installer-artifact.json",
                    sha256,
                    packageSize)
            ]);
        return component;
    }

    private static ClientReleaseComponent CreatePluginComponent(
        string moduleId,
        string displayName,
        string channel,
        string version,
        string hostApiVersion,
        string minHostVersion,
        string maxHostVersion,
        string targetRuntime,
        string? targetFramework,
        string downloadUrl,
        string sha256,
        long packageSize,
        string? releaseNotes,
        ClientReleaseStatus status)
    {
        var component = ClientReleaseComponent.CreatePlugin(
            moduleId,
            displayName,
            null,
            null,
            null,
            channel,
            targetRuntime);
        component.UpsertPluginVersion(
            version,
            hostApiVersion,
            minHostVersion,
            maxHostVersion,
            targetFramework,
            downloadUrl,
            sha256,
            packageSize,
            releaseNotes,
            "[]",
            status,
            null,
            "IIoT",
            artifacts:
            [
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PluginPackageDirectory,
                    $"plugins/{channel}/{moduleId}/{version}"),
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PackageFile,
                    $"plugins/{channel}/{moduleId}/{version}/{Path.GetFileName(downloadUrl)}",
                    sha256,
                    packageSize)
            ]);
        return component;
    }

    private static ClientReleaseVersion SingleVersion(ClientReleaseComponent component)
    {
        return Assert.Single(component.Versions);
    }

    private static void SetUpdatedAtUtc(ClientReleaseComponent component, DateTime value)
    {
        var property = typeof(ClientReleaseComponent).GetProperty(nameof(ClientReleaseComponent.UpdatedAtUtc))
                       ?? throw new InvalidOperationException("ClientReleaseComponent.UpdatedAtUtc is missing.");
        property.SetValue(component, value);
    }

    private static ClientReleaseComponent SingleComponent(
        InMemoryRepository<ClientReleaseComponent> repository,
        ClientReleaseComponentKind kind,
        string key)
    {
        return Assert.Single(repository.Items, component =>
            component.ComponentKind == kind
            && string.Equals(component.ComponentKey, key, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertUploadSessionCleaned(
        string edgeRoot,
        string stagingDirectoryName)
    {
        var stagingKindRoot = Path.Combine(edgeRoot, ".staging", stagingDirectoryName);
        if (Directory.Exists(stagingKindRoot))
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(stagingKindRoot));
        }
    }

    private static void AssertSanitizedPublishUnavailableAudit(
        RecordingAuditTrailService auditTrail,
        string sensitiveFailure)
    {
        var failureAudit = Assert.Single(auditTrail.Entries, entry => !entry.Succeeded);
        Assert.Equal(ClientReleasePublishUnavailableException.PublicMessage, failureAudit.FailureReason);
        Assert.DoesNotContain(sensitiveFailure, failureAudit.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveFailure, failureAudit.FailureReason!, StringComparison.Ordinal);
    }

    private static async Task AssertPublishUnavailableAsync(
        (PublishEdgeReleaseBundleHandler Handler, ClientReleaseUploadTestSource Source) publisher,
        string bundlePath,
        RecordingAuditTrailService auditTrail,
        string sensitiveFailure)
    {
        await Assert.ThrowsAsync<ClientReleasePublishUnavailableException>(
            () => PublishBundleAsync(publisher, bundlePath));
        AssertSanitizedPublishUnavailableAudit(auditTrail, sensitiveFailure);
    }

    private static (PublishEdgeReleaseBundleHandler Handler, ClientReleaseUploadTestSource Source)
        CreatePublishHandler(
        string edgeRoot,
        InMemoryRepository<ClientReleaseComponent> componentRepository,
        IClientReleaseRetentionService retentionService,
        RecordingAuditTrailService auditTrail,
        IClientReleaseVersionObservationReader? observationReader = null)
    {
        var source = new ClientReleaseUploadTestSource();
        var handler = new PublishEdgeReleaseBundleHandler(
            ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, source),
            componentRepository,
            observationReader ?? new NotObservedReleaseReader(),
            retentionService,
            new TestCurrentUser(),
            auditTrail,
            NullLogger<PublishEdgeReleaseBundleHandler>.Instance);
        return (handler, source);
    }

    private static (PublishEdgePluginPackageHandler Handler, ClientReleaseUploadTestSource Source)
        CreatePluginPackageHandler(
        string edgeRoot,
        InMemoryRepository<ClientReleaseComponent> componentRepository,
        IClientReleaseRetentionService retentionService,
        RecordingAuditTrailService auditTrail,
        IClientReleaseVersionObservationReader? observationReader = null)
    {
        var source = new ClientReleaseUploadTestSource();
        var handler = new PublishEdgePluginPackageHandler(
            ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, source),
            componentRepository,
            observationReader ?? new NotObservedReleaseReader(),
            retentionService,
            new TestCurrentUser(),
            auditTrail,
            NullLogger<PublishEdgePluginPackageHandler>.Instance);
        return (handler, source);
    }

    private static async Task<IIoT.SharedKernel.Result.Result<EdgeReleaseBundlePublishResultDto>> PublishBundleAsync(
        (PublishEdgeReleaseBundleHandler Handler, ClientReleaseUploadTestSource Source) publisher,
        string bundlePath)
    {
        publisher.Source.LoadFile(bundlePath);
        return await publisher.Handler.Handle(
            new PublishEdgeReleaseBundleCommand(),
            CancellationToken.None);
    }

    private static async Task<IIoT.SharedKernel.Result.Result<EdgePluginPackagePublishResultDto>> PublishPluginPackageAsync(
        (PublishEdgePluginPackageHandler Handler, ClientReleaseUploadTestSource Source) publisher,
        string wrapperPath,
        CancellationToken cancellationToken = default)
    {
        publisher.Source.LoadFile(wrapperPath);
        return await publisher.Handler.Handle(
            new PublishEdgePluginPackageCommand(),
            cancellationToken);
    }

    private static EdgeReleaseBundleFixture CreateEdgeReleaseBundle(
        string version,
        Action<string>? mutateInstallerRoot = null,
        Action<string>? mutateVelopackRoot = null,
        Action<JsonObject>? mutateManifest = null)
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
        WriteFile(Path.Combine(velopackRoot, "RELEASES-stable"), $"hash IIoT.EdgeClient-{version}-full.nupkg 1024");
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
            installerStubSha256 = ClientReleaseFileFacts.ComputeSha256(setupPath),
            installerStubSize = new FileInfo(setupPath).Length,
            launcherDirectory = "launcher",
            hostDirectory = "host",
            hostDirectorySha256 = ClientReleaseFileFacts.ComputeDirectorySha256(hostRoot),
            hostDirectorySize = ClientReleaseFileFacts.GetDirectorySize(hostRoot),
            pluginsRoot = "plugins",
            velopackSetupFile = "velopack/IIoT.Edge.Setup.exe",
            velopackSetupSha256 = ClientReleaseFileFacts.ComputeSha256(velopackSetupPath),
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
                    pluginSha256 = ClientReleaseFileFacts.ComputeDirectorySha256(pluginRoot),
                    pluginSize = ClientReleaseFileFacts.GetDirectorySize(pluginRoot)
                }
            }
        };
        var manifestJson = JsonSerializer.SerializeToNode(
            manifest,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        mutateManifest?.Invoke(manifestJson);
        File.WriteAllText(
            Path.Combine(installerRoot, "installer-artifact.json"),
            manifestJson.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            new UTF8Encoding(false));

        var zipPath = Path.Combine(workingRoot, "bundle.zip");
        ZipFile.CreateFromDirectory(bundleRoot, zipPath);
        return new EdgeReleaseBundleFixture(workingRoot, zipPath);
    }

    private static EdgeReleaseBundleFixture CreatePluginReleaseWrapper(
        string moduleId,
        string version,
        Action<string>? mutatePackageRoot = null,
        bool uppercaseSha256 = false,
        string? createdAtUtcText = null)
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
        var packageSha256 = ClientReleaseFileFacts.ComputeSha256(packagePath);
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
            sha256 = uppercaseSha256 ? packageSha256.ToUpperInvariant() : packageSha256,
            signature = "",
            publisher = "IIoT",
            releaseNotes = "独立插件更新",
            createdAtUtc = createdAtUtcText ?? DateTime.UtcNow.ToString("O")
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

    private static void AssertGatewayReadableDirectory(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        Assert.True((mode & UnixFileMode.OtherRead) != 0, $"{path} must be readable by nginx gateway user.");
        Assert.True((mode & UnixFileMode.OtherExecute) != 0, $"{path} must be traversable by nginx gateway user.");
        Assert.False((mode & UnixFileMode.OtherWrite) != 0, $"{path} must not be world-writable.");
    }

    private static void AssertGatewayReadableFile(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        Assert.True((mode & UnixFileMode.OtherRead) != 0, $"{path} must be readable by nginx gateway user.");
        Assert.False((mode & UnixFileMode.OtherWrite) != 0, $"{path} must not be world-writable.");
    }

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

    private sealed class NotObservedReleaseReader : IClientReleaseVersionObservationReader
    {
        public Task<IReadOnlyList<ClientReleaseVersionObservation>> ObserveAsync(
            IReadOnlyCollection<ClientReleaseVersionIdentity> identities,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ClientReleaseVersionObservation>>([]);
        }
    }

    private sealed class RepositoryBackedReleaseReader(
        InMemoryRepository<ClientReleaseComponent> repository,
        Func<ClientReleaseVersionObservation, ClientReleaseVersionObservation>? mutate = null,
        Action? beforeRead = null)
        : IClientReleaseVersionObservationReader
    {
        public int Calls { get; private set; }

        public IReadOnlyList<ClientReleaseVersionIdentity> LastIdentities { get; private set; } = [];

        public Task<IReadOnlyList<ClientReleaseVersionObservation>> ObserveAsync(
            IReadOnlyCollection<ClientReleaseVersionIdentity> identities,
            CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            LastIdentities = identities.ToArray();
            beforeRead?.Invoke();
            var observations = new List<ClientReleaseVersionObservation>();
            foreach (var identity in identities)
            {
                var component = repository.Items.SingleOrDefault(item =>
                    item.ComponentKind == identity.ComponentKind
                    && item.ComponentKey == identity.ComponentKey
                    && item.Channel == identity.Channel
                    && item.TargetRuntime == identity.TargetRuntime);
                var version = component?.Versions.SingleOrDefault(item => item.Version == identity.Version);
                if (component is null || version is null)
                {
                    continue;
                }

                var observation = new ClientReleaseVersionObservation(
                    component.Id,
                    component.ComponentKind,
                    component.ComponentKey,
                    component.DisplayName,
                    component.Description,
                    component.IconKind,
                    component.AccentColor,
                    component.Channel,
                    component.TargetRuntime,
                    version.Id,
                    version.Version,
                    version.HostApiVersion,
                    version.MinHostVersion,
                    version.MaxHostVersion,
                    version.TargetFramework,
                    version.DownloadUrl,
                    version.Sha256,
                    version.PackageSize,
                    version.ReleaseNotes,
                    version.DependenciesJson,
                    version.Status,
                    version.Signature,
                    version.Publisher,
                    version.PublishedAtUtc,
                    version.DeletedAtUtc,
                    version.DeletionReason,
                    version.DeletionFailure,
                    version.Artifacts
                        .Select(artifact => new ClientReleaseArtifactObservation(
                            artifact.ArtifactKind,
                            artifact.RelativePath,
                            artifact.Sha256,
                            artifact.Size))
                        .ToList());
                observations.Add(mutate is null ? observation : mutate(observation));
            }

            return Task.FromResult<IReadOnlyList<ClientReleaseVersionObservation>>(observations);
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public string? Id { get; init; } = Guid.NewGuid().ToString();

        public string? UserName { get; init; } = "tester";

        public IReadOnlyCollection<string> Roles { get; init; } = ["Administrator"];

        public string? ActorType { get; init; }

        public IReadOnlyCollection<string> Permissions { get; init; } = [];

        public Guid? DeviceId => null;

        public bool IsAuthenticated => true;
    }

    private sealed class StubCurrentUserDeviceAccessService : ICurrentUserDeviceAccessService
    {
        public bool IsAdministrator => AccessibleDeviceIds is null;

        public IReadOnlyList<Guid>? AccessibleDeviceIds { get; init; }

        public Task<Result<IReadOnlyList<Guid>?>> GetAccessibleDeviceIdsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success(AccessibleDeviceIds));
        }

        public Task<Result> EnsureCanAccessDeviceAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Success());
        }
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

    private sealed class InMemoryDeviceClientStateStore : IDeviceClientStateStore
    {
        public List<DeviceClientVersionSnapshot> VersionSnapshots { get; } = [];

        public List<EdgeDeviceRuntimeHeartbeat> RuntimeHeartbeats { get; } = [];

        public List<DeviceClientState> States { get; } = [];

        public int SaveChangesCalls { get; private set; }

        public Task<DeviceClientVersionSnapshot?> GetVersionSnapshotByDeviceAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(VersionSnapshots.SingleOrDefault(snapshot => snapshot.DeviceId == deviceId));
        }

        public Task<IReadOnlyList<DeviceClientVersionSnapshot>> GetVersionSnapshotsByDevicesAsync(
            IReadOnlyCollection<Guid>? deviceIds = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<DeviceClientVersionSnapshot>>(
                VersionSnapshots
                    .Where(snapshot => deviceIds == null || deviceIds.Contains(snapshot.DeviceId))
                    .OrderBy(snapshot => snapshot.ClientCode)
                    .ToList());
        }

        public Task<EdgeDeviceRuntimeHeartbeat?> GetRuntimeHeartbeatByIdentityAsync(
            Guid deviceId,
            string clientCode,
            CancellationToken cancellationToken = default)
        {
            var normalizedClientCode = clientCode.Trim().ToUpperInvariant();
            return Task.FromResult(RuntimeHeartbeats.SingleOrDefault(heartbeat =>
                heartbeat.DeviceId == deviceId && heartbeat.ClientCode == normalizedClientCode));
        }

        public Task<DeviceClientState?> GetStateByIdentityAsync(
            Guid deviceId,
            string clientCode,
            CancellationToken cancellationToken = default)
        {
            var normalizedClientCode = clientCode.Trim().ToUpperInvariant();
            return Task.FromResult(States.SingleOrDefault(state =>
                state.DeviceId == deviceId && state.ClientCode == normalizedClientCode));
        }

        public Task<IReadOnlyList<DeviceClientState>> GetStatesByDevicesAsync(
            IReadOnlyCollection<Guid>? deviceIds = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<DeviceClientState>>(
                States
                    .Where(state => deviceIds == null || deviceIds.Contains(state.DeviceId))
                    .OrderBy(state => state.ClientCode)
                    .ToList());
        }

        public void AddVersionSnapshot(DeviceClientVersionSnapshot snapshot)
        {
            VersionSnapshots.Add(snapshot);
        }

        public void AddRuntimeHeartbeat(EdgeDeviceRuntimeHeartbeat heartbeat)
        {
            RuntimeHeartbeats.Add(heartbeat);
        }

        public void AddState(DeviceClientState state)
        {
            States.Add(state);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalls++;
            return Task.FromResult(1);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class InMemoryKeyedDistributedLockService : IDistributedLockService
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        public List<string> AcquiredResources { get; } = [];

        public async Task<IDistributedLockLease> AcquireAsync(
            string resource,
            TimeSpan? acquireTimeout = null,
            CancellationToken cancellationToken = default)
        {
            var semaphore = _locks.GetOrAdd(resource, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync(cancellationToken);
            lock (AcquiredResources)
                AcquiredResources.Add(resource);
            return new AsyncLockHandle(semaphore);
        }

        private sealed class AsyncLockHandle(SemaphoreSlim semaphore) : IDistributedLockLease
        {
            public CancellationToken OwnershipLost => CancellationToken.None;

            public ValueTask DisposeAsync()
            {
                semaphore.Release();
                return ValueTask.CompletedTask;
            }
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

    private static HardDeleteClientReleaseComponentHandler CreateHardDeleteHandler(
        string edgeRoot,
        InMemoryRepository<ClientReleaseComponent> componentRepository,
        InMemoryClientReleaseComponentDeletionStore deletionStore,
        IAuditTrailService auditTrail,
        IDeviceClientStateStore? clientStateStore = null)
    {
        return new HardDeleteClientReleaseComponentHandler(
            Options.Create(new EdgeInstallerArtifactOptions { RootPath = Path.Combine(edgeRoot, "installers") }),
            componentRepository,
            clientStateStore ?? new InMemoryDeviceClientStateStore(),
            deletionStore,
            CreateDeletionProcessor(edgeRoot, componentRepository, deletionStore),
            new TestCurrentUser(),
            auditTrail,
            NullLogger<HardDeleteClientReleaseComponentHandler>.Instance);
    }

    private static ClientReleaseComponentDeletionProcessor CreateDeletionProcessor(
        string edgeRoot,
        InMemoryRepository<ClientReleaseComponent> componentRepository,
        InMemoryClientReleaseComponentDeletionStore deletionStore)
    {
        return new ClientReleaseComponentDeletionProcessor(
            Options.Create(new EdgeInstallerArtifactOptions { RootPath = Path.Combine(edgeRoot, "installers") }),
            componentRepository,
            deletionStore,
            NullLogger<ClientReleaseComponentDeletionProcessor>.Instance);
    }

    private static RetryClientReleaseComponentDeletionHandler CreateRetryHandler(
        InMemoryClientReleaseComponentDeletionStore deletionStore,
        IClientReleaseComponentDeletionProcessor deletionProcessor,
        IAuditTrailService auditTrail)
    {
        return new RetryClientReleaseComponentDeletionHandler(
            deletionStore,
            deletionProcessor,
            new TestCurrentUser(),
            auditTrail);
    }

    /// <summary>
    /// 包装真实 processor，在首次被调用（数据库事务已提交之后）触发回调，
    /// 用于模拟“提交完成、文件清理开始前进程取消/中断”的现场。
    /// </summary>
    private sealed class CancellationAfterCommitProcessor(
        IClientReleaseComponentDeletionProcessor inner,
        Action onFirstInvocation) : IClientReleaseComponentDeletionProcessor
    {
        public Task<ClientReleaseComponentDeletionOutcome> ProcessAsync(
            ClientReleaseComponentDeletion deletion,
            CancellationToken cancellationToken)
        {
            onFirstInvocation();
            return inner.ProcessAsync(deletion, cancellationToken);
        }
    }

    private sealed class InMemoryClientReleaseComponentDeletionStore : IClientReleaseComponentDeletionStore
    {
        public List<ClientReleaseComponentDeletion> Items { get; } = [];

        public List<ClientReleaseComponentDeletion> Added { get; } = [];

        public Func<CancellationToken, Task<int>>? SaveChangesAsyncOverride { get; init; }

        public Task<ClientReleaseComponentDeletion?> GetByIdAsync(
            Guid deletionId,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Items.SingleOrDefault(deletion => deletion.Id == deletionId));

        public Task<IReadOnlyList<ClientReleaseComponentDeletion>> GetPendingAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ClientReleaseComponentDeletion>>(Items.ToList());

        public Task<IReadOnlyList<ClientReleaseComponentDeletion>> GetByChannelAsync(
            string channel,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ClientReleaseComponentDeletion>>(
                Items.Where(deletion => string.Equals(
                        deletion.Channel,
                        channel,
                        StringComparison.OrdinalIgnoreCase))
                    .ToList());

        public void Add(ClientReleaseComponentDeletion deletion)
        {
            Added.Add(deletion);
            Items.Add(deletion);
        }

        public void Remove(ClientReleaseComponentDeletion deletion)
        {
            Items.Remove(deletion);
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => SaveChangesAsyncOverride is not null
                ? SaveChangesAsyncOverride(cancellationToken)
                : Task.FromResult(1);
    }

    private sealed class InMemoryRepository<T> : IRepository<T>
        where T : class, IEntity, IAggregateRoot
    {
        public List<T> Items { get; } = [];
        public T? AddedEntity { get; private set; }

        public Func<CancellationToken, Task<int>>? SaveChangesAsyncOverride { get; init; }

        public Func<Expression<Func<T, bool>>, CancellationToken, Task<bool>>? AnyAsyncPredicateOverride { get; init; }

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
            if (SaveChangesAsyncOverride is not null)
            {
                return SaveChangesAsyncOverride(cancellationToken);
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
            if (AnyAsyncPredicateOverride is not null)
            {
                return AnyAsyncPredicateOverride(predicate, cancellationToken);
            }

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
