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
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AuthorizeRequirement(ClientReleasePermissions.GenerateInstaller)]
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
    IReadRepository<ClientReleaseComponent> componentRepository,
    IAuditTrailService auditTrailService,
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

        var artifactResult = LoadArtifact(channel, host.Version.Version);
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
                cancellationToken);
            if (!pluginResolution.IsSuccess)
            {
                return await FailAsync(pluginResolution.Error!, cancellationToken);
            }

            var packageResult = LoadPluginPackage(pluginResolution.Selection!);
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
                selectedPlugins,
                bindingBundle,
                targetRuntime);
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

        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);
        if (affected <= 0)
        {
            await packageStream.DisposeAsync();
            return await FailAsync("生成安装包失败：保存设备启动凭据失败。", cancellationToken);
        }

        await WriteSuccessAuditAsync(bindings, devices.DevicesById!, cancellationToken);
        var fileName = BuildDownloadFileName(bindings, host.Version.Version);
        return Result.Success(new EdgeInstallerPackageDto(
            fileName,
            "application/vnd.microsoft.portable-executable",
            packageStream));
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

        var compatible = published.FirstOrDefault(release =>
            ClientReleaseMapping.IsCompatibleWithHost(
                release,
                hostVersion,
                hostApiVersion,
                out _));
        return compatible is null
            ? PluginReleaseResolution.Fail(
                $"生成安装包失败：插件 {moduleId} 没有与宿主 {hostVersion} 兼容的已发布版本。")
            : PluginReleaseResolution.Success(new PluginReleaseSelection(component, compatible));
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
            || manifest.Modules is null)
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装素材清单不完整。");
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
        if (!File.Exists(manifest.InstallerStubPath))
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装器外壳缺失。");
        }

        return ArtifactLoadResult.Success(manifest);
    }

    private PluginPackageLoadResult LoadPluginPackage(PluginReleaseSelection selection)
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

        var archiveError = ValidatePluginPackageArchive(packagePath, selection);
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
            moduleId,
            packagePath));
    }

    private static string? ValidatePluginPackageArchive(
        string packagePath,
        PluginReleaseSelection selection)
    {
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ZipArchiveEntry? pluginManifestEntry = null;
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

                if (!IsSafeZipEntry(normalized) || !entries.Add(normalized))
                {
                    return $"生成安装包失败：插件 {selection.Component.ComponentKey} 的安装包包含非法或重复路径。";
                }

                if (string.Equals(normalized, "plugin.json", StringComparison.OrdinalIgnoreCase))
                {
                    pluginManifestEntry = entry;
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
                || !entries.Contains(entryAssembly))
            {
                return $"生成安装包失败：插件 {selection.Component.ComponentKey} 的 plugin.json 与发布记录不一致。";
            }

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
        CancellationToken cancellationToken)
    {
        foreach (var binding in bindings)
        {
            var device = deviceById.Values.Single(item => item.Code == binding.ClientCode);
            await auditTrailService.TryWriteAsync(
                new AuditTrailEntry(
                    ClientReleaseAuditActor.ParseId(currentUser.Id),
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
        IReadOnlyCollection<EdgeInstallerPluginPackage> selectedPlugins,
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
                selectedPlugins,
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
        IReadOnlyCollection<EdgeInstallerPluginPackage> selectedPlugins,
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

        WritePayloadZip(payloadStream, artifact, selectedPlugins, bindingBundle, targetRuntime);
        payloadStream.Position = 0;
        payloadStream.CopyTo(packageStream);
        return payloadStream.Length;
    }

    private static void WritePayloadZip(
        Stream packageStream,
        EdgeInstallerArtifactManifest artifact,
        IReadOnlyCollection<EdgeInstallerPluginPackage> selectedPlugins,
        EdgeBindingBundleDto bindingBundle,
        string targetRuntime)
    {
        using (var target = new ZipArchive(packageStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var reservedEntries = BuildGeneratedEntryNames(artifact, selectedPlugins, bindingBundle);
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

            foreach (var binding in bindingBundle.Bindings)
            {
                var plugin = selectedPlugins.Single(item =>
                    string.Equals(item.ModuleId, binding.ModuleId, StringComparison.OrdinalIgnoreCase));
                WriteJsonEntry(
                    target,
                    CombineZipPath(artifact.PluginsRoot, plugin.PluginDirectory, PluginBindingFileName),
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
        IReadOnlyCollection<EdgeInstallerPluginPackage> selectedPlugins,
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
            var plugin = selectedPlugins.Single(item =>
                string.Equals(item.ModuleId, binding.ModuleId, StringComparison.OrdinalIgnoreCase));
            entries.Add(CombineZipPath(
                artifact.PluginsRoot,
                plugin.PluginDirectory,
                PluginBindingFileName));
        }

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
                    plugin.PluginDirectory,
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

            if (reservedEntries.Contains(entryName))
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

    private static string NormalizeDefault(string? value, string defaultValue)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? defaultValue : normalized;
    }

    private sealed record HostReleaseSelection(
        ClientReleaseComponent Component,
        ClientReleaseVersion Version);

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
        string PluginDirectory,
        string PackagePath);

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
