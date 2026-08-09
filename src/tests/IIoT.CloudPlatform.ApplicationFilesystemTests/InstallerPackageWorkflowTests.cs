using AutoMapper;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices.Events;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Aggregates.Recipes.Events;
using IIoT.EmployeeService.Commands.Employees;
using IIoT.MasterDataService.Commands.Processes;
using IIoT.ProductionService.Commands;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Commands.DeviceLogs;
using IIoT.ProductionService.Commands.Devices;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.ProductionService.Commands.Recipes;
using IIoT.ProductionService.Caching;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.PassStations;
using IIoT.ProductionService.Profiles;
using IIoT.ProductionService.Commands.ClientReleases;
using IIoT.ProductionService.Queries.Capacities;
using IIoT.ProductionService.Queries.Devices;
using IIoT.ProductionService.Queries.DeviceLogs;
using IIoT.ProductionService.Queries.PassStations;
using IIoT.ProductionService.Queries.Recipes;
using IIoT.ProductionService.Queries.ClientReleases;
using IIoT.ProductionService.Security;
using IIoT.ProductionService.Validators;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.CrossCutting.Exceptions;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.Contracts.Events.Capacities;
using IIoT.Services.Contracts.Events.DeviceLogs;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;
using IIoT.SharedKernel.Specification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace IIoT.CloudPlatform.ApplicationFilesystemTests;

public sealed class InstallerPackageWorkflowTests
{
    private const string PrimaryModuleId = "CP";
    private const string SecondaryModuleId = "AP";
    private const string VelopackSetupFixtureFile = "velopack/IIoT.EdgeClient-stable-Setup.exe";
    private static readonly Lazy<string> PayloadSigningPrivateKeyPem =
        new(() =>
        {
            using var rsa = RSA.Create(2048);
            return rsa.ExportRSAPrivateKeyPem();
        });

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldFailBeforeRotatingSecret_WhenBaseUrlMissing()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("正极模切客户端", "DEV-AAAAAAAAAA", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);

        var edgeRoot = CreateInstallerArtifactFixture("stable", "1.2.0");
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                CreatePublishedReleaseComponentRepository(edgeRoot),
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                [device.Id],
                baseUrl: null,
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Errors);
            Assert.Contains(result.Errors, error => error.Contains("云端地址必须填写", StringComparison.Ordinal));
            Assert.Equal(oldHash, device.BootstrapSecretHash);
            Assert.True(BootstrapSecretHasher.Verify(oldSecret, device.BootstrapSecretHash!));
            Assert.Empty(deviceRepository.UpdatedEntities);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldFailBeforeRotatingSecret_WhenArtifactMissing()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("正极模切客户端", "DEV-AAAAAAAAAA", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = Path.Combine(
            Path.GetTempPath(),
            $"iiot-missing-installer-{Guid.NewGuid():N}");
        var componentRepository = CreatePublishedReleaseComponentRepository(edgeRoot);
        var artifactRoot = GetInstallerRoot(edgeRoot);
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                artifactRoot,
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                [device.Id],
                "http://cloud.local",
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Errors);
            Assert.Contains(result.Errors, error => error.Contains("安装素材不存在", StringComparison.Ordinal));
            Assert.Equal(oldHash, device.BootstrapSecretHash);
            Assert.True(BootstrapSecretHasher.Verify(oldSecret, device.BootstrapSecretHash!));
            Assert.Empty(deviceRepository.UpdatedEntities);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldRejectLegacyHostBeforeRotatingSecret()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("正极模切客户端", "DEV-AAAAAAAAAA", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture(
            "stable",
            "1.2.0",
            installerBindingSchemaVersion: 2);
        var componentRepository = CreatePublishedReleaseComponentRepository(edgeRoot);

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                [device.Id],
                "http://cloud.local",
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains(
                result.Errors ?? [],
                error => error.Contains("binding schema v3", StringComparison.Ordinal));
            Assert.Equal(oldHash, device.BootstrapSecretHash);
            Assert.True(BootstrapSecretHasher.Verify(oldSecret, device.BootstrapSecretHash!));
            Assert.Empty(deviceRepository.UpdatedEntities);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldFailBeforeRotatingSecret_WhenVelopackSetupFileMissingFromManifest()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("正极模切客户端", "DEV-AAAAAAAAAA", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture(
            "stable",
            "1.2.0",
            includeVelopackSetupFile: false);
        var componentRepository = CreatePublishedReleaseComponentRepository(edgeRoot);

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                [device.Id],
                "http://cloud.local",
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Errors);
            Assert.Contains(result.Errors, error => error.Contains("Velopack Setup 声明不完整", StringComparison.Ordinal));
            Assert.Equal(oldHash, device.BootstrapSecretHash);
            Assert.True(BootstrapSecretHasher.Verify(oldSecret, device.BootstrapSecretHash!));
            Assert.Empty(deviceRepository.UpdatedEntities);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldFailBeforeRotatingSecret_WhenVelopackSetupFileDoesNotExist()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("正极模切客户端", "DEV-AAAAAAAAAA", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture(
            "stable",
            "1.2.0",
            writeVelopackSetupFile: false);
        var componentRepository = CreatePublishedReleaseComponentRepository(edgeRoot);

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                [device.Id],
                "http://cloud.local",
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Errors);
            Assert.Contains(result.Errors, error => error.Contains("Velopack Setup 缺失或完整性已变更", StringComparison.Ordinal));
            Assert.Equal(oldHash, device.BootstrapSecretHash);
            Assert.True(BootstrapSecretHasher.Verify(oldSecret, device.BootstrapSecretHash!));
            Assert.Empty(deviceRepository.UpdatedEntities);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldPackageAutomaticallySelectedRuntimeAndInjectJsonConfigs()
    {
        const string targetRuntime = "win-x64";
        var device = new Device("正极模切客户端", "DEV-AAAAAAAAAA", Guid.NewGuid());
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var auditTrail = new RecordingAuditTrailService();
        var generationStore = new InMemoryEdgeInstallerGenerationStore();
        var edgeRoot = CreateInstallerArtifactFixture("stable", "1.2.0", targetRuntime);
        var componentRepository = CreatePublishedReleaseComponentRepository(edgeRoot, targetRuntime);

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                auditTrail,
                installerGenerationStore: generationStore);

            var result = await handler.Handle(
                [device.Id],
                "http://cloud.local/",
                CancellationToken.None);

            Assert.True(
                result.IsSuccess,
                string.Join(" | ", result.Errors ?? []));
            var package = result.Value!;
            Assert.EndsWith(".exe", package.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("application/vnd.microsoft.portable-executable", package.ContentType);
            Assert.NotEqual(Guid.Empty, package.GenerationId);
            await using var packageContent = package.Content;
            using var packageBuffer = new MemoryStream();
            await packageContent.CopyToAsync(packageBuffer);
            var packageBytes = packageBuffer.ToArray();
            Assert.Equal((byte)'M', packageBytes[0]);
            Assert.Equal((byte)'Z', packageBytes[1]);

            var payload = ReadInstallerPayload(packageBytes);
            using var archive = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read);
            Assert.NotNull(archive.GetEntry("launcher/IIoT.Edge.Launcher.dll"));
            Assert.NotNull(archive.GetEntry("launcher/iiot-binding.json"));
            Assert.NotNull(archive.GetEntry("launcher/iiot-enabled-plugins.json"));
            Assert.Null(archive.GetEntry("launcher/launcher.profiles.json"));
            Assert.NotNull(archive.GetEntry("host/IIoT.Edge.Shell.dll"));
            Assert.NotNull(archive.GetEntry(VelopackSetupFixtureFile));
            Assert.NotNull(archive.GetEntry($"plugins/{device.Code}/app/plugin.json"));
            Assert.NotNull(archive.GetEntry($"plugins/{device.Code}/app/IIoT.Edge.Module.CP.dll"));
            Assert.Null(archive.GetEntry($"plugins/{device.Code}/app/iiot-plugin-binding.json"));
            Assert.DoesNotContain(
                archive.Entries,
                entry => entry.FullName.Contains("IIoT.Edge.Module.AP.dll", StringComparison.Ordinal));

            var bindingJson = ReadZipEntryText(archive, "launcher/iiot-binding.json");
            using var binding = JsonDocument.Parse(bindingJson);
            Assert.Equal(3, binding.RootElement.GetProperty("schemaVersion").GetInt32());
            var bindingPaths = binding.RootElement.GetProperty("paths");
            Assert.Equal(17, bindingPaths.EnumerateObject().Count());
            Assert.Equal(
                "/api/v1/edge/bootstrap/device-instance",
                bindingPaths.GetProperty("deviceInstance").GetString());
            Assert.Equal(
                "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                bindingPaths.GetProperty("clientReleaseCatalogTemplate").GetString());
            Assert.Equal(
                "/api/v1/edge/client-releases/version-reports",
                bindingPaths.GetProperty("clientVersionReport").GetString());
            Assert.Equal(
                "/api/v1/edge/runtime-heartbeats",
                bindingPaths.GetProperty("runtimeHeartbeat").GetString());
            Assert.Equal(
                "/api/v1/edge/pass-stations/{typeKey}/batch",
                bindingPaths.GetProperty("passStationBatchTemplate").GetString());
            Assert.Equal(
                "/api/v1/edge/edge-hosts/plc-runtime-states",
                bindingPaths.GetProperty("edgeHostPlcRuntimeStates").GetString());
            var bindingItem = binding.RootElement.GetProperty("bindings")[0];
            var bootstrapSecret = bindingItem
                .GetProperty("pendingCredential")
                .GetProperty("secret")
                .GetString();
            Assert.Equal("http://cloud.local", binding.RootElement.GetProperty("baseUrl").GetString());
            Assert.Equal(PrimaryModuleId, bindingItem.GetProperty("moduleId").GetString());
            Assert.Equal(device.Code, bindingItem.GetProperty("clientCode").GetString());
            Assert.False(string.IsNullOrWhiteSpace(bootstrapSecret));
            var pendingCredential = Assert.Single(
                generationStore.PendingCredentials);
            Assert.True(BootstrapSecretHasher.Verify(
                bootstrapSecret!,
                pendingCredential.SecretHash));
            Assert.Null(device.BootstrapSecretHash);

            var updateConfigJson = ReadZipEntryText(archive, "launcher/launcher.update.json");
            using var updateConfig = JsonDocument.Parse(updateConfigJson);
            Assert.Equal("http://cloud.local/edge-updates/velopack/stable/", updateConfig.RootElement.GetProperty("source").GetString());
            Assert.Equal("stable", updateConfig.RootElement.GetProperty("channel").GetString());
            Assert.Equal(targetRuntime, updateConfig.RootElement.GetProperty("targetRuntime").GetString());
            Assert.False(updateConfig.RootElement.TryGetProperty("Source", out _));
            Assert.False(updateConfig.RootElement.TryGetProperty("TargetRuntime", out _));

            var hostConfigJson = ReadZipEntryText(archive, "launcher/iiot-enabled-plugins.json");
            using var hostConfig = JsonDocument.Parse(hostConfigJson);
            var hostPlugin = hostConfig.RootElement.GetProperty("plugins")[0];
            Assert.Equal(PrimaryModuleId, hostPlugin.GetProperty("moduleId").GetString());
            Assert.Equal("2.3.4", hostPlugin.GetProperty("version").GetString());
            Assert.Equal(device.Code, hostPlugin.GetProperty("pluginDirectory").GetString());
            Assert.Equal(device.Code, hostPlugin.GetProperty("clientCode").GetString());
            Assert.Equal(
                bindingItem.GetProperty("packageSha256").GetString(),
                hostPlugin.GetProperty("packageSha256").GetString());

            var generationRecord = Assert.Single(generationStore.Records).Value;
            Assert.Equal(package.GenerationId, generationRecord.Id);
            Assert.Equal(packageBytes.Length, generationRecord.PackageSize);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant(),
                generationRecord.PackageSha256);
            Assert.Contains(PrimaryModuleId, generationRecord.BindingsJson, StringComparison.Ordinal);
            Assert.Contains("2.3.4", generationRecord.PluginsJson, StringComparison.Ordinal);
            Assert.DoesNotContain(bootstrapSecret!, generationRecord.BindingsJson, StringComparison.Ordinal);
            Assert.DoesNotContain(bootstrapSecret!, generationRecord.PluginsJson, StringComparison.Ordinal);

            if (!OperatingSystem.IsWindows())
            {
                var persistedPackagePath = Path.Combine(
                    GetInstallerRoot(edgeRoot),
                    "generated",
                    $"{package.GenerationId:N}.exe");
                var mode = File.GetUnixFileMode(persistedPackagePath);
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    mode);
            }

            Assert.DoesNotContain(auditTrail.Entries, entry =>
                entry.Summary.Contains(bootstrapSecret!, StringComparison.Ordinal)
                || (entry.FailureReason?.Contains(bootstrapSecret!, StringComparison.Ordinal) ?? false));
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_WhenGenerationRecordIsUnconfirmed_ShouldNotReturnPackageOrCreateSuccessRecord()
    {
        var device = new Device("正极模切客户端", "DEV-GEN-UNKNOWN", Guid.NewGuid());
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var generationStore = new InMemoryEdgeInstallerGenerationStore
        {
            ConfirmResult = false
        };
        var edgeRoot = CreateInstallerArtifactFixture("stable", "1.2.0");
        var componentRepository = CreatePublishedReleaseComponentRepository(edgeRoot);

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService(),
                installerGenerationStore: generationStore);

            await Assert.ThrowsAsync<CloudWriteCommitUnknownException>(() =>
                handler.Handle(
                    [device.Id],
                    "http://cloud.local",
                    CancellationToken.None));

            Assert.Empty(generationStore.Records);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldChooseLatestCompatiblePublishedPluginPackage()
    {
        var device = new Device("正极模切客户端", "DEV-BBBBBBBBBB", Guid.NewGuid());
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture("stable", "1.2.0");
        var plugin = CreatePublishedPluginComponent(
            edgeRoot,
            PrimaryModuleId,
            "正极模切");
        AddPublishedPluginVersion(
            plugin,
            edgeRoot,
            PrimaryModuleId,
            "3.0.0",
            "1.0.0",
            "2.0.0",
            "9.9.9",
            "win-x64");
        AddPublishedPluginVersion(
            plugin,
            edgeRoot,
            PrimaryModuleId,
            "4.0.0",
            "1.0.0",
            "1.0.0",
            "9.9.9",
            "win-x64");
        plugin.ChangeVersionStatus(
            plugin.FindVersion("4.0.0")!.Id,
            ClientReleaseStatus.Deprecated);
        var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
        componentRepository.ListResult.Add(CreatePublishedHostComponent());
        componentRepository.ListResult.Add(plugin);

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                [device.Id],
                "http://cloud.local",
                CancellationToken.None);

            Assert.True(
                result.IsSuccess,
                string.Join(" | ", result.Errors ?? []));
            await using var packageContent = result.Value!.Content;
            using var packageBuffer = new MemoryStream();
            await packageContent.CopyToAsync(packageBuffer);
            var payload = ReadInstallerPayload(packageBuffer.ToArray());
            using var archive = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read);

            using var enabledPlugins = JsonDocument.Parse(
                ReadZipEntryText(archive, "launcher/iiot-enabled-plugins.json"));
            var selected = enabledPlugins.RootElement.GetProperty("plugins")[0];
            Assert.Equal("2.3.4", selected.GetProperty("version").GetString());

            using var pluginManifest = JsonDocument.Parse(
                ReadZipEntryText(
                    archive,
                    $"plugins/{device.Code}/app/plugin.json"));
            Assert.Equal("2.3.4", pluginManifest.RootElement.GetProperty("version").GetString());
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldFailBeforeRotatingSecret_WhenPluginPackageIntegrityMismatch()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("正极模切客户端", "DEV-CCCCCCCCCC", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture("stable", "1.2.0");
        var componentRepository = CreatePublishedReleaseComponentRepository(edgeRoot);
        var packagePath = Path.Combine(
            edgeRoot,
            "plugins",
            "stable",
            PrimaryModuleId,
            "2.3.4",
            $"{PrimaryModuleId}.zip");
        File.AppendAllText(packagePath, "tampered", Encoding.UTF8);

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                [device.Id],
                "http://cloud.local",
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains(
                result.Errors!,
                error => error.Contains("安装包不存在或完整性校验失败", StringComparison.Ordinal));
            Assert.Equal(oldHash, device.BootstrapSecretHash);
            Assert.True(BootstrapSecretHasher.Verify(oldSecret, device.BootstrapSecretHash!));
            Assert.Empty(deviceRepository.UpdatedEntities);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldFailBeforeRotatingSecret_WhenNoCompatiblePluginVersionExists()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("正极模切客户端", "DEV-DDDDDDDDDD", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture("stable", "1.2.0");
        var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
        componentRepository.ListResult.Add(CreatePublishedHostComponent());
        componentRepository.ListResult.Add(CreatePublishedPluginComponent(
            edgeRoot,
            PrimaryModuleId,
            "正极模切",
            minHostVersion: "2.0.0"));

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService());

            var plan = await handler.BuildPlanAsync(
                [device.Id],
                CancellationToken.None);

            Assert.False(plan.IsSuccess);
            Assert.Contains(
                plan.Errors!,
                error => error.Contains("没有共同兼容的已批准", StringComparison.Ordinal));
            Assert.Equal(oldHash, device.BootstrapSecretHash);
            Assert.True(BootstrapSecretHasher.Verify(oldSecret, device.BootstrapSecretHash!));
            Assert.Empty(deviceRepository.UpdatedEntities);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldFailBeforeRotatingSecret_WhenPluginArtifactRegistrationIsIncomplete()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("正极模切客户端", "DEV-EEEEEEEEEE", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture("stable", "1.2.0");
        var componentRepository = CreatePublishedReleaseComponentRepository(edgeRoot);
        var plugin = componentRepository.ListResult.Single(component =>
            component.ComponentKind == ClientReleaseComponentKind.Plugin
            && component.ComponentKey == PrimaryModuleId);
        plugin.FindVersion("2.3.4")!.ReplaceArtifacts(
        [
            new ClientReleaseArtifact(
                ClientReleaseArtifactKind.PluginPackageDirectory,
                "plugins/stable/CP/2.3.4")
        ]);

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                [device.Id],
                "http://cloud.local",
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains(
                result.Errors!,
                error => error.Contains("发布文件登记不完整", StringComparison.Ordinal));
            Assert.Equal(oldHash, device.BootstrapSecretHash);
            Assert.True(BootstrapSecretHasher.Verify(oldSecret, device.BootstrapSecretHash!));
            Assert.Empty(deviceRepository.UpdatedEntities);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldFailBeforeRotatingSecret_WhenPluginZipContainsUnsafePath()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("正极模切客户端", "DEV-FFFFFFFFFF", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture("stable", "1.2.0");
        var componentRepository = CreatePublishedReleaseComponentRepository(edgeRoot);
        var plugin = componentRepository.ListResult.Single(component =>
            component.ComponentKind == ClientReleaseComponentKind.Plugin
            && component.ComponentKey == PrimaryModuleId);
        var version = plugin.FindVersion("2.3.4")!;
        var packageRelativePath = "plugins/stable/CP/2.3.4/CP.zip";
        var packagePath = Path.Combine(
            edgeRoot,
            packageRelativePath.Replace('/', Path.DirectorySeparatorChar));
        File.Delete(packagePath);
        var fileManifestSha256 = WritePluginPackage(
            packagePath,
            PrimaryModuleId,
            version.Version,
            version.HostApiVersion,
            version.MinHostVersion!,
            version.MaxHostVersion!,
            unsafeEntryPath: "../outside.dll");
        var packageBytes = File.ReadAllBytes(packagePath);
        var sha256 = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
        version.UpdatePlugin(
            version.HostApiVersion,
            version.MinHostVersion!,
            version.MaxHostVersion!,
            version.TargetFramework,
            $"/edge-updates/{packageRelativePath}",
            sha256,
            packageBytes.LongLength,
            version.ReleaseNotes,
            version.DependenciesJson,
            ClientReleaseStatus.Published,
            version.Signature,
            version.Publisher,
            artifacts:
            [
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PluginPackageDirectory,
                    "plugins/stable/CP/2.3.4"),
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PackageFile,
                    packageRelativePath,
                    sha256,
                    packageBytes.LongLength)
            ]);
        version.ConfigurePluginManifest("[]", fileManifestSha256);
        plugin.ConfigurePluginContract(
            plugin.SupportedProcessType!,
            plugin.BusinessDocumentRef,
            plugin.ManifestSchemaVersion,
            fileManifestSha256,
            plugin.DataCapabilitiesJson);

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                [device.Id],
                "http://cloud.local",
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains(
                result.Errors!,
                error => error.Contains("安装包包含非法路径", StringComparison.Ordinal));
            Assert.Equal(oldHash, device.BootstrapSecretHash);
            Assert.True(BootstrapSecretHasher.Verify(oldSecret, device.BootstrapSecretHash!));
            Assert.Empty(deviceRepository.UpdatedEntities);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldPersistExactPendingSecretWithoutRotatingActiveSecret()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device(
            "正极模切客户端",
            "DEV-REPLAY0001",
            Guid.NewGuid());
        device.SetBootstrapSecretHash(
            BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture(
            "stable",
            "1.2.0");
        var componentRepository =
            CreatePublishedReleaseComponentRepository(edgeRoot);
        var generationStore = new InMemoryEdgeInstallerGenerationStore();
        var auditTrail = new RecordingAuditTrailService();
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                auditTrail,
                installerGenerationStore: generationStore);

            var result = await handler.Handle(
                [device.Id],
                "http://cloud.local",
                CancellationToken.None);

            Assert.True(
                result.IsSuccess,
                string.Join(" | ", result.Errors ?? []));
            Assert.Equal(oldHash, device.BootstrapSecretHash);
            Assert.Equal(0, deviceRepository.SaveChangesCalls);
            await using var package = result.Value!.Content;
            var secret = await ReadInstallerBootstrapSecretAsync(package);
            var pending = Assert.Single(generationStore.PendingCredentials);
            Assert.True(BootstrapSecretHasher.Verify(
                secret,
                pending.SecretHash));
            Assert.Equal(result.Value.GenerationId, pending.GenerationId);
            Assert.Equal(device.Id, pending.DeviceId);
            Assert.Single(
                auditTrail.Entries,
                entry =>
                    entry.OperationType
                    == "Edge.GenerateInstallerPackage"
                    && entry.Succeeded);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_GenerationConfirmationFailure_ShouldKeepActiveSecretAndReturnUnknown()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device(
            "正极模切客户端",
            "DEV-COMMIT0001",
            Guid.NewGuid());
        device.SetBootstrapSecretHash(
            BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture(
            "stable",
            "1.2.0");
        var componentRepository =
            CreatePublishedReleaseComponentRepository(edgeRoot);
        var generationStore = new InMemoryEdgeInstallerGenerationStore
        {
            ConfirmResult = false
        };
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService(),
                installerGenerationStore: generationStore);

            await Assert.ThrowsAsync<CloudWriteCommitUnknownException>(() =>
                handler.Handle(
                    [device.Id],
                    "http://cloud.local",
                    CancellationToken.None));

            Assert.Equal(oldHash, device.BootstrapSecretHash);
            Assert.Empty(generationStore.Records);
            Assert.Empty(generationStore.PendingCredentials);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ConcurrentActiveSecretDriftDuringObservation_ShouldConflict()
    {
        var device = new Device(
            "正极模切客户端",
            "DEV-CONFLICT01",
            Guid.NewGuid());
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture(
            "stable",
            "1.2.0");
        var componentRepository =
            CreatePublishedReleaseComponentRepository(edgeRoot);
        var concurrentHash = BootstrapSecretHasher.Hash(
            BootstrapSecretGenerator.Generate());
        var observer = new InstallerClientReleaseWriteObservationReader(
            deviceRepository)
        {
            BeforeReturn = states =>
            {
                device.SetBootstrapSecretHash(concurrentHash);
                return states;
            }
        };
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService(),
                observationReader: observer);

            await Assert.ThrowsAsync<CloudWriteConflictException>(
                () => handler.Handle(
                    [device.Id],
                    "http://cloud.local",
                    CancellationToken.None));

            Assert.Equal(concurrentHash, device.BootstrapSecretHash);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_BaselineOnlyAfterFailure_ShouldReturnUnknown()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device(
            "正极模切客户端",
            "DEV-UNKNOWN001",
            Guid.NewGuid());
        device.SetBootstrapSecretHash(
            BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture(
            "stable",
            "1.2.0");
        var componentRepository =
            CreatePublishedReleaseComponentRepository(edgeRoot);
        var generationStore = new InMemoryEdgeInstallerGenerationStore
        {
            ConfirmResult = false
        };
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService(),
                installerGenerationStore: generationStore);

            await Assert.ThrowsAsync<CloudWriteCommitUnknownException>(
                () => handler.Handle(
                    [device.Id],
                    "http://cloud.local",
                    CancellationToken.None));

            Assert.Equal(oldHash, device.BootstrapSecretHash);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_CallbackCancellation_ShouldPropagateWithoutRotatingSecret()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device(
            "正极模切客户端",
            "DEV-CANCEL0001",
            Guid.NewGuid());
        device.SetBootstrapSecretHash(
            BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture(
            "stable",
            "1.2.0");
        var componentRepository =
            CreatePublishedReleaseComponentRepository(edgeRoot);
        using var cancellation = new CancellationTokenSource();
        var observer = new InstallerClientReleaseWriteObservationReader(
            deviceRepository)
        {
            BeforeReturn = states =>
            {
                cancellation.Cancel();
                return states;
            }
        };
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService(),
                observationReader: observer);

            var exception =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => handler.Handle(
                        [device.Id],
                        "http://cloud.local",
                        cancellation.Token));

            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.Equal(oldHash, device.BootstrapSecretHash);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_CancellationDuringRecoveredAudits_ShouldAuditEveryCommittedSecretAndDisposePackage()
    {
        var firstDevice = new Device(
            "正极模切客户端",
            "DEV-AUDIT00001",
            Guid.NewGuid());
        var secondDevice = new Device(
            "负极模切客户端",
            "DEV-AUDIT00002",
            Guid.NewGuid());
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(firstDevice);
        deviceRepository.Add(secondDevice);
        var edgeRoot = CreateInstallerArtifactFixture(
            "stable",
            "1.2.0");
        var componentRepository =
            CreatePublishedReleaseComponentRepository(edgeRoot);
        using var cancellation = new CancellationTokenSource();
        var auditTrail = new ConfirmedAuditBarrier();
        var generationStore = new InMemoryEdgeInstallerGenerationStore();
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                auditTrail,
                installerGenerationStore: generationStore);

            var handling = handler.Handle(
                [firstDevice.Id, secondDevice.Id],
                "http://cloud.local",
                cancellation.Token);

            await auditTrail.FirstAuditEntered.WaitAsync(
                TimeSpan.FromSeconds(5));

            cancellation.Cancel();
            auditTrail.Release();
            var exception =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => handling);

            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.Null(firstDevice.BootstrapSecretHash);
            Assert.Null(secondDevice.BootstrapSecretHash);
            Assert.Equal(2, auditTrail.Entries.Count);
            Assert.Equal(
                2,
                auditTrail.Entries
                    .Select(entry => entry.IdempotencyKey)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.All(
                auditTrail.Entries,
                entry => Assert.True(entry.Succeeded));
            Assert.Empty(generationStore.Records);
            Assert.Empty(generationStore.PendingCredentials);
        }
        finally
        {
            auditTrail.Release();
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ObservationFailure_ShouldReturnCommitUnknown()
    {
        var device = new Device(
            "正极模切客户端",
            "DEV-OBSERVE001",
            Guid.NewGuid());
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture(
            "stable",
            "1.2.0");
        var componentRepository =
            CreatePublishedReleaseComponentRepository(edgeRoot);
        var observer =
            new InstallerClientReleaseWriteObservationReader(
                deviceRepository)
            {
                ExceptionToThrow =
                    new IOException("simulated observation failure")
            };
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService(),
                observationReader: observer);

            await Assert.ThrowsAsync<CloudWriteCommitUnknownException>(
                () => handler.Handle(
                    [device.Id],
                    "http://cloud.local",
                    CancellationToken.None));

            Assert.Null(device.BootstrapSecretHash);
            Assert.Equal(0, deviceRepository.SaveChangesCalls);
        }
        finally
        {
            if (Directory.Exists(edgeRoot))
            {
                Directory.Delete(edgeRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EdgeInstallerPlan_ShouldRejectFormalPluginWhoseExactHostManifestDoesNotMatch()
    {
        var device = new Device(
            "P1正极模切",
            "DEV-HOST-EVIDENCE",
            Guid.NewGuid());
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var edgeRoot = CreateInstallerArtifactFixture("stable", "1.2.0");
        var componentRepository =
            CreatePublishedReleaseComponentRepository(edgeRoot);
        var host = componentRepository.ListResult.Single(component =>
            component.ComponentKind == ClientReleaseComponentKind.Host);
        var hostVersion = host.FindVersion("1.2.0")!;
        hostVersion.ConfigureHostManifest(new string('a', 64));

        var plugin = componentRepository.ListResult.Single(component =>
            component.ComponentKind == ClientReleaseComponentKind.Plugin
            && component.ComponentKey == PrimaryModuleId);
        plugin.ConfigurePluginContract(
            plugin.SupportedProcessType!,
            plugin.BusinessDocumentRef,
            manifestSchemaVersion: 3,
            plugin.FileManifestSha256,
            plugin.DataCapabilitiesJson);
        var pluginVersion = plugin.FindVersion("2.3.4")!;
        pluginVersion.ConfigurePluginManifest(
            pluginVersion.DataCapabilitiesJson,
            pluginVersion.FileManifestSha256,
            new string('b', 64),
            hostVersion.Version,
            new string('c', 64));

        try
        {
            var harness = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService());

            var plan = await harness.BuildPlanAsync([device.Id]);

            Assert.False(plan.IsSuccess);
            Assert.Contains(
                plan.Errors ?? [],
                error => error.Contains(
                    "共同兼容",
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(edgeRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingInstallerDownload_ShouldRequireAccessToEveryBoundDevice()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"iiot-generated-download-{Guid.NewGuid():N}");
        var generationId = Guid.NewGuid();
        var firstDeviceId = Guid.NewGuid();
        var secondDeviceId = Guid.NewGuid();
        var bytes = "MZ-authenticated-installer"u8.ToArray();
        var packageSha256 = Convert.ToHexString(SHA256.HashData(bytes))
            .ToLowerInvariant();
        var generatedDirectory = Path.Combine(root, "generated");
        Directory.CreateDirectory(generatedDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(generatedDirectory, $"{generationId:N}.exe"),
            bytes);

        var record = new EdgeInstallerGenerationRecord(
            generationId,
            Guid.NewGuid(),
            "admin",
            DateTime.UtcNow,
            "stable",
            "win-x64",
            "2.0.12",
            new string('a', 64),
            "IIoT.EdgeClient-bundle.exe",
            packageSha256,
            bytes.LongLength,
            [
                new EdgeInstallerGenerationBindingFact(
                    "P1",
                    firstDeviceId,
                    "DEV-P1",
                    "P1正极模切",
                    Guid.NewGuid()),
                new EdgeInstallerGenerationBindingFact(
                    "P2",
                    secondDeviceId,
                    "DEV-P2",
                    "P2正极模切",
                    Guid.NewGuid())
            ],
            [
                new EdgeInstallerGenerationPluginFact(
                    "P1",
                    "2.0.12",
                    new string('b', 64)),
                new EdgeInstallerGenerationPluginFact(
                    "P2",
                    "2.0.12",
                    new string('c', 64))
            ]);
        var store = new InMemoryEdgeInstallerGenerationStore();
        await store.TryAddConfirmedAsync(
            record,
            [
                new EdgeInstallerPendingCredential(
                    generationId,
                    firstDeviceId,
                    "DEV-P1",
                    "secret-hash-p1",
                    "P1",
                    "2.0.12",
                    new string('b', 64),
                    DateTime.UtcNow.AddDays(1)),
                new EdgeInstallerPendingCredential(
                    generationId,
                    secondDeviceId,
                    "DEV-P2",
                    "secret-hash-p2",
                    "P2",
                    "2.0.12",
                    new string('c', 64),
                    DateTime.UtcNow.AddDays(1))
            ]);

        try
        {
            var access = new StubCurrentUserDeviceAccessService
            {
                AccessibleDeviceIds = [firstDeviceId]
            };
            var handler = new GetEdgeInstallerPackageByGenerationHandler(
                store,
                access,
                Options.Create(new EdgeInstallerArtifactOptions
                {
                    RootPath = root
                }));

            var forbidden = await handler.Handle(
                new GetEdgeInstallerPackageByGenerationQuery(generationId),
                CancellationToken.None);

            Assert.False(forbidden.IsSuccess);
            Assert.Contains(
                forbidden.Errors ?? [],
                error => error.Contains("无权访问", StringComparison.Ordinal));

            access.AccessibleDeviceIds = [firstDeviceId, secondDeviceId];
            var allowed = await handler.Handle(
                new GetEdgeInstallerPackageByGenerationQuery(generationId),
                CancellationToken.None);

            Assert.True(
                allowed.IsSuccess,
                string.Join(" | ", allowed.Errors ?? []));
            await using var stream = allowed.Value!.Content;
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static InstallerPackageTestHarness CreateInstallerPackageHandler(
        InMemoryRepository<Device> deviceRepository,
        InMemoryRepository<ClientReleaseComponent> componentRepository,
        string artifactRoot,
        IAuditTrailService auditTrail,
        IClientReleaseWriteObservationReader? observationReader = null,
        IEdgeInstallerGenerationStore? installerGenerationStore = null)
    {
        var devices = deviceRepository.ListResult.Count > 0
            ? deviceRepository.ListResult
            : deviceRepository.SingleOrDefaultResult is null
                ? []
                : [deviceRepository.SingleOrDefaultResult];
        var plugins = componentRepository.ListResult
            .Where(component =>
                component.ComponentKind == ClientReleaseComponentKind.Plugin)
            .OrderBy(component => component.ComponentKey, StringComparer.Ordinal)
            .ToArray();
        var bindings = devices
            .OrderBy(device => device.Id)
            .Select(device =>
            {
                var expectedModuleId = device.DeviceName.Contains(
                    "负极",
                    StringComparison.Ordinal)
                    ? SecondaryModuleId
                    : PrimaryModuleId;
                var plugin = plugins.SingleOrDefault(component =>
                                 string.Equals(
                                     component.ComponentKey,
                                     expectedModuleId,
                                     StringComparison.Ordinal))
                             ?? plugins.Single();
                return new DevicePluginBindingReadItem(
                Guid.NewGuid(),
                device.Id,
                plugin.Id,
                plugin.ComponentKey,
                plugin.DisplayName,
                plugin.SupportedProcessType ?? plugin.ComponentKey,
                plugin.Channel,
                plugin.TargetRuntime,
                plugin.DataCapabilitiesJson);
            })
            .ToArray();
        var access = new StubCurrentUserDeviceAccessService
        {
            IsAdministrator = true
        };
        var planService = new EdgeInstallerPlanService(
            access,
            deviceRepository,
            componentRepository,
            new StubDevicePluginBindingQueryService
            {
                Bindings = bindings
            });
        var handler = new GenerateEdgeInstallerPackageHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin-001",
                Roles = [SystemRoles.Admin],
                IsAuthenticated = true
            },
            access,
            deviceRepository,
            componentRepository,
            auditTrail,
            Options.Create(new EdgeInstallerArtifactOptions
            {
                RootPath = artifactRoot,
                PayloadSigningKeyId = "installer-test-key",
                PayloadSigningPrivateKeyPem = PayloadSigningPrivateKeyPem.Value
            }),
            observationReader
            ?? new InstallerClientReleaseWriteObservationReader(
                deviceRepository),
            installerGenerationStore
            ?? new InMemoryEdgeInstallerGenerationStore(),
            planService);
        return new InstallerPackageTestHarness(handler, planService);
    }

    private sealed class InstallerPackageTestHarness(
        GenerateEdgeInstallerPackageHandler handler,
        IEdgeInstallerPlanService planService)
    {
        public Task<Result<EdgeInstallerPlanDto>> BuildPlanAsync(
            IReadOnlyList<Guid> deviceIds,
            CancellationToken cancellationToken = default)
            => planService.BuildAsync(deviceIds, cancellationToken);

        public async Task<Result<EdgeInstallerPackageDto>> Handle(
            IReadOnlyList<Guid> deviceIds,
            string? baseUrl,
            CancellationToken cancellationToken)
        {
            var plan = await planService.BuildAsync(
                deviceIds,
                cancellationToken);
            Assert.True(
                plan.IsSuccess,
                string.Join(" | ", plan.Errors ?? []));
            return await handler.Handle(
                new GenerateEdgeInstallerPackageCommand(
                    BaseUrl: baseUrl,
                    DeviceIds: deviceIds,
                    PlanFingerprint: plan.Value!.PlanFingerprint),
                cancellationToken);
        }
    }

    private sealed class ConfirmedAuditBarrier : IAuditTrailService
    {
        private readonly TaskCompletionSource firstAuditEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<AuditTrailEntry> Entries { get; } = [];

        public Task FirstAuditEntered => firstAuditEntered.Task;

        public void Release()
            => release.TrySetResult();

        public Task TryWriteAsync(
            AuditTrailEntry entry,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async Task<bool> TryWriteConfirmedAsync(
            AuditTrailEntry entry,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            if (Entries.Count == 1)
            {
                firstAuditEntered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return true;
        }
    }

    private sealed class InstallerClientReleaseWriteObservationReader(
        InMemoryRepository<Device> deviceRepository)
        : IClientReleaseWriteObservationReader
    {
        public Exception? ExceptionToThrow { get; init; }

        public Func<
            IReadOnlyList<DeviceBootstrapWriteState>,
            IReadOnlyList<DeviceBootstrapWriteState>>? BeforeReturn
        {
            get;
            init;
        }

        public Task<ClientReleaseVersionWriteState?> ObserveVersionAsync(
            Guid versionId,
            CancellationToken cancellationToken)
            => Task.FromResult<ClientReleaseVersionWriteState?>(null);

        public Task<IReadOnlyList<ClientReleaseVersionWriteState>>
            ObserveVersionsAsync(
                IReadOnlyCollection<Guid> versionIds,
                CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<
                ClientReleaseVersionWriteState>>([]);

        public Task<ClientReleaseComponentWriteState?>
            ObserveComponentAsync(
                Guid componentId,
                CancellationToken cancellationToken)
            => Task.FromResult<ClientReleaseComponentWriteState?>(null);

        public Task<ClientReleaseComponentDeletionWriteObservation>
            ObserveComponentDeletionAsync(
                Guid componentId,
                Guid deletionId,
                CancellationToken cancellationToken)
            => Task.FromResult(
                new ClientReleaseComponentDeletionWriteObservation(
                    null,
                    null));

        public Task<ClientReleaseDeletionWriteState?>
            ObserveDeletionAsync(
                Guid deletionId,
                CancellationToken cancellationToken)
            => Task.FromResult<ClientReleaseDeletionWriteState?>(null);

        public Task<ClientReleaseRetentionPolicyWriteState?>
            ObserveRetentionPolicyAsync(
                CancellationToken cancellationToken)
            => Task.FromResult<
                ClientReleaseRetentionPolicyWriteState?>(null);

        public Task<IReadOnlyList<DeviceBootstrapWriteState>>
            ObserveDeviceBootstrapAsync(
                IReadOnlyCollection<Guid> deviceIds,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            var requested = deviceIds.ToHashSet();
            IReadOnlyList<DeviceBootstrapWriteState> result =
                deviceRepository.ListResult
                    .Where(device => requested.Contains(device.Id))
                    .OrderBy(device => device.Id)
                    .Select(device => new DeviceBootstrapWriteState(
                        device.Id,
                        device.DeviceName,
                        device.Code,
                        device.ProcessId,
                        device.BootstrapSecretHash,
                        device.RowVersion))
                    .ToList();
            return Task.FromResult(
                BeforeReturn is null ? result : BeforeReturn(result));
        }
    }

    private static InMemoryRepository<ClientReleaseComponent> CreatePublishedReleaseComponentRepository(
        string edgeRoot,
        string targetRuntime = "win-x64")
    {
        var repository = new InMemoryRepository<ClientReleaseComponent>();
        repository.ListResult.Add(CreatePublishedHostComponent(targetRuntime));
        repository.ListResult.Add(CreatePublishedPluginComponent(
            edgeRoot,
            PrimaryModuleId,
            "正极模切",
            targetRuntime));
        repository.ListResult.Add(CreatePublishedPluginComponent(
            edgeRoot,
            SecondaryModuleId,
            "负极模切",
            targetRuntime,
            version: "2.1.0"));
        return repository;
    }

    private static ClientReleaseComponent CreatePublishedHostComponent(string targetRuntime = "win-x64")
    {
        var component = ClientReleaseComponent.CreateHost("stable", targetRuntime);
        component.UpsertHostVersion(
            "1.2.0",
            "1.0.0",
            "net10.0",
            "/edge-updates/installers/stable/1.2.0/installer-artifact.json",
            new string('a', 64),
            1024,
            null,
            ClientReleaseStatus.Published,
            null,
            "IIoT",
            artifacts:
            [
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.InstallerDirectory,
                    "installers/stable/1.2.0"),
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.ManifestFile,
                    "installers/stable/1.2.0/installer-artifact.json",
                    new string('a', 64),
                    1024)
            ]);
        return component;
    }

    private static ClientReleaseComponent CreatePublishedPluginComponent(
        string edgeRoot,
        string moduleId,
        string displayName,
        string targetRuntime = "win-x64",
        string version = "2.3.4",
        string hostApiVersion = "1.0.0",
        string minHostVersion = "1.0.0",
        string maxHostVersion = "9.9.9")
    {
        var component = ClientReleaseComponent.CreatePlugin(
            moduleId,
            displayName,
            $"{displayName}工序",
            null,
            null,
            "stable",
            targetRuntime);
        var fileManifestSha256 = AddPublishedPluginVersion(
            component,
            edgeRoot,
            moduleId,
            version,
            hostApiVersion,
            minHostVersion,
            maxHostVersion,
            targetRuntime);
        component.ConfigurePluginContract(
            "DIECUT",
            $"docs/plugins/{moduleId}.md",
            manifestSchemaVersion: 1,
            fileManifestSha256,
            dataCapabilitiesJson: "[]");
        return component;
    }

    private static string AddPublishedPluginVersion(
        ClientReleaseComponent component,
        string edgeRoot,
        string moduleId,
        string version,
        string hostApiVersion,
        string minHostVersion,
        string maxHostVersion,
        string targetRuntime)
    {
        var packageRelativePath = $"plugins/stable/{moduleId}/{version}/{moduleId}.zip";
        var packagePath = Path.Combine(
            edgeRoot,
            packageRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var fileManifestSha256 = WritePluginPackage(
            packagePath,
            moduleId,
            version,
            hostApiVersion,
            minHostVersion,
            maxHostVersion);
        var packageBytes = File.ReadAllBytes(packagePath);
        var sha256 = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();

        var release = component.UpsertPluginVersion(
            version,
            hostApiVersion,
            minHostVersion,
            maxHostVersion,
            "net10.0",
            $"/edge-updates/{packageRelativePath}",
            sha256,
            packageBytes.LongLength,
            null,
            "[]",
            ClientReleaseStatus.Published,
            null,
            "IIoT",
            artifacts:
            [
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PluginPackageDirectory,
                    $"plugins/stable/{moduleId}/{version}"),
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PackageFile,
                    packageRelativePath,
                    sha256,
                    packageBytes.LongLength)
            ]);
        release.ConfigurePluginManifest("[]", fileManifestSha256);
        return fileManifestSha256;
    }

    private static string CreateInstallerArtifactFixture(
        string channel,
        string version,
        string targetRuntime = "win-x64",
        bool includeVelopackSetupFile = true,
        bool writeVelopackSetupFile = true,
        int installerBindingSchemaVersion = 3)
    {
        var edgeRoot = Path.Combine(Path.GetTempPath(), $"iiot-edge-updates-{Guid.NewGuid():N}");
        var installerRoot = GetInstallerRoot(edgeRoot);
        var artifactDirectory = Path.Combine(installerRoot, channel, version);
        Directory.CreateDirectory(artifactDirectory);

        var installerStubPath = Path.Combine(
            artifactDirectory,
            "IIoT.Edge.Setup.exe");
        File.WriteAllBytes(installerStubPath, "MZ-STUB"u8.ToArray());
        WriteFixtureFile(artifactDirectory, "launcher/IIoT.Edge.Launcher.dll", "launcher");
        WriteFixtureFile(artifactDirectory, "launcher/launcher.profiles.json", "{}");
        WriteFixtureFile(artifactDirectory, "host/IIoT.Edge.Shell.dll", "shell");
        Directory.CreateDirectory(Path.Combine(artifactDirectory, "plugins"));
        if (writeVelopackSetupFile)
        {
            WriteFixtureFile(artifactDirectory, VelopackSetupFixtureFile, "velopack setup");
        }

        var launcherRoot = Path.Combine(artifactDirectory, "launcher");
        var hostRoot = Path.Combine(artifactDirectory, "host");
        var velopackSetupPath = Path.Combine(
            artifactDirectory,
            VelopackSetupFixtureFile.Replace(
                '/',
                Path.DirectorySeparatorChar));
        var installerStubBytes = File.ReadAllBytes(installerStubPath);
        var installerStubSha256 = Convert.ToHexString(
            SHA256.HashData(installerStubBytes)).ToLowerInvariant();
        var velopackSetupSha256 = writeVelopackSetupFile
            ? Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(velopackSetupPath)))
                .ToLowerInvariant()
            : new string('b', 64);
        var velopackSetupSize = writeVelopackSetupFile
            ? new FileInfo(velopackSetupPath).Length
            : 1;

        var velopackSetupManifestProperty = includeVelopackSetupFile
            ? $"  \"velopackSetupFile\": \"{VelopackSetupFixtureFile}\","
            : string.Empty;

        var manifest = $$"""
        {
          "schemaVersion": 3,
          "installerBindingSchemaVersion": {{installerBindingSchemaVersion}},
          "channel": "{{channel}}",
          "version": "{{version}}",
          "hostApiVersion": "1.0.0",
          "targetRuntime": "{{targetRuntime}}",
          "targetFramework": "net10.0",
          "installerStubFile": "IIoT.Edge.Setup.exe",
          "installerStubSha256": "{{installerStubSha256}}",
          "installerStubSize": {{installerStubBytes.LongLength}},
          "launcherDirectory": "launcher",
          "launcherDirectorySha256": "{{ComputeDirectorySha256(launcherRoot)}}",
          "launcherDirectorySize": {{GetDirectorySize(launcherRoot)}},
          "hostDirectory": "host",
          "hostDirectorySha256": "{{ComputeDirectorySha256(hostRoot)}}",
          "hostDirectorySize": {{GetDirectorySize(hostRoot)}},
          "pluginsRoot": "plugins",
        {{velopackSetupManifestProperty}}
          "velopackSetupSha256": "{{velopackSetupSha256}}",
          "velopackSetupSize": {{velopackSetupSize}},
          "modules": []
        }
        """;
        File.WriteAllText(Path.Combine(artifactDirectory, "installer-artifact.json"), manifest, Encoding.UTF8);
        return edgeRoot;
    }

    private static string GetInstallerRoot(string edgeRoot)
        => Path.Combine(edgeRoot, "installers");

    private static string ComputeDirectorySha256(string directory)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory
                     .EnumerateFiles(
                         directory,
                         "*",
                         SearchOption.AllDirectories)
                     .OrderBy(
                         path => Path.GetRelativePath(directory, path)
                             .Replace('\\', '/'),
                         StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(directory, file)
                .Replace('\\', '/');
            hasher.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hasher.AppendData([0]);
            hasher.AppendData(File.ReadAllBytes(file));
            hasher.AppendData([10]);
        }

        return Convert.ToHexString(hasher.GetHashAndReset())
            .ToLowerInvariant();
    }

    private static long GetDirectorySize(string directory)
        => Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);

    private static string WritePluginPackage(
        string packagePath,
        string moduleId,
        string version,
        string hostApiVersion,
        string minHostVersion,
        string maxHostVersion,
        string? unsafeEntryPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        var pluginJson = JsonSerializer.SerializeToUtf8Bytes(new
        {
            moduleId,
            version,
            hostApiVersion,
            minHostVersion,
            maxHostVersion,
            entryAssembly = $"IIoT.Edge.Module.{moduleId}.dll"
        });
        var assemblyBytes = Encoding.UTF8.GetBytes($"{moduleId}-{version}");
        var files = new List<(string Path, byte[] Bytes)>
        {
            ("plugin.json", pluginJson),
            ($"IIoT.Edge.Module.{moduleId}.dll", assemblyBytes)
        };
        if (!string.IsNullOrWhiteSpace(unsafeEntryPath))
        {
            files.Add((unsafeEntryPath, "unsafe"u8.ToArray()));
        }
        var fileManifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            component = moduleId,
            version,
            files = files.Select(file => new
            {
                path = file.Path,
                size = file.Bytes.LongLength,
                sha256 = Convert.ToHexString(SHA256.HashData(file.Bytes))
                    .ToLowerInvariant(),
                type = "file",
                component = moduleId,
                version
            })
        });

        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry("plugin.json");
        using (var stream = manifestEntry.Open())
        {
            stream.Write(pluginJson);
        }

        var assemblyEntry = archive.CreateEntry($"IIoT.Edge.Module.{moduleId}.dll");
        using (var stream = assemblyEntry.Open())
        {
            stream.Write(assemblyBytes);
        }

        if (!string.IsNullOrWhiteSpace(unsafeEntryPath))
        {
            var unsafeEntry = archive.CreateEntry(unsafeEntryPath);
            using var stream = unsafeEntry.Open();
            stream.Write("unsafe"u8);
        }

        var fileManifestEntry = archive.CreateEntry("file-manifest.json");
        using (var stream = fileManifestEntry.Open())
        {
            stream.Write(fileManifestBytes);
        }

        return Convert.ToHexString(SHA256.HashData(fileManifestBytes))
            .ToLowerInvariant();
    }

    private static byte[] ReadInstallerPayload(byte[] package)
    {
        Assert.True(package.Length > 16);
        Assert.Equal("IIOTEDG1"u8.ToArray(), package.AsSpan(package.Length - 8, 8).ToArray());

        var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(package.AsSpan(package.Length - 16, 8));
        Assert.InRange(payloadLength, 1, package.Length - 16);
        var payloadStart = package.Length - 16 - (int)payloadLength;
        return package.AsSpan(payloadStart, (int)payloadLength).ToArray();
    }

    private static async Task<string> ReadInstallerBootstrapSecretAsync(
        Stream package)
    {
        using var buffer = new MemoryStream();
        await package.CopyToAsync(buffer);
        var payload = ReadInstallerPayload(buffer.ToArray());
        using var archive = new ZipArchive(
            new MemoryStream(payload),
            ZipArchiveMode.Read);
        using var binding = JsonDocument.Parse(
            ReadZipEntryText(
                archive,
                "launcher/iiot-binding.json"));
        return binding.RootElement
                   .GetProperty("bindings")[0]
                   .GetProperty("pendingCredential")
                   .GetProperty("secret")
                   .GetString()
               ?? throw new InvalidDataException(
                   "Generated installer binding is missing pending credential secret.");
    }

    private static void WriteFixtureFile(string artifactDirectory, string relativePath, string content)
    {
        var path = Path.Combine(
            artifactDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static string ReadZipEntryText(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"Zip entry not found: {entryName}");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
