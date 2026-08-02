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
using IIoT.ProductionService.Security;
using IIoT.ProductionService.Validators;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.CrossCutting.Exceptions;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.Persistence;
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

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldFailBeforeRotatingSecret_WhenBaseUrlMissing()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("正极模切客户端", "DEV-AAAAAAAAAA", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);

        var handler = CreateInstallerPackageHandler(
            deviceRepository,
            new InMemoryRepository<ClientReleaseComponent>(),
            Path.Combine(Path.GetTempPath(), $"iiot-baseurl-guard-{Guid.NewGuid():N}"),
            new RecordingAuditTrailService());

        var result = await handler.Handle(
            new GenerateEdgeInstallerPackageCommand(
                [new EdgeBindingSelection(PrimaryModuleId, device.Id)],
                HostVersion: "1.2.0"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, error => error.Contains("云端地址必须填写", StringComparison.Ordinal));
        Assert.Equal(oldHash, device.BootstrapSecretHash);
        Assert.True(BootstrapSecretHasher.Verify(oldSecret, device.BootstrapSecretHash!));
        Assert.Empty(deviceRepository.UpdatedEntities);
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
        var componentRepository = new InMemoryRepository<ClientReleaseComponent>();
        componentRepository.ListResult.Add(CreatePublishedHostComponent());

        var artifactRoot = Path.Combine(Path.GetTempPath(), $"iiot-missing-installer-{Guid.NewGuid():N}");
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                artifactRoot,
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection(PrimaryModuleId, device.Id)],
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local"),
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
            if (Directory.Exists(artifactRoot))
            {
                Directory.Delete(artifactRoot, recursive: true);
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
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection(PrimaryModuleId, device.Id)],
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local"),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Errors);
            Assert.Contains(result.Errors, error => error.Contains("安装素材未包含 Velopack Setup", StringComparison.Ordinal));
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
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection(PrimaryModuleId, device.Id)],
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local"),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Errors);
            Assert.Contains(result.Errors, error => error.Contains("安装素材缺少 Velopack Setup 文件", StringComparison.Ordinal));
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
    public async Task GenerateEdgeInstallerPackageHandler_ShouldPackageSelectedRuntimeAndInjectJsonConfigs()
    {
        const string targetRuntime = "win-arm64";
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
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection(PrimaryModuleId, device.Id)],
                    TargetRuntime: targetRuntime,
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local/"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
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
            Assert.NotNull(archive.GetEntry("host/IIoT.Edge.Shell.dll"));
            Assert.NotNull(archive.GetEntry(VelopackSetupFixtureFile));
            Assert.NotNull(archive.GetEntry("plugins/CP/plugin.json"));
            Assert.NotNull(archive.GetEntry("plugins/CP/IIoT.Edge.Module.CP.dll"));
            Assert.Null(archive.GetEntry("plugins/CP/iiot-plugin-binding.json"));
            Assert.Null(archive.GetEntry("plugins/AP/plugin.json"));
            Assert.Null(archive.GetEntry("plugins/AP/iiot-plugin-binding.json"));

            var bindingJson = ReadZipEntryText(archive, "launcher/iiot-binding.json");
            using var binding = JsonDocument.Parse(bindingJson);
            var bindingItem = binding.RootElement.GetProperty("bindings")[0];
            var bootstrapSecret = bindingItem.GetProperty("bootstrapSecret").GetString();
            Assert.Equal("http://cloud.local", binding.RootElement.GetProperty("baseUrl").GetString());
            Assert.Equal(PrimaryModuleId, bindingItem.GetProperty("moduleId").GetString());
            Assert.Equal(device.Code, bindingItem.GetProperty("clientCode").GetString());
            Assert.False(string.IsNullOrWhiteSpace(bootstrapSecret));
            Assert.True(BootstrapSecretHasher.Verify(bootstrapSecret!, device.BootstrapSecretHash!));

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
            Assert.Equal(PrimaryModuleId, hostPlugin.GetProperty("pluginDirectory").GetString());
            Assert.Equal(device.Code, hostPlugin.GetProperty("clientCode").GetString());

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
                    new GenerateEdgeInstallerPackageCommand(
                        [new EdgeBindingSelection(PrimaryModuleId, device.Id)],
                        HostVersion: "1.2.0",
                        BaseUrl: "http://cloud.local"),
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
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection(PrimaryModuleId, device.Id)],
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
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
                ReadZipEntryText(archive, "plugins/CP/plugin.json"));
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
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection(PrimaryModuleId, device.Id)],
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local"),
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

            var result = await handler.Handle(
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection(PrimaryModuleId, device.Id)],
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local"),
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Contains(
                result.Errors!,
                error => error.Contains("没有与宿主 1.2.0 兼容的已发布版本", StringComparison.Ordinal));
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
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection(PrimaryModuleId, device.Id)],
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local"),
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
        WritePluginPackage(
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

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection(PrimaryModuleId, device.Id)],
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local"),
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
    public async Task GenerateEdgeInstallerPackageHandler_TransientReplay_ShouldReuseExactSecretTarget()
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
        string? firstTargetHash = null;
        var unitOfWork = new InstallerFaultingUnitOfWork
        {
            RetryFirstAfterOperationFailure = true,
            AfterOperationAsync = (attempt, _) =>
            {
                if (attempt == 1)
                {
                    firstTargetHash = device.BootstrapSecretHash;
                    device.SetBootstrapSecretHash(oldHash!);
                    throw new TimeoutException(
                        "simulated transient before commit");
                }

                return Task.CompletedTask;
            }
        };
        var auditTrail = new RecordingAuditTrailService();
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                auditTrail,
                unitOfWork);

            var result = await handler.Handle(
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection(
                        PrimaryModuleId,
                        device.Id)],
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, unitOfWork.Attempts);
            Assert.Equal(2, deviceRepository.SaveChangesCalls);
            Assert.NotEqual(oldHash, device.BootstrapSecretHash);
            Assert.Equal(firstTargetHash, device.BootstrapSecretHash);
            await using var package = result.Value!.Content;
            var secret = await ReadInstallerBootstrapSecretAsync(package);
            Assert.True(BootstrapSecretHasher.Verify(
                secret,
                device.BootstrapSecretHash!));
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
    public async Task GenerateEdgeInstallerPackageHandler_PostCommitFailure_ShouldRecoverExactSecretTarget()
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
        var unitOfWork = new InstallerFaultingUnitOfWork
        {
            AfterOperationAsync = (_, _) =>
                throw new TimeoutException(
                    "simulated commit confirmation loss")
        };
        var auditTrail = new RecordingAuditTrailService();
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                auditTrail,
                unitOfWork);

            var result = await handler.Handle(
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection(
                        PrimaryModuleId,
                        device.Id)],
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, unitOfWork.Attempts);
            Assert.NotEqual(oldHash, device.BootstrapSecretHash);
            await using var package = result.Value!.Content;
            var secret = await ReadInstallerBootstrapSecretAsync(package);
            Assert.True(BootstrapSecretHasher.Verify(
                secret,
                device.BootstrapSecretHash!));
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
    public async Task GenerateEdgeInstallerPackageHandler_ConcurrentSecretDriftAfterFailure_ShouldConflict()
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
        var unitOfWork = new InstallerFaultingUnitOfWork
        {
            AfterOperationAsync = (_, _) =>
            {
                device.SetBootstrapSecretHash(concurrentHash);
                throw new TimeoutException(
                    "simulated failure after concurrent secret rotation");
            }
        };
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService(),
                unitOfWork);

            await Assert.ThrowsAsync<CloudWriteConflictException>(
                () => handler.Handle(
                    new GenerateEdgeInstallerPackageCommand(
                        [new EdgeBindingSelection(
                            PrimaryModuleId,
                            device.Id)],
                        HostVersion: "1.2.0",
                        BaseUrl: "http://cloud.local"),
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
        var unitOfWork = new InstallerFaultingUnitOfWork
        {
            BeforeOperationAsync = (_, _) =>
                throw new TimeoutException(
                    "simulated failure before persistence")
        };
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService(),
                unitOfWork);

            await Assert.ThrowsAsync<CloudWriteCommitUnknownException>(
                () => handler.Handle(
                    new GenerateEdgeInstallerPackageCommand(
                        [new EdgeBindingSelection(
                            PrimaryModuleId,
                            device.Id)],
                        HostVersion: "1.2.0",
                        BaseUrl: "http://cloud.local"),
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
        var unitOfWork = new InstallerFaultingUnitOfWork
        {
            BeforeOperationAsync = (_, _) =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            }
        };
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                new RecordingAuditTrailService(),
                unitOfWork);

            var exception =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => handler.Handle(
                        new GenerateEdgeInstallerPackageCommand(
                            [new EdgeBindingSelection(
                                PrimaryModuleId,
                                device.Id)],
                            HostVersion: "1.2.0",
                            BaseUrl: "http://cloud.local"),
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
        var preexistingPackages = Directory
            .EnumerateFiles(
                Path.GetTempPath(),
                "iiot-edge-installer-*.exe",
                SearchOption.TopDirectoryOnly)
            .ToHashSet(StringComparer.Ordinal);
        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                GetInstallerRoot(edgeRoot),
                auditTrail);

            var handling = handler.Handle(
                new GenerateEdgeInstallerPackageCommand(
                    [
                        new EdgeBindingSelection(
                            PrimaryModuleId,
                            firstDevice.Id),
                        new EdgeBindingSelection(
                            SecondaryModuleId,
                            secondDevice.Id)
                    ],
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local"),
                cancellation.Token);

            await auditTrail.FirstAuditEntered.WaitAsync(
                TimeSpan.FromSeconds(5));
            var packagePath = Assert.Single(
                Directory
                    .EnumerateFiles(
                        Path.GetTempPath(),
                        "iiot-edge-installer-*.exe",
                        SearchOption.TopDirectoryOnly),
                path => !preexistingPackages.Contains(path));

            cancellation.Cancel();
            auditTrail.Release();
            var exception =
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => handling);

            Assert.Equal(cancellation.Token, exception.CancellationToken);
            Assert.NotNull(firstDevice.BootstrapSecretHash);
            Assert.NotNull(secondDevice.BootstrapSecretHash);
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
            Assert.False(File.Exists(packagePath));
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
                    new GenerateEdgeInstallerPackageCommand(
                        [new EdgeBindingSelection(
                            PrimaryModuleId,
                            device.Id)],
                        HostVersion: "1.2.0",
                        BaseUrl: "http://cloud.local"),
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

    private static GenerateEdgeInstallerPackageHandler CreateInstallerPackageHandler(
        InMemoryRepository<Device> deviceRepository,
        InMemoryRepository<ClientReleaseComponent> componentRepository,
        string artifactRoot,
        IAuditTrailService auditTrail,
        IUnitOfWork? unitOfWork = null,
        IClientReleaseWriteObservationReader? observationReader = null,
        IEdgeInstallerGenerationStore? installerGenerationStore = null)
    {
        return new GenerateEdgeInstallerPackageHandler(
            new TestCurrentUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "admin-001",
                Roles = [SystemRoles.Admin],
                IsAuthenticated = true
            },
            new StubCurrentUserDeviceAccessService { IsAdministrator = true },
            deviceRepository,
            componentRepository,
            auditTrail,
            Options.Create(new EdgeInstallerArtifactOptions
            {
                RootPath = artifactRoot
            }),
            unitOfWork ?? new RecordingUnitOfWork(),
            observationReader
            ?? new InstallerClientReleaseWriteObservationReader(
                deviceRepository),
            installerGenerationStore
            ?? new InMemoryEdgeInstallerGenerationStore());
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

    private sealed class InstallerFaultingUnitOfWork : IUnitOfWork
    {
        public int Attempts { get; private set; }

        public bool RetryFirstAfterOperationFailure { get; init; }

        public Func<int, CancellationToken, Task>? BeforeOperationAsync
        {
            get;
            init;
        }

        public Func<int, CancellationToken, Task>? AfterOperationAsync
        {
            get;
            init;
        }

        public async Task<TResult> ExecuteResilientAsync<TResult>(
            Func<CancellationToken, Task<TResult>> operation,
            CancellationToken cancellationToken = default)
        {
            while (true)
            {
                Attempts += 1;
                if (BeforeOperationAsync is not null)
                {
                    await BeforeOperationAsync(
                        Attempts,
                        cancellationToken);
                }

                var result = await operation(cancellationToken);
                try
                {
                    if (AfterOperationAsync is not null)
                    {
                        await AfterOperationAsync(
                            Attempts,
                            cancellationToken);
                    }

                    return result;
                }
                catch when (
                    RetryFirstAfterOperationFailure
                    && Attempts == 1)
                {
                }
            }
        }

        public Task BeginTransactionAsync(
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CommitAsync(
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RollbackAsync(
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class InstallerClientReleaseWriteObservationReader(
        InMemoryRepository<Device> deviceRepository)
        : IClientReleaseWriteObservationReader
    {
        public Exception? ExceptionToThrow { get; init; }

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
            return Task.FromResult(result);
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
        AddPublishedPluginVersion(
            component,
            edgeRoot,
            moduleId,
            version,
            hostApiVersion,
            minHostVersion,
            maxHostVersion,
            targetRuntime);
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
        WritePluginPackage(
            packagePath,
            moduleId,
            version,
            hostApiVersion,
            minHostVersion,
            maxHostVersion);
        var packageBytes = File.ReadAllBytes(packagePath);
        var sha256 = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();

        component.UpsertPluginVersion(
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
        return packagePath;
    }

    private static string CreateInstallerArtifactFixture(
        string channel,
        string version,
        string targetRuntime = "win-x64",
        bool includeVelopackSetupFile = true,
        bool writeVelopackSetupFile = true)
    {
        var edgeRoot = Path.Combine(Path.GetTempPath(), $"iiot-edge-updates-{Guid.NewGuid():N}");
        var installerRoot = GetInstallerRoot(edgeRoot);
        var artifactDirectory = Path.Combine(installerRoot, channel, version);
        Directory.CreateDirectory(artifactDirectory);

        File.WriteAllBytes(Path.Combine(artifactDirectory, "IIoT.Edge.Setup.exe"), "MZ-STUB"u8.ToArray());
        WriteFixtureFile(artifactDirectory, "launcher/IIoT.Edge.Launcher.dll", "launcher");
        WriteFixtureFile(artifactDirectory, "launcher/launcher.profiles.json", "{}");
        WriteFixtureFile(artifactDirectory, "host/IIoT.Edge.Shell.dll", "shell");
        Directory.CreateDirectory(Path.Combine(artifactDirectory, "plugins"));
        if (writeVelopackSetupFile)
        {
            WriteFixtureFile(artifactDirectory, VelopackSetupFixtureFile, "velopack setup");
        }

        var velopackSetupManifestProperty = includeVelopackSetupFile
            ? $"  \"velopackSetupFile\": \"{VelopackSetupFixtureFile}\","
            : string.Empty;

        var manifest = $$"""
        {
          "schemaVersion": 2,
          "channel": "{{channel}}",
          "version": "{{version}}",
          "hostApiVersion": "1.0.0",
          "targetRuntime": "{{targetRuntime}}",
          "targetFramework": "net10.0",
          "installerStubFile": "IIoT.Edge.Setup.exe",
          "launcherDirectory": "launcher",
          "hostDirectory": "host",
          "pluginsRoot": "plugins",
        {{velopackSetupManifestProperty}}
          "modules": []
        }
        """;
        File.WriteAllText(Path.Combine(artifactDirectory, "installer-artifact.json"), manifest, Encoding.UTF8);
        return edgeRoot;
    }

    private static string GetInstallerRoot(string edgeRoot)
        => Path.Combine(edgeRoot, "installers");

    private static void WritePluginPackage(
        string packagePath,
        string moduleId,
        string version,
        string hostApiVersion,
        string minHostVersion,
        string maxHostVersion,
        string? unsafeEntryPath = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry("plugin.json");
        using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
        {
            writer.Write(JsonSerializer.Serialize(new
            {
                moduleId,
                version,
                hostApiVersion,
                minHostVersion,
                maxHostVersion,
                entryAssembly = $"IIoT.Edge.Module.{moduleId}.dll"
            }));
        }

        var assemblyEntry = archive.CreateEntry($"IIoT.Edge.Module.{moduleId}.dll");
        using (var assemblyWriter = new StreamWriter(assemblyEntry.Open(), new UTF8Encoding(false)))
        {
            assemblyWriter.Write($"{moduleId}-{version}");
        }

        if (!string.IsNullOrWhiteSpace(unsafeEntryPath))
        {
            var unsafeEntry = archive.CreateEntry(unsafeEntryPath);
            using var unsafeWriter = new StreamWriter(unsafeEntry.Open(), new UTF8Encoding(false));
            unsafeWriter.Write("unsafe");
        }
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
                   .GetProperty("bootstrapSecret")
                   .GetString()
               ?? throw new InvalidDataException(
                   "Generated installer binding is missing bootstrapSecret.");
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
