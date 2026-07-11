using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
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
using Microsoft.Extensions.Logging;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AuthorizeRequirement(ClientReleasePermissions.Publish)]
[DistributedLock(
    ClientReleasePublishLock.Resource,
    TimeoutSeconds = ClientReleasePublishLock.AcquireTimeoutSeconds)]
public sealed record PublishEdgeReleaseBundleCommand()
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
    ClientReleaseUploadCoordinator uploadCoordinator,
    IRepository<ClientReleaseComponent> componentRepository,
    IClientReleaseVersionObservationReader observationReader,
    IClientReleaseRetentionService retentionService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService,
    ILogger<PublishEdgeReleaseBundleHandler> logger)
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

    public async Task<Result<EdgeReleaseBundlePublishResultDto>> Handle(
        PublishEdgeReleaseBundleCommand _,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var uploadSession = uploadCoordinator.Begin(ClientReleaseUploadKind.HostBundle);

        var edgeRoot = uploadSession.EdgeRoot;
        var stagingRoot = uploadSession.StagingRoot;
        var zipPath = uploadSession.UploadedFilePath;
        var extractRoot = Path.Combine(stagingRoot, "extracted");
        var copiedBytes = 0L;
        EdgeInstallerArtifactManifest? manifest = null;
        string? installerTarget = null;
        string? velopackTarget = null;
        InstallerReleasePublishFileTransaction? installerTransaction = null;
        VelopackReleasePublishFileTransaction? velopackTransaction = null;
        IReadOnlyDictionary<string, PluginPackagePublishArtifact> pluginPackages =
            new Dictionary<string, PluginPackagePublishArtifact>();
        IReadOnlyList<ClientReleaseExpectedVersionState> expectedVersions = [];
        var saveChangesInvoked = false;
        var saveChangesReturned = false;
        var stableOutcomeAuditWritten = false;

        try
        {
            copiedBytes = await uploadSession.ReceiveAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ExtractBundle(zipPath, extractRoot);
            cancellationToken.ThrowIfCancellationRequested();
            var artifact = await LoadAndValidateBundleAsync(extractRoot, cancellationToken);
            if (!artifact.IsSuccess)
            {
                return await FailAsync(artifact.Error!, cancellationToken);
            }

            manifest = artifact.Manifest!;
            var channel = manifest.Channel.Trim();
            var version = manifest.Version.Trim();
            installerTarget = Path.Combine(edgeRoot, "installers", channel, version);
            velopackTarget = Path.Combine(edgeRoot, "velopack", channel);
            if (Directory.Exists(installerTarget))
            {
                throw new ClientReleasePublishConflictException();
            }

            AssertFreeDiskSpace(edgeRoot, copiedBytes);
            velopackTransaction = new VelopackReleasePublishFileTransaction(
                edgeRoot,
                artifact.VelopackRoot!,
                velopackTarget,
                Path.Combine(stagingRoot, "velopack-backup"),
                logger);
            velopackTransaction.Publish();
            cancellationToken.ThrowIfCancellationRequested();

            installerTransaction = new InstallerReleasePublishFileTransaction(
                edgeRoot,
                installerTarget,
                logger);
            installerTransaction.Publish(artifact.InstallerRoot!);
            pluginPackages = await PublishMissingPluginPackagesAsync(
                edgeRoot,
                manifest,
                installerTarget,
                stagingRoot,
                cancellationToken);

            var publishedDirectories = new List<string>
            {
                installerTarget,
                velopackTarget
            };
            publishedDirectories.AddRange(pluginPackages.Values.Select(package => Path.GetDirectoryName(package.PackagePath)!));
            var publishedFiles = BuildRequiredPublishedHostFiles(installerTarget, velopackTarget, manifest)
                .Concat(pluginPackages.Values.Select(package => package.PackagePath))
                .ToList();
            EdgeReleasePublishedFilePermissions.EnsureGatewayReadable(
                edgeRoot,
                publishedDirectories,
                publishedFiles);
            EdgeReleasePublishedFilePermissions.AssertPublishedPathsReady(
                edgeRoot,
                publishedDirectories,
                publishedFiles);
            cancellationToken.ThrowIfCancellationRequested();

            expectedVersions = await PrepareDatabaseRowsAsync(
                manifest,
                BuildManifestDownloadUrl(channel, version),
                pluginPackages,
                installerTarget,
                velopackTransaction.PublishedFiles,
                cancellationToken);
            saveChangesInvoked = true;
            try
            {
                await componentRepository.SaveChangesAsync(cancellationToken);
                saveChangesReturned = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ClientReleasePublishDiagnostics.LogFailure(
                    logger,
                    LogLevel.Warning,
                    ClientReleasePublishDiagnostics.HostPublishFailed,
                    "host-save-response",
                    ex,
                    "host-release");
                var outcome = await HostReleaseCommitRecovery.ObserveAsync(
                    observationReader,
                    expectedVersions,
                    installerTransaction,
                    velopackTransaction,
                    pluginPackages.Values.Select(package => package.FileTransaction).ToArray(),
                    logger);
                switch (outcome)
                {
                    case HostReleaseCommitObservationOutcome.Committed:
                        {
                            var markerWarning = FinalizeOwnershipMarkers(
                                installerTransaction,
                                pluginPackages.Values);
                            stopwatch.Stop();
                            var recoveredResult = BuildResult(
                                manifest,
                                copiedBytes,
                                stopwatch.Elapsed,
                                uploadSession.MaxUploadMbps,
                                installerTarget,
                                velopackTarget,
                                pluginPackages.Values,
                                FileCleanupResult.Empty,
                                cleanupSucceeded: false,
                                ClientReleasePublishWarnings.Combine(
                                    "Edge 发布已确认，但保留/清理旧版本未执行。",
                                    markerWarning));
                            await WriteStableOutcomeAuditAsync(
                                manifest,
                                HostPublishAuditOutcome.CommitRecovered);
                            stableOutcomeAuditWritten = true;
                            return Result.Success(recoveredResult);
                        }
                    case HostReleaseCommitObservationOutcome.Conflict:
                        await WriteStableOutcomeAuditAsync(
                            manifest,
                            HostPublishAuditOutcome.CommitConflict);
                        stableOutcomeAuditWritten = true;
                        throw new ClientReleasePublishConflictException();
                    default:
                        await WriteStableOutcomeAuditAsync(
                            manifest,
                            HostPublishAuditOutcome.CommitUnknown);
                        stableOutcomeAuditWritten = true;
                        throw new ClientReleaseCommitUnknownException();
                }
            }

            var ownershipWarning = FinalizeOwnershipMarkers(
                installerTransaction,
                pluginPackages.Values);
            cancellationToken.ThrowIfCancellationRequested();

            var cleanup = FileCleanupResult.Empty;
            var cleanupSucceeded = true;
            var cleanupWarning = ownershipWarning;
            try
            {
                await ApplyRetentionAsync(manifest, cancellationToken);
                cleanup = await CleanupArchivedFilesAsync(
                    edgeRoot,
                    channel,
                    manifest.TargetRuntime,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ClientReleasePublishDiagnostics.LogFailure(
                    logger,
                    LogLevel.Warning,
                    ClientReleasePublishDiagnostics.HostRetentionCleanupFailed,
                    "host-retention-cleanup",
                    ex,
                    "host-release");
                cleanupSucceeded = false;
                cleanupWarning = ClientReleasePublishWarnings.Combine(
                    cleanupWarning,
                    "Edge 发布成功，但保留/清理旧版本未完成。");
            }

            stopwatch.Stop();
            var result = BuildResult(
                manifest,
                copiedBytes,
                stopwatch.Elapsed,
                uploadSession.MaxUploadMbps,
                installerTarget,
                velopackTarget,
                pluginPackages.Values,
                cleanup,
                cleanupSucceeded,
                cleanupWarning);

            await WriteAuditAsync(
                result,
                uploadSession.AuditSource,
                succeeded: true,
                cleanupWarning,
                cancellationToken);
            return Result.Success(result);
        }
        catch (OperationCanceledException)
        {
            if (!saveChangesInvoked)
            {
                RollbackBeforeSave(
                    installerTransaction,
                    velopackTransaction,
                    pluginPackages.Values);
                throw;
            }

            if (manifest is not null
                && installerTransaction is not null
                && velopackTransaction is not null
                && expectedVersions.Count > 0)
            {
                var outcome = await HostReleaseCommitRecovery.ObserveAsync(
                    observationReader,
                    expectedVersions,
                    installerTransaction,
                    velopackTransaction,
                    pluginPackages.Values.Select(package => package.FileTransaction).ToArray(),
                    logger);
                if (outcome == HostReleaseCommitObservationOutcome.Committed)
                {
                    FinalizeOwnershipMarkers(installerTransaction, pluginPackages.Values);
                }

                await WriteStableOutcomeAuditAsync(
                    manifest,
                    outcome switch
                    {
                        HostReleaseCommitObservationOutcome.Committed => HostPublishAuditOutcome.CommittedResponseCancelled,
                        HostReleaseCommitObservationOutcome.Conflict => HostPublishAuditOutcome.CommitConflict,
                        _ => HostPublishAuditOutcome.CommitUnknown
                    });
            }

            throw;
        }
        catch (ClientReleasePublishConflictException)
        {
            if (!saveChangesInvoked)
            {
                RollbackBeforeSave(
                    installerTransaction,
                    velopackTransaction,
                    pluginPackages.Values);
            }

            if (!stableOutcomeAuditWritten && manifest is not null)
            {
                await WriteStableOutcomeAuditAsync(
                    manifest,
                    HostPublishAuditOutcome.PreflightConflict);
            }

            throw;
        }
        catch (ClientReleasePublishException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Error,
                ClientReleasePublishDiagnostics.HostPublishFailed,
                "host-publish",
                ex,
                "host-release");
            if (!saveChangesInvoked)
            {
                var rollbackSucceeded = RollbackBeforeSave(
                    installerTransaction,
                    velopackTransaction,
                    pluginPackages.Values);
                if (ex is ClientReleaseValidationException or InvalidDataException)
                {
                    var failureMessage = FormatPublishFailure(ex);
                    if (!rollbackSucceeded)
                    {
                        failureMessage = $"{failureMessage} 发布回滚清理未完全完成。";
                    }

                    return await FailAsync(failureMessage, CancellationToken.None);
                }

                await WriteAuditAsync(
                    null,
                    uploadSession.AuditSource,
                    succeeded: false,
                    ClientReleasePublishUnavailableException.PublicMessage,
                    CancellationToken.None);
                throw new ClientReleasePublishUnavailableException();
            }

            if (saveChangesReturned
                && manifest is not null
                && installerTarget is not null
                && velopackTarget is not null)
            {
                var markerWarning = installerTransaction is null
                    ? null
                    : FinalizeOwnershipMarkers(installerTransaction, pluginPackages.Values);
                stopwatch.Stop();
                var result = BuildResult(
                    manifest,
                    copiedBytes,
                    stopwatch.Elapsed,
                    uploadSession.MaxUploadMbps,
                    installerTarget,
                    velopackTarget,
                    pluginPackages.Values,
                    FileCleanupResult.Empty,
                    cleanupSucceeded: false,
                    ClientReleasePublishWarnings.Combine(
                        "Edge 发布已提交，但响应后处理未完成。",
                        markerWarning));
                await WriteStableOutcomeAuditAsync(
                    manifest,
                    HostPublishAuditOutcome.CommittedPostProcessingFailed);
                return Result.Success(result);
            }

            if (!stableOutcomeAuditWritten && manifest is not null)
            {
                await WriteStableOutcomeAuditAsync(
                    manifest,
                    HostPublishAuditOutcome.CommitUnknown);
            }

            throw new ClientReleaseCommitUnknownException();
        }
        async Task<Result<EdgeReleaseBundlePublishResultDto>> FailAsync(
            string message,
            CancellationToken token)
        {
            await WriteAuditAsync(null, uploadSession.AuditSource, succeeded: false, message, token);
            return Result.Invalid(message);
        }
    }

    private static string FormatPublishFailure(Exception ex)
        => ex switch
        {
            ClientReleaseValidationException validation => validation.SafeMessage,
            InvalidDataException => "Edge 发布包格式无效。",
            IOException => "Edge 发布包处理失败，请检查上传包和发布目录后重试。",
            _ => "Edge 发布包发布失败。"
        };

    private static bool RollbackBeforeSave(
        InstallerReleasePublishFileTransaction? installerTransaction,
        VelopackReleasePublishFileTransaction? velopackTransaction,
        IEnumerable<PluginPackagePublishArtifact> pluginPackages)
    {
        var succeeded = true;
        foreach (var package in pluginPackages.Reverse())
        {
            succeeded &= package.FileTransaction.TryRollbackBeforeSave();
        }

        succeeded &= installerTransaction?.TryRollbackBeforeSave() ?? true;
        succeeded &= velopackTransaction?.TryRollbackBeforeSave() ?? true;

        return succeeded;
    }

    private static string? FinalizeOwnershipMarkers(
        InstallerReleasePublishFileTransaction installerTransaction,
        IEnumerable<PluginPackagePublishArtifact> pluginPackages)
    {
        var succeeded = installerTransaction.TryRemoveOwnershipMarker();
        foreach (var package in pluginPackages)
        {
            succeeded &= package.FileTransaction.TryRemoveOwnershipMarker();
        }

        return succeeded
            ? null
            : "Edge 发布已提交，但发布所有权标记未完成清理。";
    }

    private static EdgeReleaseBundlePublishResultDto BuildResult(
        EdgeInstallerArtifactManifest manifest,
        long bundleSize,
        TimeSpan elapsed,
        int uploadRateLimitMbps,
        string installerTarget,
        string velopackTarget,
        IEnumerable<PluginPackagePublishArtifact> pluginPackages,
        FileCleanupResult cleanup,
        bool cleanupSucceeded,
        string? cleanupWarning)
    {
        var channel = manifest.Channel.Trim();
        var version = manifest.Version.Trim();
        var packages = pluginPackages.ToArray();
        return new EdgeReleaseBundlePublishResultDto(
            channel,
            version,
            NormalizeOptional(manifest.SourceCommit),
            NormalizeOptional(manifest.PreviousSourceCommit),
            NormalizeOptional(manifest.ReleaseNotes),
            SplitReleaseNotes(manifest.ReleaseNotes),
            BuildComponentSummary(manifest),
            bundleSize,
            elapsed.TotalSeconds,
            uploadRateLimitMbps,
            installerTarget,
            velopackTarget,
            cleanup.ArchivedVersions,
            cleanup.DeletedInstallerVersions,
            cleanup.DeletedVelopackFiles,
            cleanupSucceeded,
            cleanupWarning,
            BuildVerificationUrls(channel, version, packages));
    }

    private async Task WriteStableOutcomeAuditAsync(
        EdgeInstallerArtifactManifest manifest,
        HostPublishAuditOutcome outcome)
    {
        var (operationType, succeeded, summary, failureReason) = outcome switch
        {
            HostPublishAuditOutcome.PreflightConflict => (
                "ClientRelease.Publish.Conflict",
                false,
                "Host bundle publish was rejected because a target version or exact-owned path already exists.",
                "target-already-exists"),
            HostPublishAuditOutcome.CommitRecovered => (
                "ClientRelease.Publish.CommitRecovered",
                true,
                "Host bundle publish commit was confirmed by one bounded independent batch observation after the save response failed.",
                (string?)null),
            HostPublishAuditOutcome.CommittedResponseCancelled => (
                "ClientRelease.Publish.CommittedResponseCancelled",
                true,
                "Host bundle publish commit was confirmed after response cancellation or lease loss.",
                (string?)null),
            HostPublishAuditOutcome.CommittedPostProcessingFailed => (
                "ClientRelease.Publish.CommittedPostProcessingFailed",
                true,
                "Host bundle publish commit completed before response post-processing failed.",
                (string?)null),
            HostPublishAuditOutcome.CommitConflict => (
                "ClientRelease.Publish.CommitConflict",
                false,
                "Host bundle publish observation found a conflicting persisted state.",
                "persisted-state-mismatch"),
            _ => (
                "ClientRelease.Publish.CommitUnknown",
                false,
                "Host bundle publish commit could not be confirmed by the bounded independent batch observation.",
                "commit-state-not-observed")
        };
        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                operationType,
                "EdgeReleaseBundle",
                $"{manifest.Channel.Trim()}/{manifest.Version.Trim()}",
                DateTime.UtcNow,
                succeeded,
                summary,
                failureReason),
            CancellationToken.None);
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
            if (!ClientReleaseFileFacts.IsStrictChildPath(extractRoot, targetPath))
            {
                throw new ClientReleaseValidationException("Edge 发布包包含非法路径。");
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
                throw new ClientReleaseValidationException("Edge 发布包不允许包含符号链接或重解析点。");
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
            || manifest.Modules is not { Count: > 0 })
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

        var moduleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pluginDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in manifest.Modules)
        {
            if (module is null
                || string.IsNullOrWhiteSpace(module.ModuleId)
                || string.IsNullOrWhiteSpace(module.Version)
                || !SemVerPattern.IsMatch(module.Version)
                || string.IsNullOrWhiteSpace(module.HostApiVersion)
                || string.IsNullOrWhiteSpace(module.MinHostVersion)
                || string.IsNullOrWhiteSpace(module.MaxHostVersion)
                || string.IsNullOrWhiteSpace(module.PluginDirectory)
                || !IsSafeRelativePath(module.PluginDirectory))
            {
                return "Edge 发布包 manifest 包含非法插件声明。";
            }

            if (!moduleIds.Add(module.ModuleId.Trim()))
            {
                return "Edge 发布包 manifest 包含重复的插件 moduleId。";
            }

            var normalizedPluginDirectory = module.PluginDirectory
                .Replace('\\', '/')
                .Trim('/');
            if (!pluginDirectories.Add(normalizedPluginDirectory))
            {
                return "Edge 发布包 manifest 包含重复的插件目录。";
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

        if (!File.Exists(Path.Combine(velopackRoot, "RELEASES"))
            && !Directory.EnumerateFiles(velopackRoot, "RELEASES-*", SearchOption.TopDirectoryOnly).Any())
        {
            return "Edge 发布包缺少 Velopack RELEASES 清单。";
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
            || !string.Equals(ClientReleaseFileFacts.ComputeSha256(installerPath), manifest.InstallerStubSha256, StringComparison.OrdinalIgnoreCase)
            || new FileInfo(installerPath).Length != manifest.InstallerStubSize)
        {
            return "Edge 发布包安装器 sha256 或 size 与 manifest 不一致。";
        }

        var hostDirectory = Path.Combine(installerRoot, manifest.HostDirectory);
        if (!IsSha256(manifest.HostDirectorySha256)
            || !string.Equals(ClientReleaseFileFacts.ComputeDirectorySha256(hostDirectory), manifest.HostDirectorySha256, StringComparison.OrdinalIgnoreCase)
            || ClientReleaseFileFacts.GetDirectorySize(hostDirectory) != manifest.HostDirectorySize)
        {
            return "Edge 发布包 host 目录 sha256 或 size 与 manifest 不一致。";
        }

        if (!string.IsNullOrWhiteSpace(manifest.VelopackSetupFile))
        {
            var setupPath = Path.Combine(installerRoot, manifest.VelopackSetupFile);
            if (!IsSha256(manifest.VelopackSetupSha256)
                || !string.Equals(ClientReleaseFileFacts.ComputeSha256(setupPath), manifest.VelopackSetupSha256, StringComparison.OrdinalIgnoreCase)
                || new FileInfo(setupPath).Length != manifest.VelopackSetupSize)
            {
                return "Edge 发布包 Velopack Setup sha256 或 size 与 manifest 不一致。";
            }
        }

        foreach (var module in manifest.Modules)
        {
            var pluginDirectory = Path.Combine(installerRoot, manifest.PluginsRoot, module.PluginDirectory);
            if (!IsSha256(module.PluginSha256)
                || !string.Equals(ClientReleaseFileFacts.ComputeDirectorySha256(pluginDirectory), module.PluginSha256, StringComparison.OrdinalIgnoreCase)
                || ClientReleaseFileFacts.GetDirectorySize(pluginDirectory) != module.PluginSize)
            {
                return $"Edge 发布包插件 {module.ModuleId} sha256 或 size 与 manifest 不一致。";
            }
        }

        return null;
    }

    private async Task<IReadOnlyDictionary<string, PluginPackagePublishArtifact>> PublishMissingPluginPackagesAsync(
        string edgeRoot,
        EdgeInstallerArtifactManifest manifest,
        string installerTarget,
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var published = new Dictionary<string, PluginPackagePublishArtifact>(StringComparer.OrdinalIgnoreCase);
        var stagingPackagesRoot = Path.Combine(stagingRoot, "plugin-packages");
        Directory.CreateDirectory(stagingPackagesRoot);

        foreach (var module in manifest.Modules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await ReleaseVersionExistsAsync(
                ClientReleaseComponentKind.Plugin,
                module.ModuleId,
                manifest.Channel,
                manifest.TargetRuntime,
                module.Version,
                cancellationToken))
            {
                continue;
            }

            var pluginDirectory = Path.Combine(installerTarget, manifest.PluginsRoot, module.PluginDirectory);
            if (!Directory.Exists(pluginDirectory))
            {
                throw new ClientReleaseValidationException("Edge 发布包缺少声明的插件目录。");
            }

            var pluginManifestPath = Path.Combine(pluginDirectory, "plugin.json");
            if (!File.Exists(pluginManifestPath))
            {
                throw new ClientReleaseValidationException("Edge 发布包插件目录缺少 plugin.json。");
            }

            var packageFileName = BuildPluginPackageFileName(module.ModuleId, module.Version, manifest.TargetRuntime);
            var stagingPackagePath = Path.Combine(stagingPackagesRoot, packageFileName);
            if (File.Exists(stagingPackagePath))
            {
                throw new ClientReleaseValidationException("Edge 发布包包含重复的插件模块声明。");
            }

            ZipFile.CreateFromDirectory(
                pluginDirectory,
                stagingPackagePath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);

            var sha256 = ClientReleaseFileFacts.ComputeSha256(stagingPackagePath);
            var size = new FileInfo(stagingPackagePath).Length;
            var packageDirectory = Path.Combine(
                edgeRoot,
                "plugins",
                manifest.Channel,
                EscapeFileSystemSegment(module.ModuleId),
                module.Version);
            if (Directory.Exists(packageDirectory))
            {
                throw new ClientReleasePublishConflictException();
            }

            var fileTransaction = new PluginReleasePublishFileTransaction(
                edgeRoot,
                packageDirectory,
                packageFileName,
                sha256,
                size,
                logger);
            fileTransaction.Publish(stagingPackagePath);
            published[module.ModuleId] = new PluginPackagePublishArtifact(
                module.ModuleId,
                module.Version,
                fileTransaction.TargetPackagePath,
                BuildPluginDownloadUrl(manifest.Channel, module.ModuleId, module.Version, packageFileName),
                sha256,
                size,
                fileTransaction);
        }

        return published;
    }

    private async Task<IReadOnlyList<ClientReleaseExpectedVersionState>> PrepareDatabaseRowsAsync(
        EdgeInstallerArtifactManifest manifest,
        string manifestDownloadUrl,
        IReadOnlyDictionary<string, PluginPackagePublishArtifact> pluginPackages,
        string installerTarget,
        IReadOnlyList<VelopackPublishedFile> velopackFiles,
        CancellationToken cancellationToken)
    {
        if (await ReleaseVersionExistsAsync(
            ClientReleaseComponentKind.Host,
            ClientReleaseComponent.HostComponentKey,
            manifest.Channel,
            manifest.TargetRuntime,
            manifest.Version,
            cancellationToken))
        {
            throw new ClientReleasePublishConflictException();
        }

        var expected = new List<ClientReleaseExpectedVersionState>(pluginPackages.Count + 1);

        var hostComponent = await componentRepository.GetSingleOrDefaultAsync(
            new ClientReleaseComponentRootByIdentitySpec(
                ClientReleaseComponentKind.Host,
                ClientReleaseComponent.HostComponentKey,
                manifest.Channel,
                manifest.TargetRuntime),
            cancellationToken);

        if (hostComponent is null)
        {
            hostComponent = ClientReleaseComponent.CreateHost(
                manifest.Channel,
                manifest.TargetRuntime);
            componentRepository.Add(hostComponent);
        }

        hostComponent.UpdateHostMetadata();
        var manifestFact = ClientReleaseFileFacts.GetFileFact(
            Path.Combine(installerTarget, "installer-artifact.json"));
        var installerStubFact = ClientReleaseFileFacts.GetFileFact(
            Path.Combine(installerTarget, manifest.InstallerStubFile));
        var hostArtifacts = ClientReleaseArtifactBuilder
            .FromPublishedHostFiles(
                manifestDownloadUrl,
                manifest.Channel,
                manifest.Version,
                manifestFact,
                manifest.InstallerStubFile,
                installerStubFact)
            .Concat(velopackFiles.Select(file =>
                ClientReleaseArtifactBuilder.VelopackFile(
                    manifest.Channel,
                    file.RelativePath,
                    file.Sha256,
                    file.Size)))
            .ToList();
        var hostVersion = hostComponent.UpsertHostVersion(
            manifest.Version,
            manifest.HostApiVersion,
            manifest.TargetFramework,
            manifestDownloadUrl,
            manifest.InstallerStubSha256!,
            manifest.InstallerStubSize,
            manifest.ReleaseNotes,
            ClientReleaseStatus.Published,
            null,
            "IIoT",
            manifest.GeneratedAtUtc,
            hostArtifacts);
        expected.Add(ClientReleaseExpectedVersionState.From(hostComponent, hostVersion));

        foreach (var module in manifest.Modules)
        {
            if (!pluginPackages.TryGetValue(module.ModuleId, out var package))
            {
                continue;
            }

            var pluginComponent = await componentRepository.GetSingleOrDefaultAsync(
                new ClientReleaseComponentRootByIdentitySpec(
                    ClientReleaseComponentKind.Plugin,
                    module.ModuleId,
                    manifest.Channel,
                    manifest.TargetRuntime),
                cancellationToken);

            if (pluginComponent is null)
            {
                pluginComponent = ClientReleaseComponent.CreatePlugin(
                    module.ModuleId,
                    string.IsNullOrWhiteSpace(module.DisplayName) ? module.ModuleId : module.DisplayName,
                    module.Description,
                    null,
                    null,
                    manifest.Channel,
                    manifest.TargetRuntime);
                componentRepository.Add(pluginComponent);
            }
            else
            {
                pluginComponent.UpdatePluginMetadata(
                    string.IsNullOrWhiteSpace(module.DisplayName) ? module.ModuleId : module.DisplayName,
                    module.Description,
                    null,
                    null);
            }

            if (pluginComponent.FindVersion(module.Version) is not null)
            {
                throw new ClientReleasePublishConflictException();
            }

            var pluginVersion = pluginComponent.UpsertPluginVersion(
                module.Version,
                module.HostApiVersion,
                module.MinHostVersion,
                module.MaxHostVersion,
                manifest.TargetFramework,
                package.DownloadUrl,
                package.Sha256,
                package.PackageSize,
                manifest.ReleaseNotes,
                "[]",
                ClientReleaseStatus.Published,
                null,
                "IIoT",
                manifest.GeneratedAtUtc,
                ClientReleaseArtifactBuilder.FromPluginDownloadUrl(
                    package.DownloadUrl,
                    manifest.Channel,
                    module.ModuleId,
                    module.Version,
                    package.Sha256,
                    package.PackageSize));
            expected.Add(ClientReleaseExpectedVersionState.From(pluginComponent, pluginVersion));
        }

        return expected;
    }

    private async Task<bool> ReleaseVersionExistsAsync(
        ClientReleaseComponentKind componentKind,
        string componentKey,
        string channel,
        string targetRuntime,
        string version,
        CancellationToken cancellationToken)
    {
        var normalizedComponentKey = componentKey.Trim();
        var normalizedChannel = channel.Trim();
        var normalizedTargetRuntime = targetRuntime.Trim();
        var normalizedVersion = version.Trim();
        return await componentRepository.AnyAsync(
            component =>
                component.ComponentKind == componentKind
                && component.ComponentKey == normalizedComponentKey
                && component.Channel == normalizedChannel
                && component.TargetRuntime == normalizedTargetRuntime
                && component.Versions.Any(release => release.Version == normalizedVersion),
            cancellationToken);
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
        var components = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(channel, targetRuntime, onlyPublished: false, includeArchived: true),
            cancellationToken);
        var archivedVersions = components
            .Where(component => component.ComponentKind == ClientReleaseComponentKind.Host)
            .SelectMany(component => component.Versions)
            .Where(version => version.Status == ClientReleaseStatus.Archived)
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
            if (ClientReleaseVelopackPaths.IsChannelManifest(name))
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
            throw new ClientReleaseValidationException(
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
            $"/edge-updates/velopack/{channel}/RELEASES",
            $"/edge-updates/velopack/{channel}/releases.{channel}.json",
            $"/edge-updates/velopack/{channel}/assets.{channel}.json"
        };
        urls.AddRange(pluginPackages.Select(package => package.DownloadUrl));
        return urls;
    }

    private static IReadOnlyList<string> BuildRequiredPublishedHostFiles(
        string installerTarget,
        string velopackTarget,
        EdgeInstallerArtifactManifest manifest)
    {
        var files = new List<string>
        {
            Path.Combine(installerTarget, "installer-artifact.json"),
            Path.Combine(installerTarget, manifest.InstallerStubFile),
            Path.Combine(velopackTarget, "RELEASES")
        };
        files.AddRange(RequiredVelopackManifestNames.Select(name => Path.Combine(velopackTarget, name)));

        var currentVersionNupkg = Directory
            .EnumerateFiles(velopackTarget, "*.nupkg", SearchOption.TopDirectoryOnly)
            .Where(file => FileNameContainsVersion(Path.GetFileName(file), manifest.Version))
            .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(file =>
            {
                var fileName = Path.GetFileName(file);
                return !string.IsNullOrWhiteSpace(fileName)
                    && VelopackManifestsReferenceFile(velopackTarget, fileName);
            });
        if (currentVersionNupkg is null)
        {
            throw new ClientReleaseValidationException(
                "Edge 发布版本缺少被 Velopack manifests 引用的 .nupkg，拒绝置为 Published。");
        }

        files.Add(currentVersionNupkg);
        return files;
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
                throw new ClientReleaseValidationException("Edge 发布包包含禁止上传的现场数据或密钥文件。");
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
                    throw new ClientReleaseValidationException($"Edge 发布包配置包含真实 CloudApi:{key}。");
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
            throw new ClientReleaseValidationException("Edge 发布包包含非法 zip 路径。");
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

    private static bool IsSha256(string? value)
        => !string.IsNullOrWhiteSpace(value) && Sha256Pattern.IsMatch(value);

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
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
        long PackageSize,
        PluginReleasePublishFileTransaction FileTransaction);

    private enum HostPublishAuditOutcome
    {
        PreflightConflict,
        CommitRecovered,
        CommittedResponseCancelled,
        CommittedPostProcessingFailed,
        CommitConflict,
        CommitUnknown
    }
}
