using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
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
public sealed record PublishEdgePluginPackageCommand()
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
    ClientReleaseUploadCoordinator uploadCoordinator,
    IRepository<ClientReleaseComponent> componentRepository,
    IClientReleaseVersionObservationReader observationReader,
    IClientReleaseRetentionService retentionService,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService,
    ILogger<PublishEdgePluginPackageHandler> logger)
    : ICommandHandler<PublishEdgePluginPackageCommand, Result<EdgePluginPackagePublishResultDto>>
{
    private const string ManifestFileName = "plugin-release.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

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
        PublishEdgePluginPackageCommand _,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var uploadSession = uploadCoordinator.Begin(ClientReleaseUploadKind.PluginPackage);

        var edgeRoot = uploadSession.EdgeRoot;
        var stagingRoot = uploadSession.StagingRoot;
        var wrapperPath = uploadSession.UploadedFilePath;
        var extractRoot = Path.Combine(stagingRoot, "extracted");
        PluginReleasePublishFileTransaction? fileTransaction = null;
        ClientReleaseVersionIdentity? releaseIdentity = null;
        ClientReleaseExpectedVersionState? expectedState = null;
        var saveChangesInvoked = false;
        var saveChangesReturned = false;
        var stableOutcomeAuditWritten = false;

        try
        {
            var copiedBytes = await uploadSession.ReceiveAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ClientReleaseFileFacts.AssertFreeDiskSpace(edgeRoot, copiedBytes, "Edge 插件发布");
            ClientReleaseZipArchive.ExtractToDirectory(
                wrapperPath,
                extractRoot,
                "Edge 插件发布包");
            cancellationToken.ThrowIfCancellationRequested();
            var loadResult = await LoadAndValidateAsync(extractRoot, cancellationToken);
            if (!loadResult.IsSuccess)
            {
                return await FailAsync(loadResult.Error!, cancellationToken);
            }

            var metadata = loadResult.Metadata!;
            var packagePath = loadResult.PackagePath!;
            releaseIdentity = new ClientReleaseVersionIdentity(
                ClientReleaseComponentKind.Plugin,
                metadata.ModuleId.Trim(),
                metadata.Channel.Trim(),
                metadata.TargetRuntime.Trim(),
                metadata.Version.Trim());
            var component = await componentRepository.GetSingleOrDefaultAsync(
                new ClientReleaseComponentByIdentitySpec(
                    releaseIdentity.ComponentKind,
                    releaseIdentity.ComponentKey,
                    releaseIdentity.Channel,
                    releaseIdentity.TargetRuntime),
                cancellationToken);
            if (component?.FindVersion(releaseIdentity.Version) is not null)
            {
                throw new ClientReleasePublishConflictException();
            }

            var packageTargetDirectory = Path.Combine(
                edgeRoot,
                "plugins",
                releaseIdentity.Channel,
                ClientReleaseArtifactBuilder.EscapePathSegment(releaseIdentity.ComponentKey),
                ClientReleaseArtifactBuilder.EscapePathSegment(releaseIdentity.Version));
            if (Directory.Exists(packageTargetDirectory))
            {
                throw new ClientReleasePublishConflictException();
            }

            fileTransaction = new PluginReleasePublishFileTransaction(
                edgeRoot,
                packageTargetDirectory,
                metadata.PackageFileName,
                metadata.Sha256,
                metadata.PackageSize,
                logger);
            fileTransaction.Publish(packagePath);
            EdgeReleasePublishedFilePermissions.EnsureGatewayReadable(
                edgeRoot,
                [packageTargetDirectory],
                [fileTransaction.TargetPackagePath]);
            EdgeReleasePublishedFilePermissions.AssertPublishedPathsReady(
                edgeRoot,
                [packageTargetDirectory],
                [fileTransaction.TargetPackagePath]);
            cancellationToken.ThrowIfCancellationRequested();
            var downloadUrl = ClientReleaseArtifactBuilder.BuildPluginDownloadUrl(
                releaseIdentity.Channel,
                releaseIdentity.ComponentKey,
                releaseIdentity.Version,
                metadata.PackageFileName);
            var artifacts = ClientReleaseArtifactBuilder.FromPluginDownloadUrl(
                downloadUrl,
                releaseIdentity.Channel,
                releaseIdentity.ComponentKey,
                releaseIdentity.Version,
                metadata.Sha256,
                metadata.PackageSize);
            expectedState = BuildExpectedState(metadata, releaseIdentity, downloadUrl, artifacts);
            if (component is null)
            {
                component = ClientReleaseComponent.CreatePlugin(
                    releaseIdentity.ComponentKey,
                    expectedState.DisplayName,
                    expectedState.Description,
                    expectedState.IconKind,
                    expectedState.AccentColor,
                    releaseIdentity.Channel,
                    releaseIdentity.TargetRuntime);
                componentRepository.Add(component);
            }
            else
            {
                component.UpdatePluginMetadata(
                    expectedState.DisplayName,
                    expectedState.Description,
                    expectedState.IconKind,
                    expectedState.AccentColor);
            }

            component.UpsertPluginVersion(
                releaseIdentity.Version,
                expectedState.HostApiVersion,
                expectedState.MinHostVersion!,
                expectedState.MaxHostVersion!,
                expectedState.TargetFramework,
                expectedState.DownloadUrl,
                expectedState.Sha256,
                expectedState.PackageSize,
                expectedState.ReleaseNotes,
                expectedState.DependenciesJson,
                ClientReleaseStatus.Published,
                expectedState.Signature,
                expectedState.Publisher,
                expectedState.PublishedAtUtc,
                artifacts);
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
                    ClientReleasePublishDiagnostics.PluginPublishFailed,
                    "plugin-save-response",
                    ex,
                    "plugin-release");
                var outcome = await PluginReleaseCommitRecovery.ObserveAsync(
                    observationReader,
                    expectedState,
                    fileTransaction,
                    logger);
                switch (outcome)
                {
                    case PluginReleaseCommitObservationOutcome.Committed:
                        {
                            var markerWarning = fileTransaction.TryRemoveOwnershipMarker()
                                ? null
                                : "插件发布已确认，但发布所有权标记未完成清理。";
                            stopwatch.Stop();
                            var recoveredResult = BuildResult(
                                expectedState,
                                stopwatch.Elapsed,
                                uploadSession.MaxUploadMbps,
                                ClientReleasePublishWarnings.Combine(
                                    "插件发布已确认，但保留/清理旧版本未执行。",
                                    markerWarning));
                            await WriteStableOutcomeAuditAsync(
                                releaseIdentity,
                                PluginPublishAuditOutcome.CommitRecovered);
                            stableOutcomeAuditWritten = true;
                            return Result.Success(recoveredResult);
                        }
                    case PluginReleaseCommitObservationOutcome.Conflict:
                        await WriteStableOutcomeAuditAsync(
                            releaseIdentity,
                            PluginPublishAuditOutcome.CommitConflict);
                        stableOutcomeAuditWritten = true;
                        throw new ClientReleasePublishConflictException();
                    default:
                        await WriteStableOutcomeAuditAsync(
                            releaseIdentity,
                            PluginPublishAuditOutcome.CommitUnknown);
                        stableOutcomeAuditWritten = true;
                        throw new ClientReleaseCommitUnknownException();
                }
            }

            var markerCleanupWarning = fileTransaction.TryRemoveOwnershipMarker()
                ? null
                : "插件发布成功，但发布所有权标记未完成清理。";
            cancellationToken.ThrowIfCancellationRequested();

            var cleanupWarning = markerCleanupWarning;
            try
            {
                await retentionService.ApplyPluginPolicyAsync(
                    releaseIdentity.ComponentKey,
                    releaseIdentity.Channel,
                    releaseIdentity.TargetRuntime,
                    cancellationToken);
                var components = await componentRepository.GetListAsync(
                    new ClientReleaseComponentsByChannelSpec(
                        releaseIdentity.Channel,
                        releaseIdentity.TargetRuntime,
                        onlyPublished: false,
                        includeArchived: true),
                    cancellationToken);
                ClientReleaseArchivedFileCleanup.DeletePluginDirectories(edgeRoot, components);
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
                    ClientReleasePublishDiagnostics.PluginRetentionCleanupFailed,
                    "plugin-retention-cleanup",
                    ex,
                    "plugin-release");
                cleanupWarning = ClientReleasePublishWarnings.Combine(
                    cleanupWarning,
                    "插件发布成功，但保留/清理旧版本未完成。");
            }

            stopwatch.Stop();
            var result = BuildResult(
                expectedState,
                stopwatch.Elapsed,
                uploadSession.MaxUploadMbps,
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
                fileTransaction?.TryRollbackBeforeSave();
                throw;
            }

            if (releaseIdentity is not null && expectedState is not null && fileTransaction is not null)
            {
                var outcome = await PluginReleaseCommitRecovery.ObserveAsync(
                    observationReader,
                    expectedState,
                    fileTransaction,
                    logger);
                if (outcome == PluginReleaseCommitObservationOutcome.Committed)
                {
                    fileTransaction.TryRemoveOwnershipMarker();
                }

                await WriteStableOutcomeAuditAsync(
                    releaseIdentity,
                    outcome switch
                    {
                        PluginReleaseCommitObservationOutcome.Committed => PluginPublishAuditOutcome.CommittedResponseCancelled,
                        PluginReleaseCommitObservationOutcome.Conflict => PluginPublishAuditOutcome.CommitConflict,
                        _ => PluginPublishAuditOutcome.CommitUnknown
                    });
            }

            throw;
        }
        catch (ClientReleasePublishConflictException)
        {
            if (!stableOutcomeAuditWritten && releaseIdentity is not null)
            {
                await WriteStableOutcomeAuditAsync(
                    releaseIdentity,
                    PluginPublishAuditOutcome.PreflightConflict);
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
                ClientReleasePublishDiagnostics.PluginPublishFailed,
                "plugin-publish",
                ex,
                "plugin-release");
            if (!saveChangesInvoked)
            {
                var rollbackSucceeded = fileTransaction?.TryRollbackBeforeSave() ?? true;
                if (ex is ClientReleaseValidationException or InvalidDataException)
                {
                    var failureMessage = FormatValidationFailure(ex);
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

            if (saveChangesReturned && expectedState is not null && fileTransaction is not null)
            {
                var markerWarning = fileTransaction.TryRemoveOwnershipMarker()
                    ? null
                    : "插件发布已提交，但发布所有权标记未完成清理。";
                stopwatch.Stop();
                var result = BuildResult(
                    expectedState,
                    stopwatch.Elapsed,
                    uploadSession.MaxUploadMbps,
                    ClientReleasePublishWarnings.Combine("插件发布已提交，但响应后处理未完成。", markerWarning));
                if (releaseIdentity is not null)
                {
                    await WriteStableOutcomeAuditAsync(
                        releaseIdentity,
                        PluginPublishAuditOutcome.CommittedPostProcessingFailed);
                }

                return Result.Success(result);
            }

            if (!stableOutcomeAuditWritten && releaseIdentity is not null)
            {
                await WriteStableOutcomeAuditAsync(
                    releaseIdentity,
                    PluginPublishAuditOutcome.CommitUnknown);
            }

            throw new ClientReleaseCommitUnknownException();
        }
        async Task<Result<EdgePluginPackagePublishResultDto>> FailAsync(
            string message,
            CancellationToken token)
        {
            await WriteAuditAsync(null, uploadSession.AuditSource, succeeded: false, message, token);
            return Result.Invalid(message);
        }
    }

    private static ClientReleaseExpectedVersionState BuildExpectedState(
        PluginPackageReleaseManifest metadata,
        ClientReleaseVersionIdentity identity,
        string downloadUrl,
        IReadOnlyList<ClientReleaseArtifact> artifacts)
    {
        var displayName = metadata.DisplayName.Trim();
        var publisher = string.IsNullOrWhiteSpace(metadata.Publisher)
            ? "IIoT"
            : metadata.Publisher.Trim();
        return new ClientReleaseExpectedVersionState(
            identity,
            displayName,
            ClientReleaseText.NormalizeOptional(metadata.Description),
            ClientReleaseText.NormalizeOptional(metadata.IconKind),
            ClientReleaseText.NormalizeOptional(metadata.AccentColor),
            metadata.HostApiVersion.Trim(),
            metadata.MinHostVersion.Trim(),
            metadata.MaxHostVersion.Trim(),
            ClientReleaseText.NormalizeOptional(metadata.TargetFramework),
            downloadUrl,
            metadata.Sha256.Trim(),
            metadata.PackageSize,
            ClientReleaseText.NormalizeOptional(metadata.ReleaseNotes),
            JsonSerializer.Serialize(metadata.Dependencies ?? [], JsonOptions),
            ClientReleaseText.NormalizeOptional(metadata.Signature),
            publisher,
            NormalizePublishedAtUtc(metadata.CreatedAtUtc),
            artifacts
                .Select(artifact => new ClientReleaseArtifactObservation(
                    artifact.ArtifactKind,
                    artifact.RelativePath,
                    artifact.Sha256,
                    artifact.Size))
                .ToList());
    }

    private static EdgePluginPackagePublishResultDto BuildResult(
        ClientReleaseExpectedVersionState expected,
        TimeSpan elapsed,
        int uploadRateLimitMbps,
        string? cleanupWarning)
    {
        return new EdgePluginPackagePublishResultDto(
            expected.Identity.ComponentKey,
            expected.DisplayName,
            expected.Identity.Channel,
            expected.Identity.Version,
            expected.HostApiVersion,
            expected.MinHostVersion!,
            expected.MaxHostVersion!,
            expected.Identity.TargetRuntime,
            expected.TargetFramework,
            expected.DownloadUrl,
            expected.Sha256,
            expected.PackageSize,
            elapsed.TotalSeconds,
            uploadRateLimitMbps,
            [expected.DownloadUrl],
            cleanupWarning);
    }

    private async Task WriteStableOutcomeAuditAsync(
        ClientReleaseVersionIdentity identity,
        PluginPublishAuditOutcome outcome)
    {
        var (operationType, succeeded, summary, failureReason) = outcome switch
        {
            PluginPublishAuditOutcome.PreflightConflict => (
                "ClientRelease.PublishPlugin.Conflict",
                false,
                "Plugin publish was rejected because the target version or directory already exists.",
                "target-already-exists"),
            PluginPublishAuditOutcome.CommitRecovered => (
                "ClientRelease.PublishPlugin.CommitRecovered",
                true,
                "Plugin publish commit was confirmed by one bounded independent observation after the save response failed.",
                (string?)null),
            PluginPublishAuditOutcome.CommittedResponseCancelled => (
                "ClientRelease.PublishPlugin.CommittedResponseCancelled",
                true,
                "Plugin publish commit was confirmed after response cancellation or lease loss.",
                (string?)null),
            PluginPublishAuditOutcome.CommittedPostProcessingFailed => (
                "ClientRelease.PublishPlugin.CommittedPostProcessingFailed",
                true,
                "Plugin publish commit completed before response post-processing failed.",
                (string?)null),
            PluginPublishAuditOutcome.CommitConflict => (
                "ClientRelease.PublishPlugin.CommitConflict",
                false,
                "Plugin publish commit observation found a conflicting persisted state.",
                "persisted-state-mismatch"),
            _ => (
                "ClientRelease.PublishPlugin.CommitUnknown",
                false,
                "Plugin publish commit could not be confirmed by the bounded independent observation.",
                "commit-state-not-observed")
        };
        var target = $"{identity.Channel}/{identity.ComponentKey}/{identity.Version}";
        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ClientReleaseAuditActor.ParseId(currentUser.Id),
                currentUser.UserName,
                operationType,
                "EdgePluginPackage",
                target,
                DateTime.UtcNow,
                succeeded,
                summary,
                failureReason),
            CancellationToken.None);
    }

    private static DateTime? NormalizePublishedAtUtc(DateTime? value)
        => value?.Kind switch
        {
            null => null,
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => throw new ClientReleaseValidationException(
                "Edge 插件发布包 createdAtUtc 必须包含 UTC 标记或明确时区偏移。")
        };

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

        if (!ClientReleaseFileFacts.IsExactRegularFile(
                packagePath,
                manifest.Sha256,
                manifest.PackageSize))
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

        if (!ClientReleaseFileFacts.IsSha256(manifest.Sha256) || manifest.PackageSize <= 0)
        {
            return "Edge 插件发布包 sha256 或 size 非法。";
        }

        if (manifest.CreatedAtUtc is { Kind: DateTimeKind.Unspecified })
        {
            return "Edge 插件发布包 createdAtUtc 必须包含 UTC 标记或明确时区偏移。";
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
            var normalized = ClientReleaseZipArchive.NormalizeEntryPath(
                entry.FullName,
                "Edge 插件 zip");
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
            throw new ClientReleaseValidationException("Edge 插件 zip 的配置文件无法解析。");
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
                ClientReleaseAuditActor.ParseId(currentUser.Id),
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

    private static string FormatValidationFailure(Exception ex)
        => ex switch
        {
            ClientReleaseValidationException validation => validation.SafeMessage,
            InvalidDataException => "Edge 插件发布包格式无效。",
            _ => "Edge 插件发布包无效。"
        };

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

    private enum PluginPublishAuditOutcome
    {
        PreflightConflict,
        CommitRecovered,
        CommittedResponseCancelled,
        CommittedPostProcessingFailed,
        CommitConflict,
        CommitUnknown
    }
}
