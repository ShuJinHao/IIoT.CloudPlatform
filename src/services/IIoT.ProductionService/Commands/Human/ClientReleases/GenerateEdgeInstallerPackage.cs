using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.Security;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AuthorizeRequirement(ClientReleasePermissions.GenerateInstaller)]
public sealed record GenerateEdgeInstallerPackageCommand(
    IReadOnlyList<EdgeBindingSelection>? Selections = null,
    string? Channel = null,
    string? TargetRuntime = null,
    string? HostVersion = null,
    string? BaseUrl = null,
    IReadOnlyList<Guid>? DeviceIds = null,
    string? PlanFingerprint = null) : IHumanCommand<Result<EdgeInstallerPackageDto>>;

public sealed record EdgeInstallerPackageDto(
    string FileName,
    string ContentType,
    Stream Content,
    Guid GenerationId);

public sealed class GenerateEdgeInstallerPackageHandler(
    ICurrentUser currentUser,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IRepository<Device> deviceRepository,
    IReadRepository<ClientReleaseComponent> componentRepository,
    IAuditTrailService auditTrailService,
    IOptions<EdgeInstallerArtifactOptions> options,
    IClientReleaseWriteObservationReader observationReader,
    IEdgeInstallerGenerationStore installerGenerationStore,
    IEdgeInstallerPlanService installerPlanService)
    : ICommandHandler<GenerateEdgeInstallerPackageCommand, Result<EdgeInstallerPackageDto>>
{
    private static readonly byte[] InstallerMagic = "IIOTEDG1"u8.ToArray();
    private const string BindingFileName = "iiot-binding.json";
    private const string HostPluginManifestFileName = "iiot-enabled-plugins.json";
    private const string LauncherProfileCatalogFileName = "launcher.profiles.json";
    private const string RemovedPluginBindingFileName = "iiot-plugin-binding.json";
    private const string UpdateConfigFileName = "launcher.update.json";
    private static readonly IComparer<string> VersionComparer = Comparer<string>.Create(ClientReleaseMapping.CompareVersions);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<Result<EdgeInstallerPackageDto>> Handle(
        GenerateEdgeInstallerPackageCommand request,
        CancellationToken cancellationToken)
    {
        if (request.DeviceIds is not { Count: > 0 }
            || request.Selections is { Count: > 0 }
            || request.Channel is not null
            || request.TargetRuntime is not null
            || request.HostVersion is not null)
        {
            return await FailAsync(
                "生成安装包失败：只能提交已确认的设备列表和计划指纹，不接受旧版插件组合或宿主参数。",
                cancellationToken);
        }

        var planResult = await installerPlanService.BuildAsync(
            request.DeviceIds,
            cancellationToken);
        if (!planResult.IsSuccess)
            return await FailAsync(
                planResult.Errors?.FirstOrDefault()
                ?? "生成安装包失败：安装计划无法确认。",
                cancellationToken);

        var approvedPlan = planResult.Value!;
        if (string.IsNullOrWhiteSpace(request.PlanFingerprint)
            || !string.Equals(
                approvedPlan.PlanFingerprint,
                request.PlanFingerprint.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return await FailAsync(
                "生成安装包失败：安装计划已变更，请刷新后重新确认。",
                cancellationToken);
        }

        var channel = approvedPlan.Channel;
        var targetRuntime = approvedPlan.TargetRuntime;
        var requestedHostVersion = approvedPlan.HostVersion;
        IReadOnlyList<EdgeBindingSelection> requestedSelections = approvedPlan.Devices
            .Select(device => new EdgeBindingSelection(
                device.ModuleId,
                device.DeviceId,
                device.PluginVersion))
            .ToArray();
        if (!EdgeInstallerPublicBaseUrl.TryNormalize(request.BaseUrl, out var publicBaseUrl, out var baseUrlError))
        {
            return await FailAsync($"生成安装包失败：{baseUrlError}", cancellationToken);
        }

        if (!TryNormalizeSelections(requestedSelections, out var selections, out var selectionError))
        {
            return await FailAsync(selectionError!, cancellationToken);
        }

        var host = await ResolveHostReleaseAsync(
            channel,
            targetRuntime,
            requestedHostVersion,
            cancellationToken);
        if (host is null)
        {
            return await FailAsync("生成安装包失败：没有找到已发布的客户端宿主版本。", cancellationToken);
        }

        var artifactResult = LoadArtifact(
            channel,
            host.Version.Version,
            host.Version.FileManifestSha256);
        if (!artifactResult.IsSuccess)
        {
            return await FailAsync(artifactResult.Error!, cancellationToken);
        }

        var artifact = artifactResult.Artifact!;
        if (!string.Equals(artifact.Channel, channel, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(artifact.Version, host.Version.Version, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(artifact.TargetRuntime, targetRuntime, StringComparison.OrdinalIgnoreCase))
        {
            return await FailAsync("生成安装包失败：发布记录与安装素材不一致。", cancellationToken);
        }

        var layoutCheck = ValidateArtifactLayout(artifact);
        if (layoutCheck is not null)
        {
            return await FailAsync(layoutCheck, cancellationToken);
        }

        var selectedPlugins = new List<EdgeInstallerPluginPackage>(selections.Count);
        foreach (var selection in selections)
        {
            var pluginResolution = await ResolvePluginReleaseAsync(
                selection.ModuleId,
                channel,
                targetRuntime,
                host.Version.Version,
                host.Version.HostApiVersion,
                host.Version.FileManifestSha256,
                selection.PluginVersion,
                cancellationToken);
            if (!pluginResolution.IsSuccess)
            {
                return await FailAsync(pluginResolution.Error!, cancellationToken);
            }

            var packageResult = LoadPluginPackage(
                pluginResolution.Selection!,
                artifact);
            if (!packageResult.IsSuccess)
            {
                return await FailAsync(packageResult.Error!, cancellationToken);
            }

            selectedPlugins.Add(packageResult.Package!);
        }

        var devices = await LoadDevicesAsync(selections, cancellationToken);
        if (!devices.IsSuccess)
        {
            return await FailAsync(devices.Error!, cancellationToken);
        }

        selectedPlugins = selectedPlugins
            .Select((plugin, index) => plugin with
            {
                PluginDirectory = devices.DevicesById![
                    selections[index].DeviceId].Code + "/app"
            })
            .ToList();

        var requestedDeviceIds = selections
            .Select(selection => selection.DeviceId)
            .OrderBy(deviceId => deviceId)
            .ToArray();
        var baselineObservation =
            await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                token => observationReader.ObserveDeviceBootstrapAsync(
                    requestedDeviceIds,
                    token),
                cancellationToken)
            ?? throw new CloudWriteCommitUnknownException();
        if (baselineObservation.Count != requestedDeviceIds.Length
            || !LoadedDevicesMatchObservation(
                devices.DevicesById!,
                baselineObservation))
        {
            throw new CloudWriteConflictException();
        }

        var generationId = Guid.NewGuid();
        var generatedAtUtc = ClientReleaseWriteCommitRecovery.NormalizeUtc(
            DateTime.UtcNow);
        var expiresAtUtc = generatedAtUtc.AddDays(7);
        var secretTargets = CreateDeviceSecretTargets(
            generationId,
            expiresAtUtc,
            selections,
            devices.DevicesById!,
            selectedPlugins);
        var bindings = secretTargets
            .Select(target => target.Binding)
            .ToList();
        var bindingBundle = new EdgeBindingBundleDto(
            EdgeBindingWireSchema.SchemaVersion,
            generationId,
            generatedAtUtc,
            expiresAtUtc,
            publicBaseUrl,
            EdgeBindingWireSchema.Paths,
            bindings);
        Stream packageStream;
        try
        {
            packageStream = BuildInstallerPackage(
                artifact,
                selectedPlugins,
                bindingBundle,
                targetRuntime,
                options.Value);
        }
        catch (ClientReleaseValidationException)
        {
            return await FailAsync("生成安装包失败：插件安装包格式无效。", cancellationToken);
        }
        catch (InvalidDataException)
        {
            return await FailAsync("生成安装包失败：安装素材包格式无效。", cancellationToken);
        }
        catch (IOException)
        {
            return await FailAsync("生成安装包失败：服务器临时空间不足或安装素材无法读取。", cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return await FailAsync("生成安装包失败：服务器没有读取安装素材或写入临时文件的权限。", cancellationToken);
        }

        try
        {
            var fileName = BuildDownloadFileName(bindings, host.Version.Version);
            var packageFact = ComputePackageFact(packageStream);
            var persistedPackagePath = await PersistReadyPackageAsync(
                packageStream,
                generationId,
                cancellationToken);
            var generationRecord = new EdgeInstallerGenerationRecord(
                generationId,
                ClientReleaseAuditActor.ParseId(currentUser.Id),
                currentUser.UserName,
                bindingBundle.GeneratedAtUtc,
                channel,
                targetRuntime,
                host.Version.Version,
                host.Version.Sha256,
                fileName,
                packageFact.Sha256,
                packageFact.Size,
                selections.Select(selection =>
                {
                    var device = devices.DevicesById![selection.DeviceId];
                    return new EdgeInstallerGenerationBindingFact(
                        selection.ModuleId,
                        device.Id,
                        device.Code,
                        device.DeviceName,
                        device.ProcessId,
                        device.Code);
                }),
                selectedPlugins.Select(plugin => new EdgeInstallerGenerationPluginFact(
                    plugin.ModuleId,
                    plugin.Version,
                    plugin.Sha256)));

            await WriteSuccessAuditAsync(
                bindings,
                devices.DevicesById!,
                bindingBundle.GeneratedAtUtc,
                cancellationToken);
            if (!await installerGenerationStore.TryAddConfirmedAsync(
                    generationRecord,
                    secretTargets.Select(target => target.PendingCredential)
                        .ToArray(),
                    cancellationToken))
            {
                TryDeleteReadyPackage(persistedPackagePath);
                throw new CloudWriteCommitUnknownException();
            }

            return Result.Success(new EdgeInstallerPackageDto(
                fileName,
                "application/vnd.microsoft.portable-executable",
                packageStream,
                generationId));
        }
        catch
        {
            await packageStream.DisposeAsync();
            throw;
        }

    }

    private async Task<string> PersistReadyPackageAsync(
        Stream packageStream,
        Guid generationId,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetFullPath(Path.Combine(
            options.Value.RootPath,
            "generated"));
        Directory.CreateDirectory(directory);
        var path = Path.GetFullPath(Path.Combine(
            directory,
            $"{generationId:N}.exe"));
        if (!path.StartsWith(
                directory + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            throw new InvalidDataException("Generated package path escaped root.");

        packageStream.Position = 0;
        await using (var target = new FileStream(
                         path,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.Read,
                         1024 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await packageStream.CopyToAsync(target, cancellationToken);
            await target.FlushAsync(cancellationToken);
        }
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        packageStream.Position = 0;
        return path;
    }

    private static void TryDeleteReadyPackage(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Orphan cleanup is handled by the installer retention job.
        }
    }

    private async Task<HostReleaseSelection?> ResolveHostReleaseAsync(
        string channel,
        string targetRuntime,
        string? requestedVersion,
        CancellationToken cancellationToken)
    {
        var component = await componentRepository.GetSingleOrDefaultAsync(
            new ClientReleaseComponentByIdentitySpec(
                ClientReleaseComponentKind.Host,
                ClientReleaseComponent.HostComponentKey,
                channel,
                targetRuntime),
            cancellationToken);
        if (component is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(requestedVersion))
        {
            var requested = requestedVersion.Trim();
            var version = component.FindVersion(requested);
            return version?.Status == ClientReleaseStatus.Published
                ? new HostReleaseSelection(component, version)
                : null;
        }

        var publishedVersion = component.Versions
            .Where(release => release.Status == ClientReleaseStatus.Published)
            .OrderByDescending(release => release.Version, VersionComparer)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .FirstOrDefault();
        return publishedVersion is null ? null : new HostReleaseSelection(component, publishedVersion);
    }

    private async Task<PluginReleaseResolution> ResolvePluginReleaseAsync(
        string moduleId,
        string channel,
        string targetRuntime,
        string hostVersion,
        string hostApiVersion,
        string? hostFileManifestSha256,
        string? requestedPluginVersion,
        CancellationToken cancellationToken)
    {
        var component = await componentRepository.GetSingleOrDefaultAsync(
            new ClientReleaseComponentByIdentitySpec(
                ClientReleaseComponentKind.Plugin,
                moduleId,
                channel,
                targetRuntime),
            cancellationToken);
        if (component is null)
        {
            return PluginReleaseResolution.Fail(
                $"生成安装包失败：插件 {moduleId} 未登记为已发布版本。");
        }

        var published = component.Versions
            .Where(release => release.Status == ClientReleaseStatus.Published)
            .OrderByDescending(release => release.Version, VersionComparer)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .ToList();
        if (published.Count == 0)
        {
            return PluginReleaseResolution.Fail(
                $"生成安装包失败：插件 {moduleId} 未登记为已发布版本。");
        }

        var candidates = string.IsNullOrWhiteSpace(requestedPluginVersion)
            ? published
            : published.Where(release => string.Equals(
                    release.Version,
                    requestedPluginVersion.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        var compatible = candidates.FirstOrDefault(release =>
            ClientReleaseMapping.IsCompatibleWithHost(
                release,
                hostVersion,
                hostApiVersion,
                out _)
            && HasExactHostEvidence(
                component,
                release,
                hostVersion,
                hostFileManifestSha256));
        return compatible is null
            ? PluginReleaseResolution.Fail(
                $"生成安装包失败：插件 {moduleId} 没有与宿主 {hostVersion} 兼容的已发布版本。")
            : PluginReleaseResolution.Success(new PluginReleaseSelection(component, compatible));
    }

    private ArtifactLoadResult LoadArtifact(
        string channel,
        string version,
        string? expectedHostFileManifestSha256)
    {
        var rootPath = options.Value.RootPath;
        var artifactRoot = Path.GetFullPath(Path.Combine(rootPath, channel, version));
        var configuredRoot = Path.GetFullPath(rootPath);
        if (!artifactRoot.StartsWith(configuredRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装素材路径非法。");
        }

        var manifestPath = Path.Combine(artifactRoot, "installer-artifact.json");
        if (!File.Exists(manifestPath))
        {
            return ArtifactLoadResult.Fail($"生成安装包失败：安装素材不存在 {channel}/{version}。");
        }

        EdgeInstallerArtifactManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<EdgeInstallerArtifactManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);
        }
        catch (JsonException)
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装素材清单无法解析。");
        }

        if (manifest is null
            || string.IsNullOrWhiteSpace(manifest.InstallerStubFile)
            || !IsSafeRelativeFile(manifest.InstallerStubFile)
            || manifest.SchemaVersion != 3
            || !IsSafeZipDirectory(manifest.LauncherDirectory)
            || !IsSafeZipDirectory(manifest.HostDirectory)
            || !IsSafeZipDirectory(manifest.PluginsRoot)
            || manifest.Modules is null)
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装素材清单不完整。");
        }

        if (manifest.InstallerBindingSchemaVersion
            != EdgeBindingWireSchema.SchemaVersion)
        {
            return ArtifactLoadResult.Fail(
                "生成安装包失败：宿主安装素材不支持 binding schema v3。");
        }

        if (manifest.Modules.Any(module =>
            module is null
            || string.IsNullOrWhiteSpace(module.ModuleId)
            || string.IsNullOrWhiteSpace(module.Version)
            || !IsSafeZipDirectory(module.PluginDirectory)))
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装素材清单包含非法插件目录映射。");
        }

        if (manifest.Modules.Select(module => module.ModuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Modules.Count
            || manifest.Modules.Select(module => NormalizeZipDirectory(module.PluginDirectory)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Modules.Count)
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装素材清单包含重复插件或插件目录。");
        }

        manifest.RootPath = artifactRoot;
        manifest.InstallerStubPath = ResolveArtifactPath(artifactRoot, manifest.InstallerStubFile);
        if (!ClientReleaseFileFacts.IsSha256(manifest.InstallerStubSha256)
            || manifest.InstallerStubSize <= 0
            || !ClientReleaseFileFacts.IsExactRegularFile(
                manifest.InstallerStubPath,
                manifest.InstallerStubSha256!,
                manifest.InstallerStubSize))
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装器外壳缺失或完整性已变更。");
        }

        var launcherDirectory = ResolveArtifactDirectoryPath(
            manifest,
            manifest.LauncherDirectory);
        if (!IsExactDirectory(
                launcherDirectory,
                manifest.LauncherDirectorySha256,
                manifest.LauncherDirectorySize))
        {
            return ArtifactLoadResult.Fail("生成安装包失败：Launcher 源产物缺失或完整性已变更。");
        }

        var hostDirectory = ResolveArtifactDirectoryPath(
            manifest,
            manifest.HostDirectory);
        if (!IsExactDirectory(
                hostDirectory,
                manifest.HostDirectorySha256,
                manifest.HostDirectorySize))
        {
            return ArtifactLoadResult.Fail("生成安装包失败：Host 源产物缺失或完整性已变更。");
        }

        var hostManifestError = ValidateHostFileManifest(
            artifactRoot,
            hostDirectory,
            manifest,
            expectedHostFileManifestSha256);
        if (hostManifestError is not null)
        {
            return ArtifactLoadResult.Fail(hostManifestError);
        }

        if (string.IsNullOrWhiteSpace(manifest.VelopackSetupFile)
            || !IsSafeRelativeFile(manifest.VelopackSetupFile))
        {
            return ArtifactLoadResult.Fail("生成安装包失败：Velopack Setup 声明不完整。");
        }
        var setupPath = ResolveArtifactPath(
            artifactRoot,
            manifest.VelopackSetupFile);
        if (!ClientReleaseFileFacts.IsSha256(manifest.VelopackSetupSha256)
            || manifest.VelopackSetupSize <= 0
            || !ClientReleaseFileFacts.IsExactRegularFile(
                setupPath,
                manifest.VelopackSetupSha256!,
                manifest.VelopackSetupSize))
        {
            return ArtifactLoadResult.Fail("生成安装包失败：Velopack Setup 缺失或完整性已变更。");
        }

        return ArtifactLoadResult.Success(manifest);
    }

    private static string? ValidateHostFileManifest(
        string artifactRoot,
        string hostDirectory,
        EdgeInstallerArtifactManifest artifact,
        string? expectedSha256)
    {
        var hasArtifactEvidence = !string.IsNullOrWhiteSpace(
                                      artifact.HostFileManifest)
                                  || artifact.HostFileManifestSha256 is not null
                                  || artifact.HostFileManifestFileCount != 0;
        if (!hasArtifactEvidence && expectedSha256 is null)
        {
            return null;
        }

        if (!IsSafeRelativeFile(artifact.HostFileManifest)
            || !ClientReleaseFileFacts.IsSha256(
                artifact.HostFileManifestSha256)
            || artifact.HostFileManifestFileCount <= 0
            || !ClientReleaseFileFacts.IsSha256(expectedSha256)
            || !string.Equals(
                artifact.HostFileManifestSha256,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return "生成安装包失败：Host 逐文件清单与发布记录不一致。";
        }

        var path = ResolveArtifactPath(
            artifactRoot,
            artifact.HostFileManifest);
        if (!File.Exists(path))
        {
            return "生成安装包失败：Host 逐文件清单缺失。";
        }

        var bytes = File.ReadAllBytes(path);
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(
                digest,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return "生成安装包失败：Host 逐文件清单摘要已变更。";
        }

        InstallerHostFileManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<InstallerHostFileManifest>(
                bytes,
                JsonOptions);
        }
        catch (JsonException)
        {
            return "生成安装包失败：Host 逐文件清单无法解析。";
        }

        if (manifest is null
            || manifest.SchemaVersion != 1
            || !string.Equals(manifest.Component, "Host", StringComparison.Ordinal)
            || !string.Equals(manifest.Version, artifact.Version, StringComparison.Ordinal)
            || manifest.Files is null
            || manifest.Files.Count == 0
            || manifest.Files.Count != artifact.HostFileManifestFileCount)
        {
            return "生成安装包失败：Host 逐文件清单与精确版本不一致。";
        }

        var declared = new Dictionary<string, InstallerHostFileManifestEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Files)
        {
            var relativePath = item.Path.Replace('\\', '/');
            if (!IsSafeRelativeFile(relativePath)
                || !declared.TryAdd(relativePath, item)
                || item.Size < 0
                || !ClientReleaseFileFacts.IsSha256(item.Sha256)
                || !string.Equals(item.Component, "Host", StringComparison.Ordinal)
                || !string.Equals(item.Version, artifact.Version, StringComparison.Ordinal))
            {
                return "生成安装包失败：Host 逐文件清单文件项无效。";
            }
        }

        var actual = Directory
            .EnumerateFiles(hostDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(hostDirectory, file)
                .Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(declared.Keys))
        {
            return "生成安装包失败：Host 逐文件清单与实际依赖闭包不一致。";
        }

        foreach (var (relativePath, item) in declared)
        {
            var file = Path.Combine(
                hostDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!ClientReleaseFileFacts.IsExactRegularFile(
                    file,
                    item.Sha256,
                    item.Size))
            {
                return "生成安装包失败：Host 逐文件清单的文件证据不一致。";
            }
        }

        return null;
    }

    private static bool HasExactHostEvidence(
        ClientReleaseComponent component,
        ClientReleaseVersion pluginVersion,
        string hostVersion,
        string? hostFileManifestSha256)
    {
        var hasAnyEvidence = pluginVersion.DependencyClosureSha256 is not null
                             || pluginVersion.DependencyHostVersion is not null
                             || pluginVersion.DependencyHostFileManifestSha256 is not null;
        if (!hasAnyEvidence)
        {
            return component.ManifestSchemaVersion < 3;
        }

        return ClientReleaseFileFacts.IsSha256(
                   pluginVersion.DependencyClosureSha256)
               && ClientReleaseFileFacts.IsSha256(
                   pluginVersion.DependencyHostFileManifestSha256)
               && ClientReleaseFileFacts.IsSha256(hostFileManifestSha256)
               && string.Equals(
                   pluginVersion.DependencyHostVersion,
                   hostVersion,
                   StringComparison.Ordinal)
               && string.Equals(
                   pluginVersion.DependencyHostFileManifestSha256,
                   hostFileManifestSha256,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExactDirectory(
        string directory,
        string? expectedSha256,
        long expectedSize)
        => Directory.Exists(directory)
           && ClientReleaseFileFacts.IsSha256(expectedSha256)
           && expectedSize > 0
           && ClientReleaseFileFacts.GetDirectorySize(directory) == expectedSize
           && string.Equals(
               ClientReleaseFileFacts.ComputeDirectorySha256(directory),
               expectedSha256,
               StringComparison.OrdinalIgnoreCase);

    private PluginPackageLoadResult LoadPluginPackage(
        PluginReleaseSelection selection,
        EdgeInstallerArtifactManifest artifact)
    {
        var moduleId = selection.Component.ComponentKey;
        var version = selection.Version;
        if (!IsSafePluginDirectoryName(moduleId))
        {
            return PluginPackageLoadResult.Fail(
                $"生成安装包失败：插件 {moduleId} 的目录标识非法。");
        }

        var expectedDirectory = string.Join(
            '/',
            "plugins",
            ClientReleaseArtifactBuilder.EscapePathSegment(selection.Component.Channel),
            ClientReleaseArtifactBuilder.EscapePathSegment(moduleId),
            ClientReleaseArtifactBuilder.EscapePathSegment(version.Version));
        var directoryArtifacts = version.Artifacts
            .Where(artifact => artifact.ArtifactKind == ClientReleaseArtifactKind.PluginPackageDirectory)
            .ToList();
        var packageArtifacts = version.Artifacts
            .Where(artifact => artifact.ArtifactKind == ClientReleaseArtifactKind.PackageFile)
            .ToList();
        if (directoryArtifacts.Count != 1
            || packageArtifacts.Count != 1
            || !string.Equals(
                directoryArtifacts[0].RelativePath,
                expectedDirectory,
                StringComparison.Ordinal))
        {
            return PluginPackageLoadResult.Fail(
                $"生成安装包失败：插件 {moduleId} 的发布文件登记不完整。");
        }

        var packageArtifact = packageArtifacts[0];
        var downloadPath = ClientReleaseArtifactBuilder.TryExtractEdgeUpdatesPath(version.DownloadUrl);
        if (!string.Equals(downloadPath, packageArtifact.RelativePath, StringComparison.Ordinal)
            || !packageArtifact.RelativePath.StartsWith(
                expectedDirectory + "/",
                StringComparison.Ordinal)
            || !ClientReleaseFileFacts.IsSha256(packageArtifact.Sha256)
            || packageArtifact.Size is not > 0
            || !string.Equals(packageArtifact.Sha256, version.Sha256, StringComparison.OrdinalIgnoreCase)
            || packageArtifact.Size != version.PackageSize)
        {
            return PluginPackageLoadResult.Fail(
                $"生成安装包失败：插件 {moduleId} 的发布文件登记不一致。");
        }

        var edgeRoot = options.Value.ResolveEdgeUpdatesRoot();
        var packageDirectory = Path.GetFullPath(Path.Combine(edgeRoot, expectedDirectory));
        var packagePath = Path.GetFullPath(Path.Combine(edgeRoot, packageArtifact.RelativePath));
        try
        {
            ClientReleaseControlledDirectory.ValidateChain(
                edgeRoot,
                packageDirectory,
                "插件发布目录非法。",
                requireStrictChild: true);
            ClientReleaseControlledDirectory.ValidateChain(
                edgeRoot,
                Path.GetDirectoryName(packagePath)!,
                "插件发布目录非法。",
                requireStrictChild: true);
        }
        catch (ClientReleaseValidationException)
        {
            return PluginPackageLoadResult.Fail(
                $"生成安装包失败：插件 {moduleId} 的发布文件路径非法。");
        }

        if (!Directory.Exists(packageDirectory)
            || !ClientReleaseFileFacts.IsStrictChildPath(packageDirectory, packagePath)
            || !ClientReleaseFileFacts.IsExactRegularFile(
                packagePath,
                packageArtifact.Sha256!,
                packageArtifact.Size.Value))
        {
            return PluginPackageLoadResult.Fail(
                $"生成安装包失败：插件 {moduleId} 的安装包不存在或完整性校验失败。");
        }

        var archiveError = ValidatePluginPackageArchive(
            packagePath,
            selection,
            artifact);
        if (archiveError is not null)
        {
            return PluginPackageLoadResult.Fail(archiveError);
        }

        return PluginPackageLoadResult.Success(new EdgeInstallerPluginPackage(
            moduleId,
            string.IsNullOrWhiteSpace(selection.Component.DisplayName)
                ? moduleId
                : selection.Component.DisplayName,
            version.Version,
            packageArtifact.Sha256!,
            selection.Component.SupportedProcessType
                ?? throw new InvalidDataException(
                    "Plugin supported process type is missing."),
            moduleId,
            packagePath));
    }

    private static string? ValidatePluginPackageArchive(
        string packagePath,
        PluginReleaseSelection selection,
        EdgeInstallerArtifactManifest artifact)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            ZipArchiveEntry? pluginManifestEntry = null;
            ZipArchiveEntry? fileManifestEntry = null;
            ZipArchiveEntry? dependencyClosureEntry = null;
            foreach (var entry in archive.Entries)
            {
                var normalized = ClientReleaseZipArchive.NormalizeEntryPath(
                    entry.FullName,
                    $"插件 {selection.Component.ComponentKey}");
                if (string.IsNullOrWhiteSpace(normalized)
                    || entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!IsSafeZipEntry(normalized) || !entries.TryAdd(normalized, entry))
                {
                    return $"生成安装包失败：插件 {selection.Component.ComponentKey} 的安装包包含非法或重复路径。";
                }

                if (string.Equals(normalized, "plugin.json", StringComparison.OrdinalIgnoreCase))
                {
                    pluginManifestEntry = entry;
                }
                if (string.Equals(normalized, "file-manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    fileManifestEntry = entry;
                }
                if (string.Equals(normalized, "dependency-closure.json", StringComparison.OrdinalIgnoreCase))
                {
                    dependencyClosureEntry = entry;
                }
            }

            if (pluginManifestEntry is null)
            {
                return $"生成安装包失败：插件 {selection.Component.ComponentKey} 的安装包缺少 plugin.json。";
            }

            using var document = JsonDocument.Parse(pluginManifestEntry.Open());
            var root = document.RootElement;
            if (!TryGetRequiredString(root, "moduleId", out var moduleId)
                || !TryGetRequiredString(root, "version", out var version)
                || !TryGetRequiredString(root, "hostApiVersion", out var hostApiVersion)
                || !TryGetRequiredString(root, "minHostVersion", out var minHostVersion)
                || !TryGetRequiredString(root, "maxHostVersion", out var maxHostVersion)
                || !TryGetRequiredString(root, "entryAssembly", out var entryAssembly)
                || !string.Equals(moduleId, selection.Component.ComponentKey, StringComparison.Ordinal)
                || !string.Equals(version, selection.Version.Version, StringComparison.Ordinal)
                || !string.Equals(hostApiVersion, selection.Version.HostApiVersion, StringComparison.Ordinal)
                || !string.Equals(minHostVersion, selection.Version.MinHostVersion, StringComparison.Ordinal)
                || !string.Equals(maxHostVersion, selection.Version.MaxHostVersion, StringComparison.Ordinal)
                || !IsSafeRelativeFile(entryAssembly)
                || !entries.ContainsKey(entryAssembly))
            {
                return $"生成安装包失败：插件 {selection.Component.ComponentKey} 的 plugin.json 与发布记录不一致。";
            }

            var fileManifestError = ValidateInstalledPluginFileManifest(
                entries,
                fileManifestEntry,
                selection);
            if (fileManifestError is not null)
                return fileManifestError;

            var dependencyClosureError = ValidateInstalledDependencyClosure(
                entries,
                dependencyClosureEntry,
                selection,
                entryAssembly,
                artifact);
            if (dependencyClosureError is not null)
                return dependencyClosureError;

            return null;
        }
        catch (JsonException)
        {
            return $"生成安装包失败：插件 {selection.Component.ComponentKey} 的 plugin.json 无法解析。";
        }
        catch (InvalidDataException)
        {
            return $"生成安装包失败：插件 {selection.Component.ComponentKey} 的安装包格式无效。";
        }
        catch (IOException)
        {
            return $"生成安装包失败：插件 {selection.Component.ComponentKey} 的安装包无法读取。";
        }
        catch (UnauthorizedAccessException)
        {
            return $"生成安装包失败：服务器没有读取插件 {selection.Component.ComponentKey} 安装包的权限。";
        }
        catch (ClientReleaseValidationException)
        {
            return $"生成安装包失败：插件 {selection.Component.ComponentKey} 的安装包包含非法路径。";
        }
    }

    private static string? ValidateInstalledPluginFileManifest(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        ZipArchiveEntry? fileManifestEntry,
        PluginReleaseSelection selection)
    {
        var moduleId = selection.Component.ComponentKey;
        if (fileManifestEntry is null
            || !ClientReleaseFileFacts.IsSha256(
                selection.Version.FileManifestSha256))
        {
            return $"生成安装包失败：插件 {moduleId} 缺少权威 file-manifest.json。";
        }

        byte[] bytes;
        using (var source = fileManifestEntry.Open())
        using (var memory = new MemoryStream())
        {
            source.CopyTo(memory);
            bytes = memory.ToArray();
        }
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(
                digest,
                selection.Version.FileManifestSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"生成安装包失败：插件 {moduleId} 的 file-manifest.json 摘要已变更。";
        }

        InstallerPluginFileManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<InstallerPluginFileManifest>(
                bytes,
                JsonOptions);
        }
        catch (JsonException)
        {
            return $"生成安装包失败：插件 {moduleId} 的 file-manifest.json 无法解析。";
        }
        if (manifest is null
            || manifest.SchemaVersion != 1
            || !string.Equals(manifest.Component, moduleId, StringComparison.Ordinal)
            || !string.Equals(
                manifest.Version,
                selection.Version.Version,
                StringComparison.Ordinal)
            || manifest.Files is null
            || manifest.Files.Count == 0)
        {
            return $"生成安装包失败：插件 {moduleId} 的 file-manifest.json 与精确版本不一致。";
        }

        var declared = new Dictionary<string, InstallerPluginFileManifestEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in manifest.Files)
        {
            var path = ClientReleaseZipArchive.NormalizeEntryPath(
                item.Path,
                $"插件 {moduleId} file manifest");
            if (string.IsNullOrWhiteSpace(path)
                || string.Equals(path, "file-manifest.json", StringComparison.OrdinalIgnoreCase)
                || !declared.TryAdd(path, item)
                || item.Size < 0
                || !ClientReleaseFileFacts.IsSha256(item.Sha256)
                || !string.Equals(item.Component, moduleId, StringComparison.Ordinal)
                || !string.Equals(item.Version, selection.Version.Version, StringComparison.Ordinal))
            {
                return $"生成安装包失败：插件 {moduleId} 的 file-manifest.json 文件项无效。";
            }
        }

        var actual = entries.Keys
            .Where(path => !string.Equals(
                path,
                "file-manifest.json",
                StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actual.SetEquals(declared.Keys))
        {
            return $"生成安装包失败：插件 {moduleId} 文件集与 file-manifest.json 不一致。";
        }

        foreach (var pair in declared)
        {
            using var source = entries[pair.Key].Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long size = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                size += read;
            }
            if (size != pair.Value.Size
                || !string.Equals(
                    Convert.ToHexString(hash.GetHashAndReset()),
                    pair.Value.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return $"生成安装包失败：插件 {moduleId} 文件哈希不一致: {pair.Key}。";
            }
        }

        return null;
    }

    private static string? ValidateInstalledDependencyClosure(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        ZipArchiveEntry? closureEntry,
        PluginReleaseSelection selection,
        string entryAssembly,
        EdgeInstallerArtifactManifest artifact)
    {
        var version = selection.Version;
        var hasAnyEvidence = version.DependencyClosureSha256 is not null
                             || version.DependencyHostVersion is not null
                             || version.DependencyHostFileManifestSha256 is not null;
        if (!hasAnyEvidence && selection.Component.ManifestSchemaVersion < 3)
        {
            return null;
        }

        var moduleId = selection.Component.ComponentKey;
        if (closureEntry is null
            || !ClientReleaseFileFacts.IsSha256(version.DependencyClosureSha256)
            || !ClientReleaseFileFacts.IsSha256(
                version.DependencyHostFileManifestSha256)
            || string.IsNullOrWhiteSpace(version.DependencyHostVersion))
        {
            return $"生成安装包失败：插件 {moduleId} 缺少权威 dependency-closure.json。";
        }

        byte[] bytes;
        using (var source = closureEntry.Open())
        using (var memory = new MemoryStream())
        {
            source.CopyTo(memory);
            bytes = memory.ToArray();
        }

        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(
                digest,
                version.DependencyClosureSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return $"生成安装包失败：插件 {moduleId} 的 dependency-closure.json 摘要已变更。";
        }

        IReadOnlyDictionary<string, InstallerHostFileManifestEntry>? hostFiles = null;
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var schemaVersion)
                || !schemaVersion.TryGetInt32(out var schema)
                || schema != 2
                || !TryGetRequiredString(root, "entryAssembly", out var closureEntryAssembly)
                || !string.Equals(closureEntryAssembly, entryAssembly, StringComparison.Ordinal)
                || !root.TryGetProperty("plugin", out var plugin)
                || plugin.ValueKind != JsonValueKind.Object
                || !TryGetRequiredString(plugin, "moduleId", out var closureModuleId)
                || !TryGetRequiredString(plugin, "version", out var closureVersion)
                || !TryGetRequiredString(plugin, "targetRuntime", out var targetRuntime)
                || !string.Equals(closureModuleId, moduleId, StringComparison.Ordinal)
                || !string.Equals(closureVersion, version.Version, StringComparison.Ordinal)
                || !string.Equals(targetRuntime, selection.Component.TargetRuntime, StringComparison.Ordinal)
                || !root.TryGetProperty("host", out var host)
                || host.ValueKind != JsonValueKind.Object
                || !TryGetRequiredString(host, "component", out var hostComponent)
                || !TryGetRequiredString(host, "version", out var hostVersion)
                || !TryGetRequiredString(host, "fileManifestSha256", out var hostManifestSha)
                || hostComponent is not ("Host" or "IIoT.Edge.Host")
                || !string.Equals(hostVersion, version.DependencyHostVersion, StringComparison.Ordinal)
                || !string.Equals(
                    hostManifestSha,
                    version.DependencyHostFileManifestSha256,
                    StringComparison.OrdinalIgnoreCase)
                || !root.TryGetProperty("dependencies", out var dependencies)
                || dependencies.ValueKind != JsonValueKind.Array
                || dependencies.GetArrayLength() == 0)
            {
                return $"生成安装包失败：插件 {moduleId} 的依赖闭包与精确 Host 证据不一致。";
            }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entryMatches = 0;
            foreach (var dependency in dependencies.EnumerateArray())
            {
                if (!TryGetRequiredString(dependency, "publishPath", out var publishPath)
                    || !IsSafeRelativeFile(publishPath)
                    || !paths.Add(publishPath)
                    || !TryGetRequiredString(dependency, "source", out var source)
                    || source is not ("host" or "plugin")
                    || !dependency.TryGetProperty("size", out var sizeElement)
                    || !sizeElement.TryGetInt64(out var size)
                    || size < 0
                    || !TryGetRequiredString(dependency, "sha256", out var sha256)
                    || !ClientReleaseFileFacts.IsSha256(sha256))
                {
                    return $"生成安装包失败：插件 {moduleId} 的依赖闭包文件项无效。";
                }

                if (source == "plugin"
                    && (!entries.TryGetValue(publishPath, out var packageEntry)
                        || packageEntry.Length != size))
                {
                    return $"生成安装包失败：插件 {moduleId} 的自有依赖未随包携带。";
                }
                else if (source == "host")
                {
                    hostFiles ??= ReadValidatedHostFiles(artifact);
                    if (hostFiles is null
                        || !hostFiles.TryGetValue(publishPath, out var hostFile)
                        || hostFile.Size != size
                        || !string.Equals(
                            hostFile.Sha256,
                            sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return $"生成安装包失败：插件 {moduleId} 声明的 Host 公共依赖不属于所选精确 Host。";
                    }
                }

                if (string.Equals(publishPath, entryAssembly, StringComparison.OrdinalIgnoreCase))
                {
                    if (source != "plugin")
                    {
                        return $"生成安装包失败：插件 {moduleId} 入口不能由 Host 代管。";
                    }

                    entryMatches++;
                }
            }

            if (entryMatches != 1)
            {
                return $"生成安装包失败：插件 {moduleId} 入口未在依赖闭包中唯一声明。";
            }
        }
        catch (JsonException)
        {
            return $"生成安装包失败：插件 {moduleId} 的 dependency-closure.json 无法解析。";
        }

        return null;
    }

    private static IReadOnlyDictionary<string, InstallerHostFileManifestEntry>?
        ReadValidatedHostFiles(EdgeInstallerArtifactManifest artifact)
    {
        if (!IsSafeRelativeFile(artifact.HostFileManifest)
            || !ClientReleaseFileFacts.IsSha256(
                artifact.HostFileManifestSha256))
        {
            return null;
        }

        var path = ResolveArtifactPath(
            artifact.RootPath,
            artifact.HostFileManifest);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        if (!string.Equals(
                Convert.ToHexString(SHA256.HashData(bytes)),
                artifact.HostFileManifestSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<InstallerHostFileManifest>(
                bytes,
                JsonOptions);
            if (manifest?.Files is null
                || manifest.SchemaVersion != 1
                || !string.Equals(
                    manifest.Component,
                    "Host",
                    StringComparison.Ordinal)
                || !string.Equals(
                    manifest.Version,
                    artifact.Version,
                    StringComparison.Ordinal)
                || manifest.Files.Count == 0
                || manifest.Files.Count != artifact.HostFileManifestFileCount)
            {
                return null;
            }

            var result = new Dictionary<string, InstallerHostFileManifestEntry>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                var relativePath = file.Path.Replace('\\', '/');
                if (!IsSafeRelativeFile(relativePath)
                    || !ClientReleaseFileFacts.IsSha256(file.Sha256)
                    || file.Size < 0
                    || !string.Equals(
                        file.Component,
                        "Host",
                        StringComparison.Ordinal)
                    || !string.Equals(
                        file.Version,
                        artifact.Version,
                        StringComparison.Ordinal)
                    || !result.TryAdd(relativePath, file))
                {
                    return null;
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetRequiredString(
        JsonElement root,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private async Task<DeviceLoadResult> LoadDevicesAsync(
        IReadOnlyList<EdgeBindingSelection> selections,
        CancellationToken cancellationToken)
    {
        var requestedIds = selections.Select(item => item.DeviceId).ToList();
        var accessScope = await currentUserDeviceAccessService.GetAccessibleDeviceIdsAsync(cancellationToken);
        if (!accessScope.IsSuccess)
        {
            return DeviceLoadResult.Fail(
                accessScope.Errors?.FirstOrDefault() ?? "生成安装包失败：用户凭证异常。");
        }

        if (accessScope.Value is { } allowedDeviceIds
            && requestedIds.Any(deviceId => !allowedDeviceIds.Contains(deviceId)))
        {
            return DeviceLoadResult.Fail("生成安装包失败：包含未授权访问的设备。");
        }

        var devices = await deviceRepository.GetListAsync(
            new DevicePagedSpec(0, 0, requestedIds, isPaging: false),
            cancellationToken);
        var deviceById = devices.ToDictionary(device => device.Id);
        foreach (var selection in selections)
        {
            if (!deviceById.ContainsKey(selection.DeviceId))
            {
                return DeviceLoadResult.Fail(
                    $"生成安装包失败：插件 {selection.ModuleId} 选择的设备不存在或已删除。");
            }
        }

        return DeviceLoadResult.Success(deviceById);
    }

    private static List<DeviceBootstrapSecretTarget>
        CreateDeviceSecretTargets(
        Guid generationId,
        DateTime expiresAtUtc,
        IReadOnlyList<EdgeBindingSelection> selections,
        IReadOnlyDictionary<Guid, Device> deviceById,
        IReadOnlyList<EdgeInstallerPluginPackage> plugins)
    {
        var targets =
            new List<DeviceBootstrapSecretTarget>(selections.Count);
        for (var index = 0; index < selections.Count; index++)
        {
            var selection = selections[index];
            var plugin = plugins[index];
            var device = deviceById[selection.DeviceId];
            var bootstrapSecret = BootstrapSecretGenerator.Generate();
            var targetHash = BootstrapSecretHasher.Hash(
                bootstrapSecret);
            var pluginRoot = $"plugins/{device.Code}";
            var pendingCredential = new EdgeInstallerPendingCredential(
                generationId,
                device.Id,
                device.Code,
                targetHash,
                selection.ModuleId,
                plugin.Version,
                plugin.Sha256,
                expiresAtUtc);
            targets.Add(new DeviceBootstrapSecretTarget(
                device.Id,
                pendingCredential,
                new EdgeBindingItemDto(
                    device.Code,
                    device.DeviceName,
                    device.ProcessId,
                    plugin.SupportedProcessType,
                    selection.ModuleId,
                    plugin.Version,
                    plugin.Sha256,
                    $"{pluginRoot}/app",
                    $"{pluginRoot}/config",
                    $"{pluginRoot}/db",
                    $"{pluginRoot}/data",
                    $"{pluginRoot}/logs",
                    $"{pluginRoot}/cache",
                    $"{pluginRoot}/context",
                    $"{pluginRoot}/buffers",
                    new EdgePendingCredentialDto(
                        $"IIoT.Edge/Pending/{generationId:D}/{device.Code}",
                        bootstrapSecret))));
        }

        return targets;
    }

    private static bool LoadedDevicesMatchObservation(
        IReadOnlyDictionary<Guid, Device> devices,
        IReadOnlyCollection<DeviceBootstrapWriteState> observation)
        => LoadedDevicesMatchObservation(
            devices.Values.ToArray(),
            observation);

    private static bool LoadedDevicesMatchObservation(
        IReadOnlyCollection<Device> devices,
        IReadOnlyCollection<DeviceBootstrapWriteState> observation)
    {
        if (devices.Count != observation.Count)
        {
            return false;
        }

        var observedById = observation.ToDictionary(
            item => item.DeviceId);
        return devices.All(device =>
            observedById.TryGetValue(device.Id, out var observed)
            && device.DeviceName == observed.DeviceName
            && device.Code == observed.ClientCode
            && device.ProcessId == observed.ProcessId
            && device.BootstrapSecretHash
            == observed.BootstrapSecretHash
            && device.RowVersion == observed.RowVersion);
    }

    private static string? ValidateArtifactLayout(EdgeInstallerArtifactManifest artifact)
    {
        try
        {
            var launcherDirectory = ResolveArtifactDirectoryPath(artifact, artifact.LauncherDirectory);
            if (!Directory.Exists(launcherDirectory) || !Directory.EnumerateFiles(launcherDirectory, "*", SearchOption.AllDirectories).Any())
            {
                return "生成安装包失败：安装素材缺少 launcher 运行目录。";
            }

            var hostDirectory = ResolveArtifactDirectoryPath(artifact, artifact.HostDirectory);
            if (!Directory.Exists(hostDirectory) || !Directory.EnumerateFiles(hostDirectory, "*", SearchOption.AllDirectories).Any())
            {
                return "生成安装包失败：安装素材缺少 host 运行目录。";
            }

            if (string.IsNullOrWhiteSpace(artifact.VelopackSetupFile))
            {
                return "生成安装包失败：安装素材未包含 Velopack Setup，无法生成安装包。";
            }

            var normalizedVelopackSetupFile = artifact.VelopackSetupFile.Replace('\\', '/').Trim('/');
            if (!IsSafeRelativeFile(normalizedVelopackSetupFile)
                || !normalizedVelopackSetupFile.StartsWith("velopack/", StringComparison.OrdinalIgnoreCase)
                || !normalizedVelopackSetupFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return "生成安装包失败：安装素材 Velopack Setup 路径无效。";
            }

            var velopackSetupPath = ResolveArtifactPath(artifact.RootPath, normalizedVelopackSetupFile);
            if (!File.Exists(velopackSetupPath))
            {
                return "生成安装包失败：安装素材缺少 Velopack Setup 文件。";
            }

            return null;
        }
        catch (InvalidDataException)
        {
            return "生成安装包失败：安装素材路径无效。";
        }
        catch (IOException)
        {
            return "生成安装包失败：安装素材目录无法读取。";
        }
        catch (UnauthorizedAccessException)
        {
            return "生成安装包失败：服务器没有读取安装素材目录的权限。";
        }
    }

    private async Task WriteSuccessAuditAsync(
        IReadOnlyList<EdgeBindingItemDto> bindings,
        IReadOnlyDictionary<Guid, Device> deviceById,
        DateTime executedAtUtc,
        CancellationToken cancellationToken)
    {
        var allAuditsConfirmed = true;
        foreach (var binding in bindings)
        {
            var device = deviceById.Values.Single(item => item.Code == binding.ClientCode);
            allAuditsConfirmed &=
                await CloudWriteCommitRecovery.TryConfirmRecoveredAuditAsync(
                    auditTrailService,
                    new AuditTrailEntry(
                        ClientReleaseAuditActor.ParseId(currentUser.Id),
                        currentUser.UserName,
                        "Edge.GenerateInstallerPackage",
                        "Device",
                        device.Id.ToString(),
                        executedAtUtc,
                        true,
                        $"为设备 {device.DeviceName}（{device.Code}）生成带独立待激活凭据的客户端首装包，未替换现场旧凭据。",
                        null,
                        $"edge-installer-secret:{executedAtUtc.Ticks:x}:{device.Id:N}"));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!allAuditsConfirmed)
        {
            throw new CloudWriteCommitUnknownException();
        }
    }

    private static Stream BuildInstallerPackage(
        EdgeInstallerArtifactManifest artifact,
        IReadOnlyCollection<EdgeInstallerPluginPackage> selectedPlugins,
        EdgeBindingBundleDto bindingBundle,
        string targetRuntime,
        EdgeInstallerArtifactOptions artifactOptions)
    {
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"iiot-edge-installer-{Guid.NewGuid():N}.exe");
        var packageStream = new FileStream(
            tempPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan);

        try
        {
            using (var stubStream = File.OpenRead(artifact.InstallerStubPath))
            {
                stubStream.CopyTo(packageStream);
            }

            var payloadLength = WritePayloadZipToPackage(
                packageStream,
                artifact,
                selectedPlugins,
                bindingBundle,
                targetRuntime,
                artifactOptions);

            Span<byte> trailer = stackalloc byte[16];
            BinaryPrimitives.WriteInt64LittleEndian(trailer[..8], payloadLength);
            InstallerMagic.CopyTo(trailer[8..]);
            packageStream.Write(trailer);
            packageStream.Position = 0;
            return packageStream;
        }
        catch
        {
            packageStream.Dispose();
            throw;
        }
    }

    private static long WritePayloadZipToPackage(
        Stream packageStream,
        EdgeInstallerArtifactManifest artifact,
        IReadOnlyCollection<EdgeInstallerPluginPackage> selectedPlugins,
        EdgeBindingBundleDto bindingBundle,
        string targetRuntime,
        EdgeInstallerArtifactOptions artifactOptions)
    {
        var payloadTempPath = Path.Combine(
            Path.GetTempPath(),
            $"iiot-edge-payload-{Guid.NewGuid():N}.zip");
        using var payloadStream = new FileStream(
            payloadTempPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan);

        WritePayloadZip(
            payloadStream,
            artifact,
            selectedPlugins,
            bindingBundle,
            targetRuntime,
            artifactOptions);
        payloadStream.Position = 0;
        payloadStream.CopyTo(packageStream);
        return payloadStream.Length;
    }

    private static void WritePayloadZip(
        Stream packageStream,
        EdgeInstallerArtifactManifest artifact,
        IReadOnlyCollection<EdgeInstallerPluginPackage> selectedPlugins,
        EdgeBindingBundleDto bindingBundle,
        string targetRuntime,
        EdgeInstallerArtifactOptions artifactOptions)
    {
        using (var target = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var reservedEntries = BuildGeneratedEntryNames(artifact);
            var writtenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddDirectoryEntries(
                target,
                artifact,
                artifact.LauncherDirectory,
                reservedEntries,
                writtenEntries);

            AddDirectoryEntries(
                target,
                artifact,
                artifact.HostDirectory,
                reservedEntries,
                writtenEntries);

            foreach (var plugin in selectedPlugins.OrderBy(
                         plugin => plugin.ModuleId,
                         StringComparer.OrdinalIgnoreCase))
            {
                AddPluginPackageEntries(
                    target,
                    artifact,
                    plugin,
                    reservedEntries,
                    writtenEntries);
            }

            AddFileEntry(
                target,
                artifact,
                artifact.VelopackSetupFile!,
                reservedEntries,
                writtenEntries);

            WriteJsonEntry(
                target,
                CombineZipPath(artifact.LauncherDirectory, BindingFileName),
                bindingBundle,
                writtenEntries);
            WriteJsonEntry(
                target,
                CombineZipPath(artifact.LauncherDirectory, HostPluginManifestFileName),
                BuildHostPluginManifest(selectedPlugins, bindingBundle),
                writtenEntries);

            WriteJsonEntry(
                target,
                CombineZipPath(artifact.LauncherDirectory, UpdateConfigFileName),
                BuildUpdateConfig(bindingBundle, artifact.Channel, targetRuntime),
                writtenEntries);
        }

        EdgePayloadManifestWriter.AppendSignedManifest(
            packageStream,
            bindingBundle.GenerationId,
            bindingBundle.GeneratedAtUtc,
            artifact.Version,
            artifact.LauncherDirectory,
            artifact.HostDirectory,
            artifact.PluginsRoot,
            selectedPlugins.ToDictionary(
                plugin => plugin.PluginDirectory,
                plugin => plugin.Version,
                StringComparer.OrdinalIgnoreCase),
            artifactOptions);
    }

    private static HashSet<string> BuildGeneratedEntryNames(
        EdgeInstallerArtifactManifest artifact)
    {
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CombineZipPath(artifact.LauncherDirectory, BindingFileName),
            CombineZipPath(artifact.LauncherDirectory, HostPluginManifestFileName),
            CombineZipPath(artifact.LauncherDirectory, UpdateConfigFileName),
            CombineZipPath(artifact.LauncherDirectory, LauncherProfileCatalogFileName),
            EdgePayloadManifestContract.FileName
        };

        return entries;
    }

    private static EdgeInstallerHostPluginManifest BuildHostPluginManifest(
        IReadOnlyCollection<EdgeInstallerPluginPackage> selectedPlugins,
        EdgeBindingBundleDto bindingBundle)
    {
        var plugins = bindingBundle.Bindings
            .Select(binding =>
            {
                var plugin = selectedPlugins.Single(item =>
                    string.Equals(item.ModuleId, binding.ModuleId, StringComparison.OrdinalIgnoreCase));
                return new EdgeInstallerHostPluginItem(
                    plugin.ModuleId,
                    plugin.DisplayName,
                    plugin.Version,
                    plugin.Sha256,
                    binding.ClientCode,
                    binding.ClientCode,
                    binding.DeviceName,
                    binding.ProcessId);
            })
            .ToList();
        return new EdgeInstallerHostPluginManifest(2, bindingBundle.GeneratedAtUtc, plugins);
    }

    private static EdgeInstallerUpdateConfig BuildUpdateConfig(
        EdgeBindingBundleDto bindingBundle,
        string channel,
        string targetRuntime)
    {
        string? source = null;
        if (!string.IsNullOrWhiteSpace(bindingBundle.BaseUrl))
        {
            source = $"{bindingBundle.BaseUrl.TrimEnd('/')}/edge-updates/velopack/{channel}/";
        }

        return new EdgeInstallerUpdateConfig(source, channel, targetRuntime);
    }

    private static void AddDirectoryEntries(
        ZipArchive target,
        EdgeInstallerArtifactManifest artifact,
        string sourceDirectory,
        IReadOnlySet<string> reservedEntries,
        ISet<string> writtenEntries)
    {
        var normalizedDirectory = NormalizeZipDirectory(sourceDirectory);
        var sourcePath = ResolveArtifactDirectoryPath(artifact, normalizedDirectory);
        foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourcePath, filePath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            var entryName = CombineZipPath(normalizedDirectory, relativePath);
            if (!IsSafeZipEntry(entryName))
            {
                throw new InvalidDataException("Artifact contains unsafe path.");
            }

            if (IsRemovedPluginBindingEntry(entryName)
                || reservedEntries.Contains(entryName)
                || !writtenEntries.Add(entryName))
            {
                continue;
            }

            var entry = target.CreateEntry(entryName, CompressionLevel.Fastest);
            using var sourceStream = File.OpenRead(filePath);
            using var targetStream = entry.Open();
            sourceStream.CopyTo(targetStream);
        }
    }

    private static void AddPluginPackageEntries(
        ZipArchive target,
        EdgeInstallerArtifactManifest artifact,
        EdgeInstallerPluginPackage plugin,
        IReadOnlySet<string> reservedEntries,
        ISet<string> writtenEntries)
    {
        using var archive = ZipFile.OpenRead(plugin.PackagePath);
        foreach (var sourceEntry in archive.Entries)
        {
            var relativePath = ClientReleaseZipArchive.NormalizeEntryPath(
                sourceEntry.FullName,
                $"插件 {plugin.ModuleId}");
            if (string.IsNullOrWhiteSpace(relativePath)
                || sourceEntry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            var entryName = CombineZipPath(
                artifact.PluginsRoot,
                plugin.PluginDirectory,
                relativePath);
            if (!IsSafeZipEntry(entryName))
            {
                throw new InvalidDataException("Plugin package contains unsafe path.");
            }

            if (IsRemovedPluginBindingEntry(entryName)
                || reservedEntries.Contains(entryName))
            {
                continue;
            }

            if (!writtenEntries.Add(entryName))
            {
                throw new InvalidDataException("Plugin package contains duplicate path.");
            }

            var targetEntry = target.CreateEntry(entryName, CompressionLevel.Fastest);
            using var sourceStream = sourceEntry.Open();
            using var targetStream = targetEntry.Open();
            sourceStream.CopyTo(targetStream);
        }
    }

    private static void AddFileEntry(
        ZipArchive target,
        EdgeInstallerArtifactManifest artifact,
        string sourceFile,
        IReadOnlySet<string> reservedEntries,
        ISet<string> writtenEntries)
    {
        var entryName = sourceFile.Replace('\\', '/').Trim('/');
        if (!IsSafeRelativeFile(entryName) || !IsSafeZipEntry(entryName))
        {
            throw new InvalidDataException("Artifact contains unsafe path.");
        }

        if (reservedEntries.Contains(entryName) || !writtenEntries.Add(entryName))
        {
            throw new InvalidDataException("Artifact contains duplicate generated path.");
        }

        var sourcePath = ResolveArtifactPath(artifact.RootPath, entryName);
        var entry = target.CreateEntry(entryName, CompressionLevel.Fastest);
        using var sourceStream = File.OpenRead(sourcePath);
        using var targetStream = entry.Open();
        sourceStream.CopyTo(targetStream);
    }

    private static void WriteJsonEntry(
        ZipArchive target,
        string entryName,
        object value,
        ISet<string> writtenEntries)
    {
        if (!IsSafeZipEntry(entryName))
        {
            throw new InvalidDataException("Generated config path is unsafe.");
        }

        if (!writtenEntries.Add(entryName))
        {
            throw new InvalidDataException("Generated config path collides with artifact file.");
        }

        var entry = target.CreateEntry(entryName, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(JsonSerializer.Serialize(value, JsonOptions));
    }

    private static bool IsSafeZipDirectory(string directory)
    {
        var normalized = directory.Replace('\\', '/').Trim('/');
        return !string.IsNullOrWhiteSpace(normalized)
            && !normalized.StartsWith("/", StringComparison.Ordinal)
            && !normalized.Split('/').Any(part => part is "." or ".." or "");
    }

    private static bool IsSafePluginDirectoryName(string directory)
        => IsSafeZipDirectory(directory)
           && !directory.Contains('/')
           && !directory.Contains('\\');

    private static bool IsSafeRelativeFile(string filePath)
    {
        var normalized = filePath.Replace('\\', '/').Trim('/');
        return !string.IsNullOrWhiteSpace(normalized)
            && !normalized.EndsWith("/", StringComparison.Ordinal)
            && !normalized.StartsWith("/", StringComparison.Ordinal)
            && !normalized.Split('/').Any(part => part is "." or ".." or "");
    }

    private static bool IsSafeZipEntry(string entryName)
    {
        return !entryName.StartsWith("/", StringComparison.Ordinal)
            && !entryName.Split('/').Any(part => part is "." or "..");
    }

    private static bool IsRemovedPluginBindingEntry(string entryName)
        => string.Equals(
            entryName.Replace('\\', '/').Split('/').LastOrDefault(),
            RemovedPluginBindingFileName,
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeZipDirectory(string directory)
        => directory.Replace('\\', '/').Trim('/');

    private static string CombineZipPath(params string[] parts)
        => string.Join(
            '/',
            parts.Select(part => part.Replace('\\', '/').Trim('/')).Where(part => part.Length > 0));

    private static string ResolveArtifactDirectoryPath(EdgeInstallerArtifactManifest artifact, string directory)
        => ResolveArtifactPath(artifact.RootPath, NormalizeZipDirectory(directory));

    private static string ResolveArtifactPath(string artifactRoot, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(artifactRoot);
        var normalizedRelative = relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, normalizedRelative));
        if (!fullPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Artifact path escaped root.");
        }

        return fullPath;
    }

    private static bool TryNormalizeSelections(
        IReadOnlyList<EdgeBindingSelection>? selections,
        out List<EdgeBindingSelection> normalized,
        out string? error)
    {
        normalized = [];
        error = null;
        if (selections is null || selections.Count == 0)
        {
            error = "生成安装包失败：请至少为一个插件选择设备。";
            return false;
        }

        var seenModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenDevices = new HashSet<Guid>();
        foreach (var selection in selections)
        {
            var moduleId = selection.ModuleId?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(moduleId))
            {
                error = "生成安装包失败：存在未选择插件的配置行。";
                return false;
            }

            if (selection.DeviceId == Guid.Empty)
            {
                error = $"生成安装包失败：插件 {moduleId} 未选择设备。";
                return false;
            }

            if (!seenModules.Add(moduleId))
            {
                error = $"生成安装包失败：插件 {moduleId} 重复，请合并为一行。";
                return false;
            }

            if (!seenDevices.Add(selection.DeviceId))
            {
                error = "生成安装包失败：同一台设备不能分配给多个插件。";
                return false;
            }

            normalized.Add(new EdgeBindingSelection(
                moduleId,
                selection.DeviceId,
                string.IsNullOrWhiteSpace(selection.PluginVersion)
                    ? null
                    : selection.PluginVersion.Trim()));
        }

        return true;
    }

    private async Task<Result<EdgeInstallerPackageDto>> FailAsync(
        string message,
        CancellationToken cancellationToken,
        bool forbidden = false)
    {
        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ClientReleaseAuditActor.ParseId(currentUser.Id),
                currentUser.UserName,
                "Edge.GenerateInstallerPackage",
                "Device",
                "installer-package",
                DateTime.UtcNow,
                false,
                "生成客户端安装包。",
                message),
            cancellationToken);

        return forbidden ? Result.Forbidden(message) : Result.Failure(message);
    }

    private static string BuildDownloadFileName(IReadOnlyList<EdgeBindingItemDto> bindings, string version)
    {
        var identity = bindings.Count == 1 ? bindings[0].ClientCode : "bundle";
        var safeIdentity = new string(identity.Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-').ToArray());
        return $"IIoT.EdgeClient-{safeIdentity}-{version}.exe";
    }

    private static InstallerPackageFact ComputePackageFact(Stream packageStream)
    {
        packageStream.Position = 0;
        var sha256 = Convert.ToHexString(SHA256.HashData(packageStream)).ToLowerInvariant();
        var size = packageStream.Length;
        packageStream.Position = 0;
        return new InstallerPackageFact(sha256, size);
    }

    private sealed record HostReleaseSelection(
        ClientReleaseComponent Component,
        ClientReleaseVersion Version);

    private sealed record InstallerPackageFact(string Sha256, long Size);

    private sealed record DeviceBootstrapSecretTarget(
        Guid DeviceId,
        EdgeInstallerPendingCredential PendingCredential,
        EdgeBindingItemDto Binding);

    private sealed record PluginReleaseSelection(
        ClientReleaseComponent Component,
        ClientReleaseVersion Version);

    private sealed record PluginReleaseResolution(
        bool IsSuccess,
        PluginReleaseSelection? Selection,
        string? Error)
    {
        public static PluginReleaseResolution Success(PluginReleaseSelection selection)
            => new(true, selection, null);

        public static PluginReleaseResolution Fail(string error)
            => new(false, null, error);
    }

    private sealed record EdgeInstallerPluginPackage(
        string ModuleId,
        string DisplayName,
        string Version,
        string Sha256,
        string SupportedProcessType,
        string PluginDirectory,
        string PackagePath);

    private sealed class InstallerPluginFileManifest
    {
        public int SchemaVersion { get; set; }
        public string Component { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<InstallerPluginFileManifestEntry>? Files { get; set; }
    }

    private sealed class InstallerPluginFileManifestEntry
    {
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Component { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    private sealed class InstallerHostFileManifest
    {
        public int SchemaVersion { get; set; }
        public string Component { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<InstallerHostFileManifestEntry>? Files { get; set; }
    }

    private sealed class InstallerHostFileManifestEntry
    {
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Sha256 { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Component { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }

    private sealed record PluginPackageLoadResult(
        bool IsSuccess,
        EdgeInstallerPluginPackage? Package,
        string? Error)
    {
        public static PluginPackageLoadResult Success(EdgeInstallerPluginPackage package)
            => new(true, package, null);

        public static PluginPackageLoadResult Fail(string error)
            => new(false, null, error);
    }

    private sealed record ArtifactLoadResult(
        bool IsSuccess,
        EdgeInstallerArtifactManifest? Artifact,
        string? Error)
    {
        public static ArtifactLoadResult Success(EdgeInstallerArtifactManifest artifact)
            => new(true, artifact, null);

        public static ArtifactLoadResult Fail(string error)
            => new(false, null, error);
    }

    private sealed record DeviceLoadResult(
        bool IsSuccess,
        IReadOnlyDictionary<Guid, Device>? DevicesById,
        string? Error)
    {
        public static DeviceLoadResult Success(IReadOnlyDictionary<Guid, Device> devicesById)
            => new(true, devicesById, null);

        public static DeviceLoadResult Fail(string error)
            => new(false, null, error);
    }
}

internal sealed class EdgeInstallerArtifactManifest
{
    public int SchemaVersion { get; set; }

    public int InstallerBindingSchemaVersion { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string HostApiVersion { get; set; } = string.Empty;

    public string TargetRuntime { get; set; } = string.Empty;

    public string? TargetFramework { get; set; }

    public DateTime? GeneratedAtUtc { get; set; }

    public string? SourceCommit { get; set; }

    public string? PreviousVersion { get; set; }

    public string? PreviousSourceCommit { get; set; }

    public string? ReleaseNotes { get; set; }

    public string InstallerStubFile { get; set; } = string.Empty;

    public string? InstallerStubSha256 { get; set; }

    public long InstallerStubSize { get; set; }

    public string LauncherDirectory { get; set; } = "launcher";

    public string? LauncherDirectorySha256 { get; set; }

    public long LauncherDirectorySize { get; set; }

    public string HostDirectory { get; set; } = "host";

    public string? HostDirectorySha256 { get; set; }

    public long HostDirectorySize { get; set; }

    public string HostFileManifest { get; set; } = string.Empty;

    public string? HostFileManifestSha256 { get; set; }

    public int HostFileManifestFileCount { get; set; }

    public string PluginsRoot { get; set; } = "plugins";

    public string? VelopackSetupFile { get; set; }

    public string? VelopackSetupSha256 { get; set; }

    public long VelopackSetupSize { get; set; }

    public List<EdgeInstallerArtifactModule> Modules { get; set; } = [];

    public string RootPath { get; set; } = string.Empty;

    public string InstallerStubPath { get; set; } = string.Empty;
}

internal sealed class EdgeInstallerArtifactModule
{
    public string ModuleId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Version { get; set; } = string.Empty;

    public string HostApiVersion { get; set; } = string.Empty;

    public string MinHostVersion { get; set; } = string.Empty;

    public string MaxHostVersion { get; set; } = string.Empty;

    public string PluginDirectory { get; set; } = string.Empty;

    public string? PluginSha256 { get; set; }

    public long PluginSize { get; set; }
}

internal sealed record EdgeInstallerHostPluginManifest(
    int SchemaVersion,
    DateTime GeneratedAtUtc,
    IReadOnlyList<EdgeInstallerHostPluginItem> Plugins);

internal sealed record EdgeInstallerHostPluginItem(
    string ModuleId,
    string DisplayName,
    string Version,
    string PackageSha256,
    string PluginDirectory,
    string ClientCode,
    string DeviceName,
    Guid ProcessId);

internal sealed record EdgeInstallerUpdateConfig(
    string? Source,
    string Channel,
    string TargetRuntime);
