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
    byte[] Content);

public sealed class GenerateEdgeInstallerPackageHandler(
    ICurrentUser currentUser,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IRepository<Device> deviceRepository,
    IReadRepository<ClientHostRelease> hostReleaseRepository,
    IReadRepository<ClientPluginRelease> pluginReleaseRepository,
    ICacheService cacheService,
    IAuditTrailService auditTrailService,
    IOptions<EdgeInstallerArtifactOptions> options)
    : ICommandHandler<GenerateEdgeInstallerPackageCommand, Result<EdgeInstallerPackageDto>>
{
    private static readonly byte[] InstallerMagic = "IIOTEDG1"u8.ToArray();
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
        var selectedRuntimeDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in selections)
        {
            if (!moduleById.TryGetValue(selection.ModuleId, out var module))
            {
                return await FailAsync(
                    $"生成安装包失败：安装素材中不存在插件 {selection.ModuleId}。",
                    cancellationToken);
            }

            var plugin = await pluginReleaseRepository.GetSingleOrDefaultAsync(
                new ClientPluginReleaseByIdentitySpec(
                    module.ModuleId,
                    channel,
                    module.Version,
                    targetRuntime),
                cancellationToken);
            if (plugin is null || plugin.Status != ClientReleaseStatus.Published)
            {
                return await FailAsync(
                    $"生成安装包失败：插件 {module.ModuleId} 未登记为已发布版本。",
                    cancellationToken);
            }

            if (!ClientReleaseMapping.IsCompatibleWithHost(plugin, host.Version, host.HostApiVersion, out var issue))
            {
                return await FailAsync($"生成安装包失败：{issue}", cancellationToken);
            }

            selectedRuntimeDirectories.Add(module.RuntimeDirectory);
        }

        var layoutCheck = ValidateArtifactLayout(artifact, selectedRuntimeDirectories);
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
        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);
        if (affected <= 0)
        {
            return await FailAsync("生成安装包失败：保存设备启动密钥失败。", cancellationToken);
        }

        await WriteSuccessAuditAsync(bindings, devices.DevicesById!, cancellationToken);
        var bindingBundle = new EdgeBindingBundleDto(
            1,
            NormalizeOptional(request.BaseUrl),
            DateTime.UtcNow,
            bindings);
        var packageBytes = BuildInstallerPackage(
            artifact,
            selectedRuntimeDirectories,
            bindingBundle);
        var fileName = BuildDownloadFileName(bindings, host.Version);
        return Result.Success(new EdgeInstallerPackageDto(
            fileName,
            "application/vnd.microsoft.portable-executable",
            packageBytes));
    }

    private async Task<ClientHostRelease?> ResolveHostReleaseAsync(
        string channel,
        string targetRuntime,
        string? requestedVersion,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedVersion))
        {
            var release = await hostReleaseRepository.GetSingleOrDefaultAsync(
                new ClientHostReleaseByIdentitySpec(channel, requestedVersion, targetRuntime),
                cancellationToken);
            return release?.Status == ClientReleaseStatus.Published ? release : null;
        }

        var releases = await hostReleaseRepository.GetListAsync(
            new ClientHostReleasesByChannelSpec(channel, targetRuntime, onlyPublished: true),
            cancellationToken);
        return releases
            .OrderByDescending(release => release.Version, VersionComparer)
            .ThenByDescending(release => release.PublishedAtUtc ?? release.CreatedAtUtc)
            .FirstOrDefault();
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
            || string.IsNullOrWhiteSpace(manifest.LayoutZipFile)
            || !IsSafeZipDirectory(manifest.LauncherDirectory)
            || manifest.Modules.Count == 0)
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装素材清单不完整。");
        }

        if (manifest.Modules.Any(module =>
            string.IsNullOrWhiteSpace(module.ModuleId)
            || string.IsNullOrWhiteSpace(module.Version)
            || !IsSafeZipDirectory(module.RuntimeDirectory)))
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装素材清单包含非法 runtime 映射。");
        }

        manifest.RootPath = artifactRoot;
        manifest.InstallerStubPath = Path.Combine(artifactRoot, manifest.InstallerStubFile);
        manifest.LayoutZipPath = Path.Combine(artifactRoot, manifest.LayoutZipFile);
        if (!File.Exists(manifest.InstallerStubPath) || !File.Exists(manifest.LayoutZipPath))
        {
            return ArtifactLoadResult.Fail("生成安装包失败：安装器外壳或布局包缺失。");
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
        IReadOnlyCollection<string> selectedRuntimeDirectories)
    {
        try
        {
            using var source = ZipFile.OpenRead(artifact.LayoutZipPath);
            var hasLauncher = false;
            var runtimeHits = selectedRuntimeDirectories.ToDictionary(
                runtime => runtime,
                _ => false,
                StringComparer.OrdinalIgnoreCase);

            foreach (var entry in source.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var normalized = entry.FullName.Replace('\\', '/');
                if (!IsSafeZipEntry(normalized))
                {
                    return "生成安装包失败：安装素材布局包包含非法路径。";
                }

                if (normalized.StartsWith($"{artifact.LauncherDirectory}/", StringComparison.OrdinalIgnoreCase))
                {
                    hasLauncher = true;
                }

                foreach (var runtime in selectedRuntimeDirectories)
                {
                    if (normalized.StartsWith($"{runtime}/", StringComparison.OrdinalIgnoreCase))
                    {
                        runtimeHits[runtime] = true;
                    }
                }
            }

            if (!hasLauncher)
            {
                return "生成安装包失败：安装素材缺少 launcher 运行目录。";
            }

            var missingRuntime = runtimeHits.FirstOrDefault(item => !item.Value).Key;
            return string.IsNullOrWhiteSpace(missingRuntime)
                ? null
                : $"生成安装包失败：安装素材缺少 runtime 目录 {missingRuntime}。";
        }
        catch (InvalidDataException)
        {
            return "生成安装包失败：安装素材布局包无法读取。";
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
                    "Device.RotateBootstrapSecret",
                    "Device",
                    device.Id.ToString(),
                    DateTime.UtcNow,
                    true,
                    $"生成客户端安装包时轮换设备 {device.DeviceName}（{device.Code}）的启动密钥。",
                    null),
                cancellationToken);
        }
    }

    private byte[] BuildInstallerPackage(
        EdgeInstallerArtifactManifest artifact,
        IReadOnlyCollection<string> selectedRuntimeDirectories,
        EdgeBindingBundleDto bindingBundle)
    {
        var payload = BuildPayloadZip(artifact, selectedRuntimeDirectories, bindingBundle);
        var stub = File.ReadAllBytes(artifact.InstallerStubPath);
        var output = new byte[stub.Length + payload.Length + 16];

        Buffer.BlockCopy(stub, 0, output, 0, stub.Length);
        Buffer.BlockCopy(payload, 0, output, stub.Length, payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(stub.Length + payload.Length, 8), payload.Length);
        InstallerMagic.CopyTo(output.AsSpan(stub.Length + payload.Length + 8, 8));
        return output;
    }

    private static byte[] BuildPayloadZip(
        EdgeInstallerArtifactManifest artifact,
        IReadOnlyCollection<string> selectedRuntimeDirectories,
        EdgeBindingBundleDto bindingBundle)
    {
        using var source = ZipFile.OpenRead(artifact.LayoutZipPath);
        using var payloadStream = new MemoryStream();
        using (var target = new ZipArchive(payloadStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) || !IsAllowedEntry(entry.FullName, artifact, selectedRuntimeDirectories))
                {
                    continue;
                }

                CopyZipEntry(entry, target);
            }

            var bindingEntry = target.CreateEntry(
                $"{artifact.LauncherDirectory}/iiot-binding.json",
                CompressionLevel.Optimal);
            using var writer = new StreamWriter(bindingEntry.Open(), new UTF8Encoding(false));
            writer.Write(JsonSerializer.Serialize(bindingBundle, JsonOptions));
        }

        return payloadStream.ToArray();
    }

    private static bool IsAllowedEntry(
        string entryName,
        EdgeInstallerArtifactManifest artifact,
        IReadOnlyCollection<string> selectedRuntimeDirectories)
    {
        var normalized = entryName.Replace('\\', '/');
        if (!IsSafeZipEntry(normalized))
        {
            return false;
        }

        if (normalized.StartsWith($"{artifact.LauncherDirectory}/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return selectedRuntimeDirectories.Any(runtime =>
            normalized.StartsWith($"{runtime}/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafeZipDirectory(string directory)
    {
        var normalized = directory.Replace('\\', '/').Trim('/');
        return !string.IsNullOrWhiteSpace(normalized)
            && !normalized.StartsWith("/", StringComparison.Ordinal)
            && !normalized.Split('/').Any(part => part is "." or ".." or "");
    }

    private static bool IsSafeZipEntry(string entryName)
    {
        return !entryName.StartsWith("/", StringComparison.Ordinal)
            && !entryName.Split('/').Any(part => part is "." or "..");
    }

    private static void CopyZipEntry(ZipArchiveEntry sourceEntry, ZipArchive targetArchive)
    {
        var targetEntry = targetArchive.CreateEntry(sourceEntry.FullName.Replace('\\', '/'), CompressionLevel.Optimal);
        using var sourceStream = sourceEntry.Open();
        using var targetStream = targetEntry.Open();
        sourceStream.CopyTo(targetStream);
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
                error = $"生成安装包失败：插件 {moduleId} 未选择设备唯一码。";
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

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
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

    public string InstallerStubFile { get; set; } = string.Empty;

    public string LayoutZipFile { get; set; } = string.Empty;

    public string LauncherDirectory { get; set; } = "launcher";

    public List<EdgeInstallerArtifactModule> Modules { get; set; } = [];

    public string RootPath { get; set; } = string.Empty;

    public string InstallerStubPath { get; set; } = string.Empty;

    public string LayoutZipPath { get; set; } = string.Empty;
}

internal sealed class EdgeInstallerArtifactModule
{
    public string ModuleId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string HostApiVersion { get; set; } = string.Empty;

    public string MinHostVersion { get; set; } = string.Empty;

    public string MaxHostVersion { get; set; } = string.Empty;

    public string RuntimeId { get; set; } = string.Empty;

    public string RuntimeDirectory { get; set; } = string.Empty;
}
