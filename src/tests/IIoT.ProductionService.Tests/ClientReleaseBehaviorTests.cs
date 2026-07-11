using System.Linq.Expressions;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

        Assert.Equal(1000, options.MaxUploadMbps);
        Assert.Equal(EdgeReleaseUploadOptions.DefaultMaxBundleBytes, options.MaxBundleBytes);
        Assert.Equal(".staging", options.StagingDirectoryName);
    }

    [Fact]
    public async Task ClientReleaseComponent_ShouldNotTouchHostComponent_WhenAppendingNewVersion()
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
        var updatedAt = component.UpdatedAtUtc;

        await Task.Delay(20);
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
    public async Task ClientReleaseComponent_ShouldTouchHostComponent_WhenUpdatingExistingVersion()
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
        var updatedAt = component.UpdatedAtUtc;

        await Task.Delay(20);
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
    public async Task ClientReleaseComponent_ShouldTouchPluginComponentOnlyForMetadataOrExistingVersionChanges()
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
        var updatedAt = component.UpdatedAtUtc;

        await Task.Delay(20);
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

        await Task.Delay(20);
        component.UpdatePluginMetadata("匀浆插件", null, null, null);
        var metadataUpdatedAt = component.UpdatedAtUtc;

        Assert.True(metadataUpdatedAt > updatedAt);

        await Task.Delay(20);
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

        Assert.True(component.UpdatedAtUtc > metadataUpdatedAt);
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
            [typeof(DeleteClientReleasePackageCommand)] = ClientReleasePermissions.Manage,
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

            var result = await PublishPluginPackageAsync(handler, wrapper.ZipPath);

            Assert.False(result.IsSuccess);
            Assert.Contains(result.Errors ?? [], error => error.Contains("插件版本已存在", StringComparison.Ordinal));
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
    public async Task PublishEdgeReleaseBundleHandler_ShouldRollbackFilesAndHideCatalog_WhenDatabaseCommitFails()
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
                SaveChangesException = new InvalidOperationException(sensitiveFailure)
            };
            var auditTrail = new RecordingAuditTrailService();
            var handler = CreatePublishHandler(edgeRoot, componentRepository, new NoopRetentionService(), auditTrail);

            var result = await PublishBundleAsync(handler, bundle.ZipPath);

            Assert.False(result.IsSuccess);
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", "1.2.1")));
            Assert.Equal("old-manifest", File.ReadAllText(Path.Combine(edgeRoot, "velopack", "stable", "releases.stable.json")));
            Assert.Equal("old-assets", File.ReadAllText(Path.Combine(edgeRoot, "velopack", "stable", "assets.stable.json")));
            Assert.False(File.Exists(Path.Combine(edgeRoot, "velopack", "stable", "IIoT.EdgeClient-1.2.1-full.nupkg")));
            Assert.Equal("Edge 发布包发布失败。", Assert.Single(result.Errors ?? []));
            Assert.DoesNotContain(sensitiveFailure, string.Join(';', result.Errors ?? []), StringComparison.Ordinal);
            var failureAudit = Assert.Single(auditTrail.Entries, entry => !entry.Succeeded);
            Assert.Equal("Edge 发布包发布失败。", failureAudit.FailureReason);
            Assert.DoesNotContain(sensitiveFailure, failureAudit.Summary, StringComparison.Ordinal);
            Assert.DoesNotContain(sensitiveFailure, failureAudit.FailureReason!, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgeReleaseBundleHandler_ShouldNotExposeUnknownInvalidDataDetails()
    {
        const string sensitiveFailure = "/private/release/SECRET-invalid-data";
        var edgeRoot = CreateTempDirectory("iiot-edge-upload-root");
        var bundle = CreateEdgeReleaseBundle("1.2.7");
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesException = new InvalidDataException(sensitiveFailure)
            };
            var auditTrail = new RecordingAuditTrailService();
            var publisher = CreatePublishHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                auditTrail);

            var result = await PublishBundleAsync(publisher, bundle.ZipPath);

            Assert.False(result.IsSuccess);
            Assert.Equal("Edge 发布包格式无效。", Assert.Single(result.Errors ?? []));
            var failureAudit = Assert.Single(auditTrail.Entries, entry => !entry.Succeeded);
            Assert.Equal("Edge 发布包格式无效。", failureAudit.FailureReason);
            Assert.DoesNotContain(sensitiveFailure, failureAudit.FailureReason!, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(edgeRoot, "installers", "stable", "1.2.7")));
        }
        finally
        {
            TryDeleteDirectory(edgeRoot);
            bundle.Dispose();
        }
    }

    [Fact]
    public async Task PublishEdgePluginPackageHandler_ShouldNotExposeIoFailureDetails()
    {
        const string sensitiveFailure = "/private/release/SECRET-plugin-io";
        var edgeRoot = CreateTempDirectory("iiot-edge-plugin-upload-root");
        var wrapper = CreatePluginReleaseWrapper("Homogenization", "1.1.3");
        try
        {
            var repository = new InMemoryRepository<ClientReleaseComponent>
            {
                SaveChangesException = new IOException(sensitiveFailure)
            };
            var auditTrail = new RecordingAuditTrailService();
            var publisher = CreatePluginPackageHandler(
                edgeRoot,
                repository,
                new NoopRetentionService(),
                auditTrail);

            var result = await PublishPluginPackageAsync(publisher, wrapper.ZipPath);

            Assert.False(result.IsSuccess);
            Assert.Equal(
                "Edge 插件发布包处理失败，请检查上传包和发布目录后重试。",
                Assert.Single(result.Errors ?? []));
            var failureAudit = Assert.Single(auditTrail.Entries, entry => !entry.Succeeded);
            Assert.Equal(Assert.Single(result.Errors ?? []), failureAudit.FailureReason);
            Assert.DoesNotContain(sensitiveFailure, failureAudit.FailureReason!, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(
                edgeRoot,
                "plugins",
                "stable",
                "Homogenization",
                "1.1.3")));
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
                auditTrail);

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

    private static (PublishEdgeReleaseBundleHandler Handler, ClientReleaseUploadTestSource Source)
        CreatePublishHandler(
        string edgeRoot,
        InMemoryRepository<ClientReleaseComponent> componentRepository,
        IClientReleaseRetentionService retentionService,
        RecordingAuditTrailService auditTrail)
    {
        var source = new ClientReleaseUploadTestSource();
        var handler = new PublishEdgeReleaseBundleHandler(
            ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, source),
            componentRepository,
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
        RecordingAuditTrailService auditTrail)
    {
        var source = new ClientReleaseUploadTestSource();
        var handler = new PublishEdgePluginPackageHandler(
            ClientReleaseUploadTestSupport.CreateCoordinator(edgeRoot, source),
            componentRepository,
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
        string wrapperPath)
    {
        publisher.Source.LoadFile(wrapperPath);
        return await publisher.Handler.Handle(
            new PublishEdgePluginPackageCommand(),
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
