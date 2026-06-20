using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.ProductionService.ClientReleases;
using IIoT.ProductionService.Security;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AuthorizeRequirement("Device.Update")]
public sealed record GenerateEdgeInstallerPackageCommand(
    IReadOnlyList<EdgeBindingSelection> Selections,
    string? Channel = null,
    string? TargetRuntime = null,
    string? HostVersion = null,
    string? BaseUrl = null) : IHumanCommand<Result<EdgeInstallerPackageDto>>;

public sealed record EdgeInstallerPackageDto(
    string FileName,
    string ContentType,
    Stream Content);

public sealed class GenerateEdgeInstallerPackageHandler(
    ICurrentUser currentUser,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IRepository<Device> deviceRepository,
    IReadRepository<ClientHostRelease> hostReleaseRepository,
    IReadRepository<ClientPluginRelease> pluginReleaseRepository,
    ICacheService cacheService,
    IAuditTrailService auditTrailService,
    IEdgeInstallerArtifactCatalogReader artifactCatalogReader,
    IOptions<EdgeInstallerArtifactOptions> options)
    : ICommandHandler<GenerateEdgeInstallerPackageCommand, Result<EdgeInstallerPackageDto>>
{
    private static readonly byte[] InstallerMagic = "IIOTEDG1"u8.ToArray();
    private const string BindingFileName = "iiot-binding.json";
    private const string HostPluginManifestFileName = "iiot-enabled-plugins.json";
    private const string PluginBindingFileName = "iiot-plugin-binding.json";
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
        if (!currentUserDeviceAccessService.IsAdministrator)
        {
            return await FailAsync("只有管理员可以生成客户端安装包。", cancellationToken, forbidden: true);
        }

        var channel = NormalizeDefault(request.Channel, "stable");
        var targetRuntime = NormalizeDefault(request.TargetRuntime, "win-x64");
        if (!EdgeInstallerPublicBaseUrl.TryNormalize(request.BaseUrl, out var publicBaseUrl, out var baseUrlError))
        {
            return await FailAsync($"生成安装包失败：{baseUrlError}", cancellationToken);
        }

        if (!TryNormalizeSelections(request.Selections, out var selections, out var selectionError))
        {
            return await FailAsync(selectionError!, cancellationToken);
        }

        var host = await ResolveHostReleaseAsync(channel, targetRuntime, request.HostVersion, cancellationToken);
        if (host is null)
        {
            return await FailAsync("生成安装包失败：没有找到已发布的客户端宿主版本。", cancellationToken);
        }

        var artifactResult = LoadArtifact(channel, host.Version);
        if (!artifactResult.IsSuccess)
        {
            return await FailAsync(artifactResult.Error!, cancellationToken);
        }

        var artifact = artifactResult.Artifact!;
        if (!string.Equals(artifact.Channel, channel, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(artifact.Version, host.Version, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(artifact.TargetRuntime, targetRuntime, StringComparison.OrdinalIgnoreCase))
        {
            return await FailAsync("生成安装包失败：发布记录与安装素材不一致。", cancellationToken);
        }

        var moduleById = artifact.Modules.ToDictionary(
            module => module.ModuleId,
            StringComparer.OrdinalIgnoreCase);
        var artifactCatalog = await artifactCatalogReader.ReadAsync(channel, targetRuntime, cancellationToken);
        var selectedModules = new List<EdgeInstallerArtifactModule>(selections.Count);
        foreach (var selection in selections)
        {
            if (!moduleById.TryGetValue(selection.ModuleId, out var module))
            {
                return await FailAsync(
                    $"生成安装包失败：安装素材中不存在插件 {selection.ModuleId}。",
                    cancellationToken);
            }

            var plugin = await ResolvePluginReleaseAsync(
                module.ModuleId,
                channel,
                module.Version,
                targetRuntime,
                artifactCatalog.PluginReleases,
                cancellationToken);
            if (plugin is null)
            {
                return await FailAsync(
                    $"生成安装包失败：插件 {module.ModuleId} 未登记为已发布版本。",
                    cancellationToken);
            }

            if (!ClientReleaseMapping.IsCompatibleWithHost(plugin, host.Version, host.HostApiVersion, out var issue))
            {
                return await FailAsync($"生成安装包失败：{issue}", cancellationToken);
            }

            selectedModules.Add(module);
        }

        var layoutCheck = ValidateArtifactLayout(artifact, selectedModules);
        if (layoutCheck is not null)
        {
            return await FailAsync(layoutCheck, cancellationToken);
        }

        var devices = await LoadDevicesAsync(selections, cancellationToken);
        if (!devices.IsSuccess)
        {
            return await FailAsync(devices.Error!, cancellationToken);
        }

        var bindings = RotateDeviceSecrets(selections, devices.DevicesById!);
        var bindingBundle = new EdgeBindingBundleDto(
            1,
            publicBaseUrl,
            DateTime.UtcNow,
            bindings);
        Stream packageStream;
        try
        {
            packageStream = BuildInstallerPackage(
                artifact,
                selectedModules,
                bindingBundle,
                targetRuntime);
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

        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);
        if (affected <= 0)
        {
            await packageStream.DisposeAsync();
            return await FailAsync("生成安装包失败：保存设备启动凭据失败。", cancellationToken);
        }

        await WriteSuccessAuditAsync(bindings, devices.DevicesById!, cancellationToken);
        var fileName = BuildDownloadFileName(bindings, host.Version);
        return Result.Success(new EdgeInstallerPackageDto(
            fileName,
            "application/vnd.microsoft.portable-executable",
            packageStream));
    }

    private async Task<ClientHostRelease?> ResolveHostReleaseAsync(
        string channel,
        string targetRuntime,
        string? requestedVersion,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedVersion))
        {
            var requested = requestedVersion.Trim();
            var databaseRelease = await hostReleaseRepository.GetSingleOrDefaultAsync(
                new ClientHostReleaseByIdentitySpec(channel, requested, targetRuntime),
                cancellationToken);
            if (databaseRelease is not null)
            {
                return databaseRelease.Status == ClientReleaseStatus.Published ? databaseRelease : null;
            }

            var artifactCatalog = await artifactCatalogReader.ReadAsync(channel, targetRuntime, cancellationToken);
            return artifactCatalog.HostReleases.FirstOrDefault(release =>
                string.Equals(release.Version, requested, StringComparison.OrdinalIgnoreCase)
                && release.Status == ClientReleaseStatus.Published);
        }

        var databaseReleases = await hostReleaseRepository.GetListAsync(
            new ClientHostReleasesByChannelSpec(channel, targetRuntime, onlyPublished: false, includeArchived: true),
            cancellationToken);
        var snapshot = await artifactCatalogReader.ReadAsync(channel, targetRuntime, cancellationToken);
        var releases = ClientReleaseCatalogMerge.MergeHostReleases(
            databaseReleases,
            snapshot.HostReleases,
            onlyPublished: true);
        return releases
            .OrderByDescending(release => release.Version, VersionComparer)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .FirstOrDefault();
    }

    private async Task<ClientPluginRelease?> ResolvePluginReleaseAsync(
        string moduleId,
        string channel,
        string version,
        string targetRuntime,
        IReadOnlyList<ClientPluginRelease> artifactPluginReleases,
        CancellationToken cancellationToken)
    {
        var databaseRelease = await pluginReleaseRepository.GetSingleOrDefaultAsync(
            new ClientPluginReleaseByIdentitySpec(
                moduleId,
                channel,
                version,
                targetRuntime),
            cancellationToken);
        if (databaseRelease is not null)
        {
            return databaseRelease.Status == ClientReleaseStatus.Published ? databaseRelease : null;
        }

        return artifactPluginReleases.FirstOrDefault(release =>
            string.Equals(release.ModuleId, moduleId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(release.Channel, channel, StringComparison.OrdinalIgnoreCase)
            && string.Equals(release.Version, version, StringComparison.OrdinalIgnoreCase)
            && string.Equals(release.TargetRuntime, targetRuntime, StringComparison.OrdinalIgnoreCase)
            && release.Status == ClientReleaseStatus.Published);
    }

    private ArtifactLoadResult LoadArtifact(string channel, string version)
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
            || manifest.SchemaVersion != 2
            || !IsSafeZipDirectory(manifest.LauncherDirectory)
            || !IsSafeZipDirectory(manifest.HostDirectory)
            || !IsSafeZipDirectory(manifest.PluginsRoot)
            || manifest.Modules.Count == 0)
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装素材清单不完整。");
        }

        if (manifest.Modules.Any(module =>
            string.IsNullOrWhiteSpace(module.ModuleId)
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
        if (!File.Exists(manifest.InstallerStubPath))
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装器外壳缺失。");
        }

        return ArtifactLoadResult.Success(manifest);
    }

    private async Task<DeviceLoadResult> LoadDevicesAsync(
        IReadOnlyList<EdgeBindingSelection> selections,
        CancellationToken cancellationToken)
    {
        var requestedIds = selections.Select(item => item.DeviceId).ToList();
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

    private List<EdgeBindingItemDto> RotateDeviceSecrets(
        IReadOnlyList<EdgeBindingSelection> selections,
        IReadOnlyDictionary<Guid, Device> deviceById)
    {
        var bindings = new List<EdgeBindingItemDto>(selections.Count);
        foreach (var selection in selections)
        {
            var device = deviceById[selection.DeviceId];
            var bootstrapSecret = BootstrapSecretGenerator.Generate();
            device.SetBootstrapSecretHash(BootstrapSecretHasher.Hash(bootstrapSecret));
            deviceRepository.Update(device);
            bindings.Add(new EdgeBindingItemDto(
                selection.ModuleId,
                device.Code,
                bootstrapSecret,
                device.DeviceName,
                device.ProcessId));
        }

        return bindings;
    }

    private static string? ValidateArtifactLayout(
        EdgeInstallerArtifactManifest artifact,
        IReadOnlyCollection<EdgeInstallerArtifactModule> selectedModules)
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

            foreach (var module in selectedModules)
            {
                var pluginDirectory = ResolveArtifactDirectoryPath(
                    artifact,
                    CombineZipPath(artifact.PluginsRoot, module.PluginDirectory));
                if (!Directory.Exists(pluginDirectory) || !Directory.EnumerateFiles(pluginDirectory, "*", SearchOption.AllDirectories).Any())
                {
                    return $"生成安装包失败：安装素材缺少插件目录 {module.ModuleId}。";
                }
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
        CancellationToken cancellationToken)
    {
        foreach (var binding in bindings)
        {
            var device = deviceById.Values.Single(item => item.Code == binding.ClientCode);
            await cacheService.RemoveAsync(CacheKeys.DeviceCode(device.Code), cancellationToken);
            await auditTrailService.TryWriteAsync(
                new AuditTrailEntry(
                    ParseActorUserId(currentUser.Id),
                    currentUser.UserName,
                    "Edge.GenerateInstallerPackage",
                    "Device",
                    device.Id.ToString(),
                    DateTime.UtcNow,
                    true,
                    $"生成客户端首装包时更新设备 {device.DeviceName}（{device.Code}）的启动凭据。",
                    null),
                cancellationToken);
        }
    }

    private static Stream BuildInstallerPackage(
        EdgeInstallerArtifactManifest artifact,
        IReadOnlyCollection<EdgeInstallerArtifactModule> selectedModules,
        EdgeBindingBundleDto bindingBundle,
        string targetRuntime)
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
                selectedModules,
                bindingBundle,
                targetRuntime);

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
        IReadOnlyCollection<EdgeInstallerArtifactModule> selectedModules,
        EdgeBindingBundleDto bindingBundle,
        string targetRuntime)
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

        WritePayloadZip(payloadStream, artifact, selectedModules, bindingBundle, targetRuntime);
        payloadStream.Position = 0;
        payloadStream.CopyTo(packageStream);
        return payloadStream.Length;
    }

    private static void WritePayloadZip(
        Stream packageStream,
        EdgeInstallerArtifactManifest artifact,
        IReadOnlyCollection<EdgeInstallerArtifactModule> selectedModules,
        EdgeBindingBundleDto bindingBundle,
        string targetRuntime)
    {
        using (var target = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var reservedEntries = BuildGeneratedEntryNames(artifact, bindingBundle);
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

            foreach (var module in selectedModules.OrderBy(module => module.ModuleId, StringComparer.OrdinalIgnoreCase))
            {
                AddDirectoryEntries(
                    target,
                    artifact,
                    CombineZipPath(artifact.PluginsRoot, module.PluginDirectory),
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
                BuildHostPluginManifest(artifact, bindingBundle),
                writtenEntries);

            foreach (var binding in bindingBundle.Bindings)
            {
                var module = artifact.Modules.Single(item =>
                    string.Equals(item.ModuleId, binding.ModuleId, StringComparison.OrdinalIgnoreCase));
                WriteJsonEntry(
                    target,
                    CombineZipPath(artifact.PluginsRoot, module.PluginDirectory, PluginBindingFileName),
                    BuildPluginBindingManifest(bindingBundle, binding),
                    writtenEntries);
            }

            WriteJsonEntry(
                target,
                CombineZipPath(artifact.LauncherDirectory, UpdateConfigFileName),
                BuildUpdateConfig(bindingBundle, artifact.Channel, targetRuntime),
                writtenEntries);
        }
    }

    private static HashSet<string> BuildGeneratedEntryNames(
        EdgeInstallerArtifactManifest artifact,
        EdgeBindingBundleDto bindingBundle)
    {
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            CombineZipPath(artifact.LauncherDirectory, BindingFileName),
            CombineZipPath(artifact.LauncherDirectory, HostPluginManifestFileName),
            CombineZipPath(artifact.LauncherDirectory, UpdateConfigFileName)
        };

        foreach (var binding in bindingBundle.Bindings)
        {
            var module = artifact.Modules.Single(item =>
                string.Equals(item.ModuleId, binding.ModuleId, StringComparison.OrdinalIgnoreCase));
            entries.Add(CombineZipPath(artifact.PluginsRoot, module.PluginDirectory, PluginBindingFileName));
        }

        return entries;
    }

    private static EdgeInstallerHostPluginManifest BuildHostPluginManifest(
        EdgeInstallerArtifactManifest artifact,
        EdgeBindingBundleDto bindingBundle)
    {
        var plugins = bindingBundle.Bindings
            .Select(binding =>
            {
                var module = artifact.Modules.Single(item =>
                    string.Equals(item.ModuleId, binding.ModuleId, StringComparison.OrdinalIgnoreCase));
                return new EdgeInstallerHostPluginItem(
                    module.ModuleId,
                    module.DisplayName,
                    module.Version,
                    module.PluginDirectory,
                    binding.ClientCode,
                    binding.DeviceName,
                    binding.ProcessId);
            })
            .ToList();
        return new EdgeInstallerHostPluginManifest(1, bindingBundle.GeneratedAtUtc, plugins);
    }

    private static EdgeInstallerPluginBindingManifest BuildPluginBindingManifest(
        EdgeBindingBundleDto bindingBundle,
        EdgeBindingItemDto binding)
        => new(
            1,
            bindingBundle.BaseUrl,
            bindingBundle.GeneratedAtUtc,
            binding.ModuleId,
            binding.ClientCode,
            binding.BootstrapSecret,
            binding.DeviceName,
            binding.ProcessId);

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

            if (reservedEntries.Contains(entryName) || !writtenEntries.Add(entryName))
            {
                continue;
            }

            var entry = target.CreateEntry(entryName, CompressionLevel.Fastest);
            using var sourceStream = File.OpenRead(filePath);
            using var targetStream = entry.Open();
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

            normalized.Add(new EdgeBindingSelection(moduleId, selection.DeviceId));
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
                ParseActorUserId(currentUser.Id),
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

    private static string NormalizeDefault(string? value, string defaultValue)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? defaultValue : normalized;
    }

    private static Guid? ParseActorUserId(string? rawUserId)
        => Guid.TryParse(rawUserId, out var actorUserId) ? actorUserId : null;

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

    public string HostDirectory { get; set; } = "host";

    public string? HostDirectorySha256 { get; set; }

    public long HostDirectorySize { get; set; }

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
    string PluginDirectory,
    string ClientCode,
    string DeviceName,
    Guid ProcessId);

internal sealed record EdgeInstallerPluginBindingManifest(
    int SchemaVersion,
    string? BaseUrl,
    DateTime GeneratedAtUtc,
    string ModuleId,
    string ClientCode,
    string BootstrapSecret,
    string DeviceName,
    Guid ProcessId);

internal sealed record EdgeInstallerUpdateConfig(
    string? Source,
    string Channel,
    string TargetRuntime);
