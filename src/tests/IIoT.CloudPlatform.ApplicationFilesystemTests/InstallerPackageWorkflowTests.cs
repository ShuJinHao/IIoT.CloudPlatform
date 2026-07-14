using AutoMapper;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.Core.MasterData.Aggregates.MfgProcesses;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
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
    private const string VelopackSetupFixtureFile = "velopack/IIoT.EdgeClient.Homogenization-stable-Setup.exe";
    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldFailBeforeRotatingSecret_WhenBaseUrlMissing()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("匀浆线1#", "DEV-AAAAAAAAAA", Guid.NewGuid());
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
                [new EdgeBindingSelection("Homogenization", device.Id)],
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
        var device = new Device("匀浆线1#", "DEV-AAAAAAAAAA", Guid.NewGuid());
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
                    [new EdgeBindingSelection("Homogenization", device.Id)],
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
        var device = new Device("匀浆线1#", "DEV-AAAAAAAAAA", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var componentRepository = CreatePublishedReleaseComponentRepository();
        var artifactRoot = CreateInstallerArtifactFixture(
            "stable",
            "1.2.0",
            includeVelopackSetupFile: false);

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                artifactRoot,
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection("Homogenization", device.Id)],
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
            if (Directory.Exists(artifactRoot))
            {
                Directory.Delete(artifactRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldFailBeforeRotatingSecret_WhenVelopackSetupFileDoesNotExist()
    {
        var oldSecret = BootstrapSecretGenerator.Generate();
        var device = new Device("匀浆线1#", "DEV-AAAAAAAAAA", Guid.NewGuid());
        device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(oldSecret));
        var oldHash = device.BootstrapSecretHash;
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var componentRepository = CreatePublishedReleaseComponentRepository();
        var artifactRoot = CreateInstallerArtifactFixture(
            "stable",
            "1.2.0",
            writeVelopackSetupFile: false);

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                artifactRoot,
                new RecordingAuditTrailService());

            var result = await handler.Handle(
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection("Homogenization", device.Id)],
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
            if (Directory.Exists(artifactRoot))
            {
                Directory.Delete(artifactRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GenerateEdgeInstallerPackageHandler_ShouldPackageSelectedRuntimeAndInjectJsonConfigs()
    {
        const string targetRuntime = "win-arm64";
        var device = new Device("匀浆线1#", "DEV-AAAAAAAAAA", Guid.NewGuid());
        var deviceRepository = new InMemoryRepository<Device>();
        deviceRepository.Add(device);
        var componentRepository = CreatePublishedReleaseComponentRepository(targetRuntime);
        var auditTrail = new RecordingAuditTrailService();
        var artifactRoot = CreateInstallerArtifactFixture("stable", "1.2.0", targetRuntime);

        try
        {
            var handler = CreateInstallerPackageHandler(
                deviceRepository,
                componentRepository,
                artifactRoot,
                auditTrail);

            var result = await handler.Handle(
                new GenerateEdgeInstallerPackageCommand(
                    [new EdgeBindingSelection("Homogenization", device.Id)],
                    TargetRuntime: targetRuntime,
                    HostVersion: "1.2.0",
                    BaseUrl: "http://cloud.local/"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            var package = result.Value!;
            Assert.EndsWith(".exe", package.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("application/vnd.microsoft.portable-executable", package.ContentType);
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
            Assert.NotNull(archive.GetEntry("plugins/Homogenization/plugin.json"));
            Assert.NotNull(archive.GetEntry("plugins/Homogenization/iiot-plugin-binding.json"));
            Assert.Null(archive.GetEntry("plugins/Welding/plugin.json"));
            Assert.Null(archive.GetEntry("plugins/Welding/iiot-plugin-binding.json"));

            var bindingJson = ReadZipEntryText(archive, "launcher/iiot-binding.json");
            using var binding = JsonDocument.Parse(bindingJson);
            var bindingItem = binding.RootElement.GetProperty("bindings")[0];
            var bootstrapSecret = bindingItem.GetProperty("bootstrapSecret").GetString();
            Assert.Equal("http://cloud.local", binding.RootElement.GetProperty("baseUrl").GetString());
            Assert.Equal("Homogenization", bindingItem.GetProperty("moduleId").GetString());
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
            Assert.Equal("Homogenization", hostPlugin.GetProperty("moduleId").GetString());
            Assert.Equal("Homogenization", hostPlugin.GetProperty("pluginDirectory").GetString());
            Assert.Equal(device.Code, hostPlugin.GetProperty("clientCode").GetString());

            var pluginBindingJson = ReadZipEntryText(archive, "plugins/Homogenization/iiot-plugin-binding.json");
            using var pluginBinding = JsonDocument.Parse(pluginBindingJson);
            Assert.Equal("Homogenization", pluginBinding.RootElement.GetProperty("moduleId").GetString());
            Assert.Equal(device.Code, pluginBinding.RootElement.GetProperty("clientCode").GetString());
            Assert.Equal(bootstrapSecret, pluginBinding.RootElement.GetProperty("bootstrapSecret").GetString());

            Assert.DoesNotContain(auditTrail.Entries, entry =>
                entry.Summary.Contains(bootstrapSecret!, StringComparison.Ordinal)
                || (entry.FailureReason?.Contains(bootstrapSecret!, StringComparison.Ordinal) ?? false));
        }
        finally
        {
            if (Directory.Exists(artifactRoot))
            {
                Directory.Delete(artifactRoot, recursive: true);
            }
        }
    }
    private static GenerateEdgeInstallerPackageHandler CreateInstallerPackageHandler(
        InMemoryRepository<Device> deviceRepository,
        InMemoryRepository<ClientReleaseComponent> componentRepository,
        string artifactRoot,
        RecordingAuditTrailService auditTrail)
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
            Options.Create(new EdgeInstallerArtifactOptions { RootPath = artifactRoot }));
    }

    private static InMemoryRepository<ClientReleaseComponent> CreatePublishedReleaseComponentRepository(
        string targetRuntime = "win-x64")
    {
        var repository = new InMemoryRepository<ClientReleaseComponent>();
        repository.ListResult.Add(CreatePublishedHostComponent(targetRuntime));
        repository.ListResult.Add(CreatePublishedPluginComponent(targetRuntime));
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

    private static ClientReleaseComponent CreatePublishedPluginComponent(string targetRuntime = "win-x64")
    {
        var component = ClientReleaseComponent.CreatePlugin(
            "Homogenization",
            "匀浆",
            "匀浆工序",
            null,
            null,
            "stable",
            targetRuntime);
        component.UpsertPluginVersion(
            "2.3.4",
            "1.0.0",
            "1.0.0",
            "9.9.9",
            "net10.0",
            "/edge-updates/installers/stable/1.2.0/installer-artifact.json#moduleId=Homogenization",
            new string('b', 64),
            512,
            null,
            "[]",
            ClientReleaseStatus.Published,
            null,
            "IIoT",
            artifacts:
            [
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PluginPackageDirectory,
                    "plugins/stable/Homogenization/2.3.4"),
                new ClientReleaseArtifact(
                    ClientReleaseArtifactKind.PackageFile,
                    "plugins/stable/Homogenization/2.3.4/Homogenization.zip",
                    new string('b', 64),
                    512)
            ]);
        return component;
    }

    private static string CreateInstallerArtifactFixture(
        string channel,
        string version,
        string targetRuntime = "win-x64",
        bool includeVelopackSetupFile = true,
        bool writeVelopackSetupFile = true)
    {
        var root = Path.Combine(Path.GetTempPath(), $"iiot-installer-artifact-{Guid.NewGuid():N}");
        var artifactDirectory = Path.Combine(root, channel, version);
        Directory.CreateDirectory(artifactDirectory);

        File.WriteAllBytes(Path.Combine(artifactDirectory, "IIoT.Edge.Setup.exe"), "MZ-STUB"u8.ToArray());
        WriteFixtureFile(artifactDirectory, "launcher/IIoT.Edge.Launcher.dll", "launcher");
        WriteFixtureFile(artifactDirectory, "launcher/launcher.profiles.json", "{}");
        WriteFixtureFile(artifactDirectory, "host/IIoT.Edge.Shell.dll", "shell");
        WriteFixtureFile(artifactDirectory, "plugins/Homogenization/plugin.json", "{}");
        WriteFixtureFile(artifactDirectory, "plugins/Welding/plugin.json", "{}");
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
          "modules": [
            {
              "moduleId": "Homogenization",
              "displayName": "匀浆",
              "version": "2.3.4",
              "hostApiVersion": "1.0.0",
              "minHostVersion": "1.0.0",
              "maxHostVersion": "9.9.9",
              "pluginDirectory": "Homogenization"
            },
            {
              "moduleId": "Welding",
              "displayName": "焊接",
              "version": "1.0.0",
              "hostApiVersion": "1.0.0",
              "minHostVersion": "1.0.0",
              "maxHostVersion": "9.9.9",
              "pluginDirectory": "Welding"
            }
          ]
        }
        """;
        File.WriteAllText(Path.Combine(artifactDirectory, "installer-artifact.json"), manifest, Encoding.UTF8);
        return root;
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
