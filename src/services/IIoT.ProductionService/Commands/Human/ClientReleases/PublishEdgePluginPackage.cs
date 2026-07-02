using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AuthorizeRequirement(ClientReleasePermissions.Publish)]
public sealed record PublishEdgePluginPackageCommand(
    Stream PackageStream,
    long? ContentLength,
    string? ContentType,
    string? SourceIp)
    : IHumanCommand<Result<EdgePluginPackagePublishResultDto>>;

public sealed record EdgePluginPackagePublishResultDto(
    string ModuleId,
    string DisplayName,
    string Channel,
    string Version,
    string HostApiVersion,
    string MinHostVersion,
    string MaxHostVersion,
    string TargetRuntime,
    string? TargetFramework,
    string DownloadUrl,
    string Sha256,
    long PackageSize,
    double UploadSeconds,
    int UploadRateLimitMbps,
    IReadOnlyList<string> VerificationUrls,
    string? CleanupWarning);

public sealed class PublishEdgePluginPackageHandler(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    IOptions<EdgeReleaseUploadOptions> uploadOptions,
    IRepository<ClientReleaseComponent> componentRepository,
    IClientReleaseRetentionService retentionService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<PublishEdgePluginPackageCommand, Result<EdgePluginPackagePublishResultDto>>
{
    private const string ManifestFileName = "plugin-release.json";
    public const string UploadInProgressMessage = "已有 Edge 发布上传正在执行，请稍后重试。";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-fA-F]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] ForbiddenFileNameSuffixes =
    [
        "launcher.accounts.json",
        "launcher.update.json",
        ".db",
        ".sqlite",
        ".db-wal",
        ".db-shm",
        "crash.log"
    ];

    public async Task<Result<EdgePluginPackagePublishResultDto>> Handle(
        PublishEdgePluginPackageCommand request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var edgeRoot = ResolveEdgeUpdatesRoot();
        Directory.CreateDirectory(edgeRoot);
        var lockPath = Path.Combine(edgeRoot, ".edge-release-upload.lock");
        await using var uploadLock = TryAcquireUploadLock(lockPath);
        if (uploadLock is null)
        {
            return Result.Invalid(UploadInProgressMessage);
        }

        var stagingRoot = Path.Combine(
            edgeRoot,
            uploadOptions.Value.StagingDirectoryName,
            "edge-plugin-packages",
            Guid.NewGuid().ToString("N"));
        var wrapperPath = Path.Combine(stagingRoot, "plugin-package.zip");
        var extractRoot = Path.Combine(stagingRoot, "extracted");
        string? packageTargetDirectory = null;
        var databaseSaved = false;

        try
        {
            Directory.CreateDirectory(stagingRoot);
            var copiedBytes = await CopyWithLimitAsync(request.PackageStream, wrapperPath, cancellationToken);
            if (request.ContentLength is { } declaredLength && declaredLength != copiedBytes)
            {
                return await FailAsync("Edge 插件发布包上传大小与请求头不一致。", cancellationToken);
            }

            AssertFreeDiskSpace(edgeRoot, copiedBytes);
            ExtractZip(wrapperPath, extractRoot, "Edge 插件发布包");
            var loadResult = await LoadAndValidateAsync(extractRoot, cancellationToken);
            if (!loadResult.IsSuccess)
            {
                return await FailAsync(loadResult.Error!, cancellationToken);
            }

            var metadata = loadResult.Metadata!;
            var packagePath = loadResult.PackagePath!;
            var component = await componentRepository.GetSingleOrDefaultAsync(
                new ClientReleaseComponentByIdentitySpec(
                    ClientReleaseComponentKind.Plugin,
                    metadata.ModuleId,
                    metadata.Channel,
                    metadata.TargetRuntime),
                cancellationToken);
            if (component?.FindVersion(metadata.Version) is not null)
            {
                return await FailAsync(
                    $"插件版本已存在，拒绝重复发布: {metadata.ModuleId}/{metadata.Channel}/{metadata.Version}/{metadata.TargetRuntime}。",
                    cancellationToken);
            }

            packageTargetDirectory = Path.Combine(
                edgeRoot,
                "plugins",
                metadata.Channel,
                EscapeFileSystemSegment(metadata.ModuleId),
                metadata.Version);
            if (Directory.Exists(packageTargetDirectory))
            {
                return await FailAsync(
                    $"插件版本目录已存在，拒绝覆盖: {metadata.Channel}/{metadata.ModuleId}/{metadata.Version}。",
                    cancellationToken);
            }

            Directory.CreateDirectory(packageTargetDirectory);
            var targetPackagePath = Path.Combine(packageTargetDirectory, metadata.PackageFileName);
            File.Move(packagePath, targetPackagePath);
            var downloadUrl = BuildPluginDownloadUrl(
                metadata.Channel,
                metadata.ModuleId,
                metadata.Version,
                metadata.PackageFileName);
            var displayName = string.IsNullOrWhiteSpace(metadata.DisplayName) ? metadata.ModuleId : metadata.DisplayName;
            if (component is null)
            {
                component = ClientReleaseComponent.CreatePlugin(
                    metadata.ModuleId,
                    displayName,
                    metadata.Description,
                    metadata.IconKind,
                    metadata.AccentColor,
                    metadata.Channel,
                    metadata.TargetRuntime);
                componentRepository.Add(component);
            }
            else
            {
                component.UpdatePluginMetadata(
                    displayName,
                    metadata.Description,
                    metadata.IconKind,
                    metadata.AccentColor);
            }

            component.UpsertPluginVersion(
                metadata.Version,
                metadata.HostApiVersion,
                metadata.MinHostVersion,
                metadata.MaxHostVersion,
                metadata.TargetFramework,
                downloadUrl,
                metadata.Sha256,
                metadata.PackageSize,
                metadata.ReleaseNotes,
                JsonSerializer.Serialize(metadata.Dependencies ?? [], JsonOptions),
                ClientReleaseStatus.Published,
                metadata.Signature,
                string.IsNullOrWhiteSpace(metadata.Publisher) ? "IIoT" : metadata.Publisher,
                metadata.CreatedAtUtc,
                ClientReleaseArtifactBuilder.FromPluginDownloadUrl(
                    downloadUrl,
                    metadata.Channel,
                    metadata.ModuleId,
                    metadata.Version,
                    metadata.Sha256,
                    metadata.PackageSize));
            await componentRepository.SaveChangesAsync(cancellationToken);
            databaseSaved = true;

            string? cleanupWarning = null;
            try
            {
                await retentionService.ApplyPluginPolicyAsync(
                    metadata.ModuleId,
                    metadata.Channel,
                    metadata.TargetRuntime,
                    CancellationToken.None);
                await CleanupArchivedPluginFilesAsync(edgeRoot, metadata.Channel, metadata.TargetRuntime, CancellationToken.None);
            }
            catch (Exception ex)
            {
                cleanupWarning = $"插件发布成功，但保留/清理旧版本未完成：{ex.Message}";
            }

            stopwatch.Stop();
            var result = new EdgePluginPackagePublishResultDto(
                metadata.ModuleId,
                string.IsNullOrWhiteSpace(metadata.DisplayName) ? metadata.ModuleId : metadata.DisplayName,
                metadata.Channel,
                metadata.Version,
                metadata.HostApiVersion,
                metadata.MinHostVersion,
                metadata.MaxHostVersion,
                metadata.TargetRuntime,
                metadata.TargetFramework,
                downloadUrl,
                metadata.Sha256,
                metadata.PackageSize,
                stopwatch.Elapsed.TotalSeconds,
                uploadOptions.Value.MaxUploadMbps,
                [downloadUrl],
                cleanupWarning);
            await WriteAuditAsync(result, request.SourceIp, succeeded: true, cleanupWarning, CancellationToken.None);
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            if (!databaseSaved && packageTargetDirectory is not null)
            {
                TryDeleteDirectory(packageTargetDirectory);
            }

            return await FailAsync(FormatPublishFailure(ex), CancellationToken.None);
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }

        async Task<Result<EdgePluginPackagePublishResultDto>> FailAsync(
            string message,
            CancellationToken token)
        {
            await WriteAuditAsync(null, request.SourceIp, succeeded: false, message, token);
            return Result.Invalid(message);
        }
    }

    private string ResolveEdgeUpdatesRoot()
    {
        var installerRoot = Path.GetFullPath(artifactOptions.Value.RootPath);
        var parent = Directory.GetParent(installerRoot);
        if (parent is null)
        {
            throw new InvalidOperationException("EdgeInstallerArtifacts:RootPath 必须位于 edge-updates/installers 下。");
        }

        return parent.FullName;
    }

    private static FileStream? TryAcquireUploadLock(string lockPath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
            return new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task<long> CopyWithLimitAsync(
        Stream source,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var limitBytes = uploadOptions.Value.MaxBundleBytes;
        var maxBytesPerSecond = Math.Max(1L, uploadOptions.Value.MaxUploadMbps) * 1024L * 1024L / 8L;
        var window = Stopwatch.StartNew();
        var windowBytes = 0L;
        var totalBytes = 0L;
        var buffer = new byte[1024 * 1024];

        await using var target = new FileStream(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            buffer.Length,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            windowBytes += read;
            if (totalBytes > limitBytes)
            {
                throw new InvalidDataException($"Edge 插件发布包超过最大限制 {limitBytes} 字节。");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            if (windowBytes >= maxBytesPerSecond)
            {
                var remaining = TimeSpan.FromSeconds(1) - window.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken);
                }

                window.Restart();
                windowBytes = 0;
            }
        }

        await target.FlushAsync(cancellationToken);
        return totalBytes;
    }

    private async Task<PluginPackageValidationResult> LoadAndValidateAsync(
        string extractRoot,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(extractRoot, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return PluginPackageValidationResult.Fail($"Edge 插件发布包缺少 {ManifestFileName}。");
        }

        PluginPackageReleaseManifest? manifest;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<PluginPackageReleaseManifest>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return PluginPackageValidationResult.Fail("Edge 插件发布包 manifest 无法解析。");
        }

        if (manifest is null)
        {
            return PluginPackageValidationResult.Fail("Edge 插件发布包 manifest 为空。");
        }

        var basicError = ValidateManifestBasics(manifest);
        if (basicError is not null)
        {
            return PluginPackageValidationResult.Fail(basicError);
        }

        var packagePath = Directory
            .EnumerateFiles(extractRoot, manifest.PackageFileName, SearchOption.AllDirectories)
            .SingleOrDefault();
        if (packagePath is null)
        {
            return PluginPackageValidationResult.Fail("Edge 插件发布包缺少插件 zip。");
        }

        if (!string.Equals(HashFile(packagePath), manifest.Sha256, StringComparison.OrdinalIgnoreCase)
            || new FileInfo(packagePath).Length != manifest.PackageSize)
        {
            return PluginPackageValidationResult.Fail("Edge 插件 zip 的 sha256 或 size 与 manifest 不一致。");
        }

        var packageError = ValidatePluginPackageZip(packagePath, manifest);
        return packageError is null
            ? PluginPackageValidationResult.Success(manifest, packagePath)
            : PluginPackageValidationResult.Fail(packageError);
    }

    private static string? ValidateManifestBasics(PluginPackageReleaseManifest manifest)
    {
        if (manifest.PackageSchemaVersion != 1)
        {
            return "Edge 插件发布包 schemaVersion 不受支持。";
        }

        if (!string.Equals(manifest.Channel, "stable", StringComparison.OrdinalIgnoreCase))
        {
            return "生产服务器只允许发布 stable 插件渠道。";
        }

        if (string.IsNullOrWhiteSpace(manifest.ModuleId)
            || string.IsNullOrWhiteSpace(manifest.DisplayName)
            || string.IsNullOrWhiteSpace(manifest.Version)
            || string.IsNullOrWhiteSpace(manifest.HostApiVersion)
            || string.IsNullOrWhiteSpace(manifest.MinHostVersion)
            || string.IsNullOrWhiteSpace(manifest.MaxHostVersion)
            || string.IsNullOrWhiteSpace(manifest.TargetRuntime)
            || string.IsNullOrWhiteSpace(manifest.PackageFileName)
            || string.IsNullOrWhiteSpace(manifest.ReleaseNotes))
        {
            return "Edge 插件发布包 manifest 不完整。";
        }

        if (!manifest.PackageFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || !IsSafeRelativeFileName(manifest.PackageFileName))
        {
            return "Edge 插件发布包文件名非法。";
        }

        if (!Sha256Pattern.IsMatch(manifest.Sha256 ?? string.Empty) || manifest.PackageSize <= 0)
        {
            return "Edge 插件发布包 sha256 或 size 非法。";
        }

        return null;
    }

    private static string? ValidatePluginPackageZip(
        string packagePath,
        PluginPackageReleaseManifest manifest)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var normalized = NormalizeZipEntryPath(entry.FullName, "Edge 插件 zip");
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            var lower = normalized.ToLowerInvariant();
            if (lower.Contains("/diagnostics/logs/", StringComparison.Ordinal)
                || lower.Contains("/logs/", StringComparison.Ordinal)
                || lower.Contains("/recipe/", StringComparison.Ordinal)
                || lower.Contains("/excel/", StringComparison.Ordinal)
                || ForbiddenFileNameSuffixes.Any(suffix => lower.EndsWith(suffix, StringComparison.Ordinal))
                || lower.Contains("bootstrapsecret", StringComparison.Ordinal)
                || lower.Contains("bootstrap-secret", StringComparison.Ordinal))
            {
                return $"Edge 插件 zip 包含禁止上传的现场数据或密钥文件: {normalized}";
            }

            var appSettingsSecret = TryFindCloudApiSecretInAppSettings(entry, normalized);
            if (appSettingsSecret is not null)
            {
                return $"Edge 插件 zip 配置包含真实 CloudApi:{appSettingsSecret}: {normalized}";
            }
        }

        var manifestEntry = archive.Entries
            .FirstOrDefault(entry => entry.FullName.Equals("plugin.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null)
        {
            return "Edge 插件 zip 缺少 plugin.json。";
        }

        PluginRuntimeManifest? pluginManifest;
        try
        {
            using var reader = new StreamReader(manifestEntry.Open());
            pluginManifest = JsonSerializer.Deserialize<PluginRuntimeManifest>(
                reader.ReadToEnd(),
                JsonOptions);
        }
        catch (JsonException)
        {
            return "Edge 插件 zip 的 plugin.json 无法解析。";
        }

        if (pluginManifest is null
            || !string.Equals(pluginManifest.ModuleId, manifest.ModuleId, StringComparison.Ordinal)
            || !string.Equals(pluginManifest.Version, manifest.Version, StringComparison.Ordinal)
            || !string.Equals(pluginManifest.HostApiVersion, manifest.HostApiVersion, StringComparison.Ordinal)
            || !string.Equals(pluginManifest.MinHostVersion, manifest.MinHostVersion, StringComparison.Ordinal)
            || !string.Equals(pluginManifest.MaxHostVersion, manifest.MaxHostVersion, StringComparison.Ordinal))
        {
            return "Edge 插件 zip 的 plugin.json 与发布 manifest 不一致。";
        }

        if (string.IsNullOrWhiteSpace(pluginManifest.EntryAssembly)
            || archive.Entries.All(entry => !entry.FullName.Equals(pluginManifest.EntryAssembly, StringComparison.OrdinalIgnoreCase)))
        {
            return "Edge 插件 zip 缺少入口程序集。";
        }

        return null;
    }

    private static string? TryFindCloudApiSecretInAppSettings(ZipArchiveEntry entry, string normalizedPath)
    {
        var fileName = Path.GetFileName(normalizedPath);
        if (!fileName.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(entry.Open());
        }
        catch (JsonException)
        {
            throw new InvalidDataException($"Edge 插件 zip 的配置文件无法解析: {normalizedPath}");
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("CloudApi", out var cloudApi))
            {
                return null;
            }

            foreach (var key in new[] { "ClientCode", "BootstrapSecret" })
            {
                if (cloudApi.TryGetProperty(key, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return key;
                }
            }

            return null;
        }
    }

    private async Task CleanupArchivedPluginFilesAsync(
        string edgeRoot,
        string channel,
        string targetRuntime,
        CancellationToken cancellationToken)
    {
        var components = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(channel, targetRuntime, onlyPublished: false, includeArchived: true),
            cancellationToken);

        foreach (var component in components.Where(component => component.ComponentKind == ClientReleaseComponentKind.Plugin))
        {
            foreach (var release in component.Versions.Where(release => release.Status == ClientReleaseStatus.Archived))
            {
                var directory = Path.Combine(
                    edgeRoot,
                    "plugins",
                    component.Channel,
                    EscapeFileSystemSegment(component.ComponentKey),
                    release.Version);
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }

    private async Task WriteAuditAsync(
        EdgePluginPackagePublishResultDto? result,
        string? sourceIp,
        bool succeeded,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        var target = result is null
            ? "edge-plugin-package-upload"
            : $"{result.Channel}/{result.ModuleId}/{result.Version}";
        var summary = succeeded && result is not null
            ? $"Published Edge plugin {target} via HTTP upload from {sourceIp ?? "unknown client"}."
            : $"Failed to publish Edge plugin package via HTTP upload from {sourceIp ?? "unknown client"}.";

        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "ClientRelease.PublishPlugin",
                "EdgePluginPackage",
                target,
                DateTime.UtcNow,
                succeeded,
                summary,
                failureReason),
            cancellationToken);
    }

    private static void ExtractZip(string zipPath, string extractRoot, string label)
    {
        Directory.CreateDirectory(extractRoot);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var relativePath = NormalizeZipEntryPath(entry.FullName, label);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(extractRoot, relativePath));
            if (!IsChildPath(extractRoot, targetPath))
            {
                throw new InvalidDataException($"{label} 包含非法路径: {entry.FullName}");
            }

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: false);
        }

        foreach (var fileSystemInfo in Directory.EnumerateFileSystemEntries(
            extractRoot,
            "*",
            SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(fileSystemInfo) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"{label} 不允许包含符号链接或重解析点。");
            }
        }
    }

    private static string NormalizeZipEntryPath(string path, string label)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new InvalidDataException($"{label} 包含非法 zip 路径: {path}");
        }

        return normalized;
    }

    private static bool IsSafeRelativeFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains('/')
            || path.Contains('\\')
            || path.Contains(':')
            || Path.GetFileName(path) != path)
        {
            return false;
        }

        return path.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static bool IsChildPath(string parentPath, string childPath)
    {
        var normalizedParent = Path.GetFullPath(parentPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedChild = Path.GetFullPath(childPath);
        return normalizedChild.StartsWith(
            normalizedParent + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);
    }

    private static string HashFile(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void AssertFreeDiskSpace(string rootPath, long packageBytes)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(rootPath))!);
        var required = packageBytes * 2;
        if (drive.AvailableFreeSpace < required)
        {
            throw new InvalidDataException(
                $"Edge 插件发布磁盘剩余空间不足，至少需要 {required} 字节。");
        }
    }

    private static string BuildPluginDownloadUrl(
        string channel,
        string moduleId,
        string version,
        string packageFileName)
        => $"/edge-updates/plugins/{Uri.EscapeDataString(channel)}/{Uri.EscapeDataString(moduleId)}/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(packageFileName)}";

    private static string EscapeFileSystemSegment(string value)
        => Uri.EscapeDataString(value.Trim());

    private static Guid? ParseActorUserId(string? userId)
        => Guid.TryParse(userId, out var parsed) ? parsed : null;

    private static string FormatPublishFailure(Exception ex)
        => ex switch
        {
            InvalidDataException => ex.Message,
            IOException => $"Edge 插件发布包处理失败：{ex.Message}",
            OperationCanceledException => "Edge 插件发布上传已取消。",
            _ => $"Edge 插件发布失败：{ex.Message}"
        };

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

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

    private sealed record PluginPackageValidationResult(
        bool IsSuccess,
        PluginPackageReleaseManifest? Metadata,
        string? PackagePath,
        string? Error)
    {
        public static PluginPackageValidationResult Success(
            PluginPackageReleaseManifest metadata,
            string packagePath)
            => new(true, metadata, packagePath, null);

        public static PluginPackageValidationResult Fail(string error)
            => new(false, null, null, error);
    }

    private sealed class PluginPackageReleaseManifest
    {
        public int PackageSchemaVersion { get; set; }

        public string Channel { get; set; } = string.Empty;

        public string ModuleId { get; set; } = string.Empty;

        public string ProcessType { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? IconKind { get; set; }

        public string? AccentColor { get; set; }

        public string Version { get; set; } = string.Empty;

        public string HostApiVersion { get; set; } = string.Empty;

        public string MinHostVersion { get; set; } = string.Empty;

        public string MaxHostVersion { get; set; } = string.Empty;

        public IReadOnlyList<string>? Dependencies { get; set; }

        public string TargetRuntime { get; set; } = string.Empty;

        public string? TargetFramework { get; set; }

        public string PackageFileName { get; set; } = string.Empty;

        public long PackageSize { get; set; }

        public string Sha256 { get; set; } = string.Empty;

        public string? ReleaseNotes { get; set; }

        public string? Signature { get; set; }

        public string? Publisher { get; set; }

        public DateTime? CreatedAtUtc { get; set; }
    }

    private sealed class PluginRuntimeManifest
    {
        public string ModuleId { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public string HostApiVersion { get; set; } = string.Empty;

        public string MinHostVersion { get; set; } = string.Empty;

        public string MaxHostVersion { get; set; } = string.Empty;

        public string EntryAssembly { get; set; } = string.Empty;
    }
}
