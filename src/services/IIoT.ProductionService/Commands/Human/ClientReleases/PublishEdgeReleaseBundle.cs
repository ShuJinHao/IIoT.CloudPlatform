using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
public sealed record PublishEdgeReleaseBundleCommand(
    Stream BundleStream,
    long? ContentLength,
    string? ContentType,
    string? SourceIp)
    : IHumanCommand<Result<EdgeReleaseBundlePublishResultDto>>;

public sealed record EdgeReleaseBundlePublishResultDto(
    string Channel,
    string Version,
    string? SourceCommit,
    string? PreviousSourceCommit,
    string? ReleaseNotes,
    IReadOnlyList<string> ChangedCommits,
    IReadOnlyList<string> Components,
    long BundleSize,
    double UploadSeconds,
    int UploadRateLimitMbps,
    string InstallerPath,
    string VelopackPath,
    IReadOnlyList<string> ArchivedVersions,
    IReadOnlyList<string> DeletedInstallerVersions,
    IReadOnlyList<string> DeletedVelopackFiles,
    bool CleanupSucceeded,
    string? CleanupWarning,
    IReadOnlyList<string> VerificationUrls);

public sealed class PublishEdgeReleaseBundleHandler(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    IOptions<EdgeReleaseUploadOptions> uploadOptions,
    IRepository<ClientHostRelease> hostRepository,
    IRepository<ClientPluginRelease> pluginRepository,
    IClientReleaseRetentionService retentionService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<PublishEdgeReleaseBundleCommand, Result<EdgeReleaseBundlePublishResultDto>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Regex SemVerPattern = new(
        @"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-fA-F]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] RequiredVelopackManifestNames =
    [
        "releases.stable.json",
        "assets.stable.json"
    ];

    private static readonly string[] ForbiddenFileNameSuffixes =
    [
        "launcher.accounts.json",
        ".db",
        ".sqlite",
        ".db-wal",
        ".db-shm",
        "crash.log"
    ];

    public const string UploadInProgressMessage = "已有 Edge 发布上传正在执行，请稍后重试。";

    public async Task<Result<EdgeReleaseBundlePublishResultDto>> Handle(
        PublishEdgeReleaseBundleCommand request,
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
            "edge-release-bundles",
            Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(stagingRoot, "bundle.zip");
        var extractRoot = Path.Combine(stagingRoot, "extracted");
        var succeeded = false;
        var commitPointReached = false;
        string? installerTargetForRollback = null;
        VelopackFilePublishTransaction? velopackTransaction = null;
        var pluginPackageRollbackTargets = new List<string>();

        try
        {
            Directory.CreateDirectory(stagingRoot);
            var copiedBytes = await CopyBundleAsync(request.BundleStream, zipPath, cancellationToken);
            if (request.ContentLength is { } declaredLength && declaredLength != copiedBytes)
            {
                return await FailAsync("Edge 发布包上传大小与请求头不一致。", cancellationToken);
            }

            ExtractBundle(zipPath, extractRoot);
            var artifact = await LoadAndValidateBundleAsync(extractRoot, cancellationToken);
            if (!artifact.IsSuccess)
            {
                return await FailAsync(artifact.Error!, cancellationToken);
            }

            var manifest = artifact.Manifest!;
            var channel = manifest.Channel.Trim();
            var version = manifest.Version.Trim();
            var installerTarget = Path.Combine(edgeRoot, "installers", channel, version);
            var velopackTarget = Path.Combine(edgeRoot, "velopack", channel);
            if (Directory.Exists(installerTarget))
            {
                return await FailAsync($"Edge 发布版本已存在，默认不覆盖: {channel}/{version}。", cancellationToken);
            }

            AssertFreeDiskSpace(edgeRoot, copiedBytes);
            Directory.CreateDirectory(Path.GetDirectoryName(installerTarget)!);
            Directory.CreateDirectory(velopackTarget);

            velopackTransaction = CopyVelopackFiles(
                artifact.VelopackRoot!,
                velopackTarget,
                Path.Combine(stagingRoot, "velopack-backup"));

            Directory.Move(artifact.InstallerRoot!, installerTarget);
            installerTargetForRollback = installerTarget;
            var pluginPackages = await PublishMissingPluginPackagesAsync(
                edgeRoot,
                manifest,
                installerTarget,
                stagingRoot,
                pluginPackageRollbackTargets,
                cancellationToken);

            await UpsertDatabaseRowsAsync(
                manifest,
                BuildManifestDownloadUrl(channel, version),
                pluginPackages,
                cancellationToken);

            commitPointReached = true;

            var cleanup = FileCleanupResult.Empty;
            var cleanupSucceeded = true;
            string? cleanupWarning = null;
            try
            {
                await ApplyRetentionAsync(manifest, CancellationToken.None);
                cleanup = await CleanupArchivedFilesAsync(
                    edgeRoot,
                    channel,
                    manifest.TargetRuntime,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                cleanupSucceeded = false;
                cleanupWarning = $"Edge 发布成功，但保留/清理旧版本未完成：{ex.Message}";
            }

            stopwatch.Stop();

            var result = new EdgeReleaseBundlePublishResultDto(
                channel,
                version,
                NormalizeOptional(manifest.SourceCommit),
                NormalizeOptional(manifest.PreviousSourceCommit),
                NormalizeOptional(manifest.ReleaseNotes),
                SplitReleaseNotes(manifest.ReleaseNotes),
                BuildComponentSummary(manifest),
                copiedBytes,
                stopwatch.Elapsed.TotalSeconds,
                uploadOptions.Value.MaxUploadMbps,
                installerTarget,
                velopackTarget,
                cleanup.ArchivedVersions,
                cleanup.DeletedInstallerVersions,
                cleanup.DeletedVelopackFiles,
                cleanupSucceeded,
                cleanupWarning,
                BuildVerificationUrls(channel, version, pluginPackages.Values));

            await WriteAuditAsync(result, request.SourceIp, succeeded: true, cleanupWarning, CancellationToken.None);
            succeeded = true;
            return Result.Success(result);
        }
        catch (Exception ex)
        {
            var failureMessage = FormatPublishFailure(ex);
            if (!commitPointReached)
            {
                var rollbackWarnings = RollbackPreCommitArtifacts(
                    installerTargetForRollback,
                    velopackTransaction,
                    pluginPackageRollbackTargets);
                if (rollbackWarnings.Count > 0)
                {
                    failureMessage = $"{failureMessage} 回滚警告：{string.Join("；", rollbackWarnings)}";
                }
            }

            return await FailAsync(failureMessage, CancellationToken.None);
        }
        finally
        {
            if (!succeeded)
            {
                TryDeleteDirectory(stagingRoot);
            }
            else
            {
                TryDeleteFile(zipPath);
                TryDeleteDirectory(stagingRoot);
            }
        }

        async Task<Result<EdgeReleaseBundlePublishResultDto>> FailAsync(
            string message,
            CancellationToken token)
        {
            await WriteAuditAsync(null, request.SourceIp, succeeded: false, message, token);
            return Result.Invalid(message);
        }
    }

    private static string FormatPublishFailure(Exception ex)
        => ex switch
        {
            InvalidDataException => ex.Message,
            IOException => $"Edge 发布包处理失败：{ex.Message}",
            OperationCanceledException => "Edge 发布上传已取消。",
            _ => $"Edge 发布包发布失败：{ex.Message}"
        };

    private static IReadOnlyList<string> RollbackPreCommitArtifacts(
        string? installerTarget,
        VelopackFilePublishTransaction? velopackTransaction,
        IEnumerable<string> pluginPackageTargets)
    {
        var warnings = new List<string>();
        foreach (var pluginPackageTarget in pluginPackageTargets)
        {
            TryDeleteDirectory(pluginPackageTarget, warnings);
        }

        if (!string.IsNullOrWhiteSpace(installerTarget))
        {
            TryDeleteDirectory(installerTarget, warnings);
        }

        velopackTransaction?.Rollback(warnings);
        return warnings;
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

    private async Task<long> CopyBundleAsync(
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
                throw new InvalidDataException($"Edge 发布包超过最大限制 {limitBytes} 字节。");
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

    private static void ExtractBundle(string zipPath, string extractRoot)
    {
        Directory.CreateDirectory(extractRoot);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var relativePath = NormalizeZipEntryPath(entry.FullName);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(extractRoot, relativePath));
            if (!IsChildPath(extractRoot, targetPath))
            {
                throw new InvalidDataException($"Edge 发布包包含非法路径: {entry.FullName}");
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
                throw new InvalidDataException("Edge 发布包不允许包含符号链接或重解析点。");
            }
        }
    }

    private async Task<BundleValidationResult> LoadAndValidateBundleAsync(
        string extractRoot,
        CancellationToken cancellationToken)
    {
        var installerRoot = Path.Combine(extractRoot, "installer");
        var velopackRoot = Path.Combine(extractRoot, "velopack");
        if (!Directory.Exists(installerRoot) || !Directory.Exists(velopackRoot))
        {
            return BundleValidationResult.Fail("Edge 发布包必须包含 installer/ 和 velopack/ 两个目录。");
        }

        AssertNoForbiddenFiles(installerRoot);
        AssertNoForbiddenFiles(velopackRoot);
        AssertCloudIdentityTemplatesAreEmpty(installerRoot);

        var manifestPath = Path.Combine(installerRoot, "installer-artifact.json");
        if (!File.Exists(manifestPath))
        {
            return BundleValidationResult.Fail("Edge 发布包缺少 installer/installer-artifact.json。");
        }

        EdgeInstallerArtifactManifest? manifest;
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<EdgeInstallerArtifactManifest>(
                stream,
                JsonOptions,
                cancellationToken);
        }
        catch (JsonException)
        {
            return BundleValidationResult.Fail("Edge 发布包 manifest 无法解析。");
        }

        if (manifest is null)
        {
            return BundleValidationResult.Fail("Edge 发布包 manifest 为空。");
        }

        var basicError = ValidateManifestBasics(manifest);
        if (basicError is not null)
        {
            return BundleValidationResult.Fail(basicError);
        }

        var requiredLayoutError = ValidateRequiredLayout(installerRoot, velopackRoot, manifest);
        if (requiredLayoutError is not null)
        {
            return BundleValidationResult.Fail(requiredLayoutError);
        }

        var hashError = ValidateHashes(installerRoot, manifest);
        if (hashError is not null)
        {
            return BundleValidationResult.Fail(hashError);
        }

        return BundleValidationResult.Success(manifest, installerRoot, velopackRoot);
    }

    private static string? ValidateManifestBasics(EdgeInstallerArtifactManifest manifest)
    {
        if (manifest.SchemaVersion != ClientReleaseCatalogSchema.Version)
        {
            return "Edge 发布包 manifest schemaVersion 不受支持。";
        }

        if (!string.Equals(manifest.Channel, "stable", StringComparison.OrdinalIgnoreCase))
        {
            return "生产服务器只允许发布 stable 渠道。";
        }

        if (string.IsNullOrWhiteSpace(manifest.Version) || !SemVerPattern.IsMatch(manifest.Version))
        {
            return "Edge 发布包版本号必须是 SemVer。";
        }

        if (string.IsNullOrWhiteSpace(manifest.HostApiVersion)
            || string.IsNullOrWhiteSpace(manifest.TargetRuntime)
            || string.IsNullOrWhiteSpace(manifest.InstallerStubFile)
            || string.IsNullOrWhiteSpace(manifest.LauncherDirectory)
            || string.IsNullOrWhiteSpace(manifest.HostDirectory)
            || string.IsNullOrWhiteSpace(manifest.PluginsRoot)
            || manifest.Modules.Count == 0)
        {
            return "Edge 发布包 manifest 不完整。";
        }

        if (!IsSafeRelativePath(manifest.InstallerStubFile)
            || !IsSafeRelativePath(manifest.LauncherDirectory)
            || !IsSafeRelativePath(manifest.HostDirectory)
            || !IsSafeRelativePath(manifest.PluginsRoot))
        {
            return "Edge 发布包 manifest 包含非法相对路径。";
        }

        foreach (var module in manifest.Modules)
        {
            if (string.IsNullOrWhiteSpace(module.ModuleId)
                || string.IsNullOrWhiteSpace(module.Version)
                || string.IsNullOrWhiteSpace(module.HostApiVersion)
                || string.IsNullOrWhiteSpace(module.MinHostVersion)
                || string.IsNullOrWhiteSpace(module.MaxHostVersion)
                || string.IsNullOrWhiteSpace(module.PluginDirectory)
                || !IsSafeRelativePath(module.PluginDirectory))
            {
                return "Edge 发布包 manifest 包含非法插件声明。";
            }
        }

        if (string.IsNullOrWhiteSpace(manifest.ReleaseNotes))
        {
            return "Edge 发布包缺少发布更新内容。";
        }

        return null;
    }

    private static string? ValidateRequiredLayout(
        string installerRoot,
        string velopackRoot,
        EdgeInstallerArtifactManifest manifest)
    {
        if (!File.Exists(Path.Combine(installerRoot, manifest.InstallerStubFile)))
        {
            return "Edge 发布包缺少 IIoT.Edge.Setup.exe。";
        }

        if (!Directory.Exists(Path.Combine(installerRoot, manifest.LauncherDirectory)))
        {
            return "Edge 发布包缺少 launcher/。";
        }

        if (!Directory.Exists(Path.Combine(installerRoot, manifest.HostDirectory)))
        {
            return "Edge 发布包缺少 host/。";
        }

        if (!Directory.Exists(Path.Combine(installerRoot, manifest.PluginsRoot)))
        {
            return "Edge 发布包缺少 plugins/。";
        }

        if (string.IsNullOrWhiteSpace(manifest.VelopackSetupFile)
            || !IsSafeRelativePath(manifest.VelopackSetupFile)
            || !manifest.VelopackSetupFile.Replace('\\', '/').StartsWith("velopack/", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(installerRoot, manifest.VelopackSetupFile)))
        {
            return "Edge 发布包缺少安装素材 Velopack Setup。";
        }

        foreach (var module in manifest.Modules)
        {
            var pluginDirectory = Path.Combine(installerRoot, manifest.PluginsRoot, module.PluginDirectory);
            if (!Directory.Exists(pluginDirectory))
            {
                return $"Edge 发布包缺少插件目录: {module.ModuleId}。";
            }
        }

        foreach (var required in RequiredVelopackManifestNames)
        {
            if (!File.Exists(Path.Combine(velopackRoot, required)))
            {
                return $"Edge 发布包缺少 Velopack 清单: {required}。";
            }
        }

        if (!Directory.EnumerateFiles(velopackRoot, "*.nupkg", SearchOption.TopDirectoryOnly).Any())
        {
            return "Edge 发布包缺少 Velopack .nupkg。";
        }

        return null;
    }

    private static string? ValidateHashes(
        string installerRoot,
        EdgeInstallerArtifactManifest manifest)
    {
        var installerPath = Path.Combine(installerRoot, manifest.InstallerStubFile);
        if (!IsSha256(manifest.InstallerStubSha256)
            || !string.Equals(HashFile(installerPath), manifest.InstallerStubSha256, StringComparison.OrdinalIgnoreCase)
            || new FileInfo(installerPath).Length != manifest.InstallerStubSize)
        {
            return "Edge 发布包安装器 sha256 或 size 与 manifest 不一致。";
        }

        var hostDirectory = Path.Combine(installerRoot, manifest.HostDirectory);
        if (!IsSha256(manifest.HostDirectorySha256)
            || !string.Equals(HashDirectory(hostDirectory), manifest.HostDirectorySha256, StringComparison.OrdinalIgnoreCase)
            || GetDirectorySize(hostDirectory) != manifest.HostDirectorySize)
        {
            return "Edge 发布包 host 目录 sha256 或 size 与 manifest 不一致。";
        }

        if (!string.IsNullOrWhiteSpace(manifest.VelopackSetupFile))
        {
            var setupPath = Path.Combine(installerRoot, manifest.VelopackSetupFile);
            if (!IsSha256(manifest.VelopackSetupSha256)
                || !string.Equals(HashFile(setupPath), manifest.VelopackSetupSha256, StringComparison.OrdinalIgnoreCase)
                || new FileInfo(setupPath).Length != manifest.VelopackSetupSize)
            {
                return "Edge 发布包 Velopack Setup sha256 或 size 与 manifest 不一致。";
            }
        }

        foreach (var module in manifest.Modules)
        {
            var pluginDirectory = Path.Combine(installerRoot, manifest.PluginsRoot, module.PluginDirectory);
            if (!IsSha256(module.PluginSha256)
                || !string.Equals(HashDirectory(pluginDirectory), module.PluginSha256, StringComparison.OrdinalIgnoreCase)
                || GetDirectorySize(pluginDirectory) != module.PluginSize)
            {
                return $"Edge 发布包插件 {module.ModuleId} sha256 或 size 与 manifest 不一致。";
            }
        }

        return null;
    }

    private static VelopackFilePublishTransaction CopyVelopackFiles(
        string sourceRoot,
        string targetRoot,
        string backupRoot)
    {
        var changes = new List<VelopackFileChange>();
        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                RelativePath = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/')
            })
            .OrderBy(item => IsVelopackChannelManifest(item.RelativePath) ? 1 : 0)
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var file in files)
        {
            if (!IsSafeRelativePath(file.RelativePath))
            {
                throw new InvalidDataException($"Velopack 发布文件路径非法: {file.RelativePath}");
            }

            var targetPath = Path.Combine(targetRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            string? backupPath = null;
            if (File.Exists(targetPath))
            {
                backupPath = Path.Combine(backupRoot, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(targetPath, backupPath, overwrite: false);
            }

            changes.Add(new VelopackFileChange(file.RelativePath, targetPath, backupPath));
            File.Copy(file.Path, targetPath, overwrite: true);
        }

        return new VelopackFilePublishTransaction(targetRoot, changes);
    }

    private async Task<IReadOnlyDictionary<string, PluginPackagePublishArtifact>> PublishMissingPluginPackagesAsync(
        string edgeRoot,
        EdgeInstallerArtifactManifest manifest,
        string installerTarget,
        string stagingRoot,
        ICollection<string> rollbackTargets,
        CancellationToken cancellationToken)
    {
        var published = new Dictionary<string, PluginPackagePublishArtifact>(StringComparer.OrdinalIgnoreCase);
        var stagingPackagesRoot = Path.Combine(stagingRoot, "plugin-packages");
        Directory.CreateDirectory(stagingPackagesRoot);

        foreach (var module in manifest.Modules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var existing = await pluginRepository.GetSingleOrDefaultAsync(
                new ClientPluginReleaseByIdentitySpec(
                    module.ModuleId,
                    manifest.Channel,
                    module.Version,
                    manifest.TargetRuntime),
                cancellationToken);
            if (existing is not null)
            {
                continue;
            }

            var pluginDirectory = Path.Combine(installerTarget, manifest.PluginsRoot, module.PluginDirectory);
            if (!Directory.Exists(pluginDirectory))
            {
                throw new InvalidDataException($"Edge 发布包缺少插件目录: {module.ModuleId}。");
            }

            var pluginManifestPath = Path.Combine(pluginDirectory, "plugin.json");
            if (!File.Exists(pluginManifestPath))
            {
                throw new InvalidDataException($"Edge 发布包插件目录缺少 plugin.json: {module.ModuleId}。");
            }

            var packageFileName = BuildPluginPackageFileName(module.ModuleId, module.Version, manifest.TargetRuntime);
            var stagingPackagePath = Path.Combine(stagingPackagesRoot, packageFileName);
            if (File.Exists(stagingPackagePath))
            {
                File.Delete(stagingPackagePath);
            }

            ZipFile.CreateFromDirectory(
                pluginDirectory,
                stagingPackagePath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);

            var sha256 = HashFile(stagingPackagePath);
            var size = new FileInfo(stagingPackagePath).Length;
            var packageDirectory = Path.Combine(
                edgeRoot,
                "plugins",
                manifest.Channel,
                EscapeFileSystemSegment(module.ModuleId),
                module.Version);
            if (Directory.Exists(packageDirectory))
            {
                throw new InvalidDataException(
                    $"Edge 插件包版本目录已存在，拒绝覆盖: {manifest.Channel}/{module.ModuleId}/{module.Version}。");
            }

            Directory.CreateDirectory(packageDirectory);
            rollbackTargets.Add(packageDirectory);
            var packagePath = Path.Combine(packageDirectory, packageFileName);
            File.Move(stagingPackagePath, packagePath);
            published[module.ModuleId] = new PluginPackagePublishArtifact(
                module.ModuleId,
                module.Version,
                packagePath,
                BuildPluginDownloadUrl(manifest.Channel, module.ModuleId, module.Version, packageFileName),
                sha256,
                size);
        }

        return published;
    }

    private async Task UpsertDatabaseRowsAsync(
        EdgeInstallerArtifactManifest manifest,
        string manifestDownloadUrl,
        IReadOnlyDictionary<string, PluginPackagePublishArtifact> pluginPackages,
        CancellationToken cancellationToken)
    {
        var host = await hostRepository.GetSingleOrDefaultAsync(
            new ClientHostReleaseByIdentitySpec(
                manifest.Channel,
                manifest.Version,
                manifest.TargetRuntime),
            cancellationToken);

        if (host is null)
        {
            host = new ClientHostRelease(
                manifest.Channel,
                manifest.Version,
                manifest.HostApiVersion,
                manifest.TargetRuntime,
                manifest.TargetFramework,
                manifestDownloadUrl,
                manifest.InstallerStubSha256!,
                manifest.InstallerStubSize,
                manifest.ReleaseNotes,
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                manifest.GeneratedAtUtc);
            hostRepository.Add(host);
        }
        else
        {
            host.UpdateRelease(
                manifest.HostApiVersion,
                manifest.TargetFramework,
                manifestDownloadUrl,
                manifest.InstallerStubSha256!,
                manifest.InstallerStubSize,
                manifest.ReleaseNotes,
                ClientReleaseStatus.Published,
                null,
                "IIoT");
        }

        foreach (var module in manifest.Modules)
        {
            var plugin = await pluginRepository.GetSingleOrDefaultAsync(
                new ClientPluginReleaseByIdentitySpec(
                    module.ModuleId,
                    manifest.Channel,
                    module.Version,
                    manifest.TargetRuntime),
                cancellationToken);

            if (plugin is not null)
            {
                continue;
            }

            if (!pluginPackages.TryGetValue(module.ModuleId, out var package))
            {
                throw new InvalidDataException($"插件 {module.ModuleId} 缺少可安装的独立发布包。");
            }

            plugin = new ClientPluginRelease(
                module.ModuleId,
                string.IsNullOrWhiteSpace(module.DisplayName) ? module.ModuleId : module.DisplayName,
                module.Description,
                null,
                null,
                manifest.Channel,
                module.Version,
                module.HostApiVersion,
                module.MinHostVersion,
                module.MaxHostVersion,
                manifest.TargetRuntime,
                manifest.TargetFramework,
                package.DownloadUrl,
                package.Sha256,
                package.PackageSize,
                manifest.ReleaseNotes,
                "[]",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                manifest.GeneratedAtUtc);
            pluginRepository.Add(plugin);

        }

        await hostRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyRetentionAsync(
        EdgeInstallerArtifactManifest manifest,
        CancellationToken cancellationToken)
    {
        await retentionService.ApplyHostPolicyAsync(
            manifest.Channel,
            manifest.TargetRuntime,
            cancellationToken);

        foreach (var module in manifest.Modules)
        {
            await retentionService.ApplyPluginPolicyAsync(
                module.ModuleId,
                manifest.Channel,
                manifest.TargetRuntime,
                cancellationToken);
        }
    }

    private async Task<FileCleanupResult> CleanupArchivedFilesAsync(
        string edgeRoot,
        string channel,
        string targetRuntime,
        CancellationToken cancellationToken)
    {
        var hostReleases = await hostRepository.GetListAsync(
            new ClientHostReleasesByChannelSpec(channel, targetRuntime, onlyPublished: false, includeArchived: true),
            cancellationToken);
        var archivedVersions = hostReleases
            .Where(release => release.Status == ClientReleaseStatus.Archived)
            .Select(release => release.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var removableVersions = archivedVersions.ToList();

        var deletedInstallers = new List<string>();
        var installerChannelRoot = Path.Combine(edgeRoot, "installers", channel);
        foreach (var version in removableVersions)
        {
            var directory = Path.Combine(installerChannelRoot, version);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
                deletedInstallers.Add(version);
            }
        }

        var deletedVelopackFiles = CleanupVelopackFiles(
            Path.Combine(edgeRoot, "velopack", channel),
            removableVersions);
        await CleanupArchivedPluginFilesAsync(edgeRoot, channel, targetRuntime, cancellationToken);

        return new FileCleanupResult(archivedVersions, deletedInstallers, deletedVelopackFiles);
    }

    private async Task CleanupArchivedPluginFilesAsync(
        string edgeRoot,
        string channel,
        string targetRuntime,
        CancellationToken cancellationToken)
    {
        var pluginReleases = await pluginRepository.GetListAsync(
            new ClientPluginReleasesByChannelSpec(channel, targetRuntime, onlyPublished: false, includeArchived: true),
            cancellationToken);

        foreach (var release in pluginReleases.Where(release => release.Status == ClientReleaseStatus.Archived))
        {
            var directory = Path.Combine(
                edgeRoot,
                "plugins",
                release.Channel,
                EscapeFileSystemSegment(release.ModuleId),
                release.Version);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static IReadOnlyList<string> CleanupVelopackFiles(
        string velopackChannelRoot,
        IReadOnlyCollection<string> removableVersions)
    {
        var deleted = new List<string>();
        if (!Directory.Exists(velopackChannelRoot) || removableVersions.Count == 0)
        {
            return deleted;
        }

        foreach (var file in Directory.EnumerateFiles(velopackChannelRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (IsVelopackChannelManifest(name))
            {
                continue;
            }

            if (!removableVersions.Any(version => FileNameContainsVersion(name, version)))
            {
                continue;
            }

            if (VelopackManifestsReferenceFile(velopackChannelRoot, name))
            {
                continue;
            }

            File.Delete(file);
            deleted.Add(name);
        }

        return deleted;
    }

    private static bool VelopackManifestsReferenceFile(string velopackChannelRoot, string fileName)
    {
        foreach (var manifestName in RequiredVelopackManifestNames.Concat(["RELEASES"]))
        {
            var manifestPath = Path.Combine(velopackChannelRoot, manifestName);
            if (File.Exists(manifestPath)
                && File.ReadAllText(manifestPath).Contains(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FileNameContainsVersion(string fileName, string version)
    {
        var pattern = $@"(^|[._-]){Regex.Escape(version)}([._-]|$)";
        return Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static void AssertFreeDiskSpace(string rootPath, long bundleBytes)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(rootPath))!);
        var required = bundleBytes * 2;
        if (drive.AvailableFreeSpace < required)
        {
            throw new InvalidDataException(
                $"Edge 发布磁盘剩余空间不足，至少需要 {required} 字节。");
        }
    }

    private static string BuildManifestDownloadUrl(string channel, string version)
        => $"/edge-updates/installers/{Uri.EscapeDataString(channel)}/{Uri.EscapeDataString(version)}/installer-artifact.json";

    private static string BuildPluginDownloadUrl(
        string channel,
        string moduleId,
        string version,
        string packageFileName)
        => $"/edge-updates/plugins/{Uri.EscapeDataString(channel)}/{Uri.EscapeDataString(moduleId)}/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(packageFileName)}";

    private static string BuildPluginPackageFileName(
        string moduleId,
        string version,
        string targetRuntime)
        => $"IIoT.EdgePlugin.{SanitizeFileNameSegment(moduleId)}-{SanitizeFileNameSegment(version)}-{SanitizeFileNameSegment(targetRuntime)}.zip";

    private static string EscapeFileSystemSegment(string value)
        => Uri.EscapeDataString(value.Trim());

    private static string SanitizeFileNameSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder(value.Trim().Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static IReadOnlyList<string> BuildVerificationUrls(
        string channel,
        string version,
        IEnumerable<PluginPackagePublishArtifact> pluginPackages)
    {
        var urls = new List<string>
        {
            $"/edge-updates/installers/{channel}/{version}/installer-artifact.json",
            $"/edge-updates/installers/{channel}/{version}/IIoT.Edge.Setup.exe",
            $"/edge-updates/velopack/{channel}/releases.{channel}.json",
            $"/edge-updates/velopack/{channel}/assets.{channel}.json"
        };
        urls.AddRange(pluginPackages.Select(package => package.DownloadUrl));
        return urls;
    }

    private static IReadOnlyList<string> BuildComponentSummary(EdgeInstallerArtifactManifest manifest)
    {
        var components = new List<string>
        {
            $"host:{manifest.Version}"
        };
        components.AddRange(manifest.Modules.Select(module => $"{module.ModuleId}:{module.Version}"));
        return components;
    }

    private static IReadOnlyList<string> SplitReleaseNotes(string? releaseNotes)
    {
        return string.IsNullOrWhiteSpace(releaseNotes)
            ? []
            : releaseNotes
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(50)
                .ToList();
    }

    private async Task WriteAuditAsync(
        EdgeReleaseBundlePublishResultDto? result,
        string? sourceIp,
        bool succeeded,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        var target = result is null
            ? "edge-release-upload"
            : $"{result.Channel}/{result.Version}";
        var summary = succeeded && result is not null
            ? $"Published Edge release {target} from {result.SourceCommit ?? "unknown"} via HTTP upload from {sourceIp ?? "unknown client"}."
            : $"Failed to publish Edge release via HTTP upload from {sourceIp ?? "unknown client"}.";

        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "ClientRelease.Publish",
                "EdgeReleaseBundle",
                target,
                DateTime.UtcNow,
                succeeded,
                summary,
                failureReason),
            cancellationToken);
    }

    private static Guid? ParseActorUserId(string? userId)
        => Guid.TryParse(userId, out var parsed) ? parsed : null;

    private static void AssertNoForbiddenFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            var lower = relativePath.ToLowerInvariant();
            if (lower.Contains("/diagnostics/logs/", StringComparison.Ordinal)
                || lower.Contains("/logs/", StringComparison.Ordinal)
                || lower.Contains("/recipe/", StringComparison.Ordinal)
                || lower.Contains("/excel/", StringComparison.Ordinal)
                || ForbiddenFileNameSuffixes.Any(suffix => lower.EndsWith(suffix, StringComparison.Ordinal))
                || lower.Contains("bootstrapsecret", StringComparison.Ordinal)
                || lower.Contains("bootstrap-secret", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Edge 发布包包含禁止上传的现场数据或密钥文件: {relativePath}");
            }
        }
    }

    private static void AssertCloudIdentityTemplatesAreEmpty(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "appsettings*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (!document.RootElement.TryGetProperty("CloudApi", out var cloudApi))
            {
                continue;
            }

            foreach (var key in new[] { "ClientCode", "BootstrapSecret" })
            {
                if (cloudApi.TryGetProperty(key, out var value)
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
                    throw new InvalidDataException($"Edge 发布包配置包含真实 CloudApi:{key}: {relativePath}");
                }
            }
        }
    }

    private static string NormalizeZipEntryPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new InvalidDataException($"Edge 发布包包含非法 zip 路径: {path}");
        }

        return normalized;
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/').Trim('/');
        return normalized.Length > 0
            && !Path.IsPathRooted(path)
            && !normalized.Contains(':', StringComparison.Ordinal)
            && !normalized.Split('/').Any(segment => segment is "" or "." or "..");
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

    private static bool IsVelopackChannelManifest(string relativePath)
        => relativePath.Equals("releases.stable.json", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("assets.stable.json", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("RELEASES", StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256(string? value)
        => !string.IsNullOrWhiteSpace(value) && Sha256Pattern.IsMatch(value);

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

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void TryDeleteDirectory(string path, ICollection<string>? warnings = null)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException ex)
            {
                warnings?.Add($"无法删除目录 {path}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                warnings?.Add($"无法删除目录 {path}: {ex.Message}");
            }
        }
    }

    private static void TryDeleteFile(string path, ICollection<string>? warnings = null)
    {
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException ex)
            {
                warnings?.Add($"无法删除文件 {path}: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                warnings?.Add($"无法删除文件 {path}: {ex.Message}");
            }
        }
    }

    private sealed record BundleValidationResult(
        bool IsSuccess,
        EdgeInstallerArtifactManifest? Manifest,
        string? InstallerRoot,
        string? VelopackRoot,
        string? Error)
    {
        public static BundleValidationResult Success(
            EdgeInstallerArtifactManifest manifest,
            string installerRoot,
            string velopackRoot)
            => new(true, manifest, installerRoot, velopackRoot, null);

        public static BundleValidationResult Fail(string error)
            => new(false, null, null, null, error);
    }

    private sealed record FileCleanupResult(
        IReadOnlyList<string> ArchivedVersions,
        IReadOnlyList<string> DeletedInstallerVersions,
        IReadOnlyList<string> DeletedVelopackFiles)
    {
        public static FileCleanupResult Empty { get; } = new([], [], []);
    }

    private sealed record PluginPackagePublishArtifact(
        string ModuleId,
        string Version,
        string PackagePath,
        string DownloadUrl,
        string Sha256,
        long PackageSize);

    private sealed record VelopackFilePublishTransaction(
        string TargetRoot,
        IReadOnlyList<VelopackFileChange> Changes)
    {
        public void Rollback(ICollection<string> warnings)
        {
            foreach (var change in Changes.Reverse())
            {
                if (change.BackupPath is not null)
                {
                    if (!File.Exists(change.BackupPath))
                    {
                        warnings.Add($"无法恢复 Velopack 文件 {change.RelativePath}: 备份文件缺失。");
                        continue;
                    }

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(change.TargetPath)!);
                        File.Copy(change.BackupPath, change.TargetPath, overwrite: true);
                    }
                    catch (IOException ex)
                    {
                        warnings.Add($"无法恢复 Velopack 文件 {change.RelativePath}: {ex.Message}");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        warnings.Add($"无法恢复 Velopack 文件 {change.RelativePath}: {ex.Message}");
                    }
                }
                else
                {
                    TryDeleteFile(change.TargetPath, warnings);
                }
            }
        }
    }

    private sealed record VelopackFileChange(
        string RelativePath,
        string TargetPath,
        string? BackupPath);

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
}
