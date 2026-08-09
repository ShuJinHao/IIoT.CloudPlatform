using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    IDeviceClientStateStore clientStateStore,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService,
    IOptions<PluginReleaseSignatureOptions> signatureOptions,
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

            component.ConfigurePluginContract(
                metadata.ProcessType,
                metadata.BusinessDocumentRef,
                metadata.PackageSchemaVersion,
                metadata.FileManifestSha256,
                metadata.DataCapabilitiesJson);

            var releaseVersion = component.UpsertPluginVersion(
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
            releaseVersion.ConfigurePluginManifest(
                metadata.DataCapabilitiesJson,
                metadata.FileManifestSha256,
                metadata.DependencyClosureSha256,
                metadata.DependencyHostVersion,
                metadata.DependencyHostFileManifestSha256);
            await ClientReleasePublishedLimit.EnforceBeforeCommitAsync(
                retentionService,
                clientStateStore,
                [component],
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
                .ToList(),
            metadata.DataCapabilitiesJson,
            metadata.FileManifestSha256,
            metadata.DependencyClosureSha256,
            metadata.DependencyHostVersion,
            metadata.DependencyHostFileManifestSha256);
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

        var signatureError = VerifyReleaseSignature(
            manifest,
            signatureOptions.Value);
        if (signatureError is not null)
            return PluginPackageValidationResult.Fail(signatureError);

        var businessDocumentPath = Path.Combine(
            extractRoot,
            "evidence",
            "business-document.md");
        if (!File.Exists(businessDocumentPath)
            || !ClientReleaseFileFacts.IsExactRegularFile(
                businessDocumentPath,
                manifest.BusinessDocumentSha256,
                new FileInfo(businessDocumentPath).Length))
        {
            return PluginPackageValidationResult.Fail(
                "Edge 插件发布包业务文档证据缺失或摘要不一致。");
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
        if (packageError is not null)
            return PluginPackageValidationResult.Fail(packageError);
        manifest.Signature = JsonSerializer.Serialize(
            manifest.ReleaseSignature,
            JsonOptions);
        return PluginPackageValidationResult.Success(manifest, packagePath);
    }

    private static string? ValidateManifestBasics(PluginPackageReleaseManifest manifest)
    {
        if (manifest.PackageSchemaVersion != 3)
        {
            return "Edge 插件发布包 schemaVersion 不受支持。";
        }

        if (!string.Equals(manifest.Channel, "stable", StringComparison.OrdinalIgnoreCase))
        {
            return "生产服务器只允许发布 stable 插件渠道。";
        }

        if (string.IsNullOrWhiteSpace(manifest.ModuleId)
            || string.IsNullOrWhiteSpace(manifest.DisplayName)
            || string.IsNullOrWhiteSpace(manifest.ProcessType)
            || string.IsNullOrWhiteSpace(manifest.BusinessDocumentRef)
            || !ClientReleaseFileFacts.IsSha256(manifest.BusinessDocumentSha256)
            || !ClientReleaseSemanticVersion.IsValid(manifest.Version)
            || !ClientReleaseSemanticVersion.IsValid(manifest.HostApiVersion)
            || !ClientReleaseSemanticVersion.IsValid(manifest.MinHostVersion)
            || !ClientReleaseSemanticVersion.IsValid(manifest.MaxHostVersion)
            || ClientReleaseSemanticVersion.Compare(
                manifest.MinHostVersion,
                manifest.MaxHostVersion) > 0
            || string.IsNullOrWhiteSpace(manifest.TargetRuntime)
            || string.IsNullOrWhiteSpace(manifest.TargetFramework)
            || !ClientReleaseFileFacts.IsSha256(manifest.FileManifestSha256)
            || manifest.FileManifestFileCount <= 0
            || !string.Equals(
                manifest.DataCapabilitiesFileName,
                "data-capabilities.json",
                StringComparison.Ordinal)
            || !ClientReleaseFileFacts.IsSha256(manifest.DataCapabilitiesSha256)
            || !ClientReleaseFileFacts.IsSha256(manifest.DependencyClosureSha256)
            || manifest.DependencyCount <= 0
            || string.IsNullOrWhiteSpace(manifest.DependencyHostComponent)
            || !ClientReleaseSemanticVersion.IsValid(
                manifest.DependencyHostVersion)
            || !ClientReleaseFileFacts.IsSha256(
                manifest.DependencyHostFileManifestSha256)
            || ClientReleaseSemanticVersion.Compare(
                manifest.DependencyHostVersion,
                manifest.MinHostVersion) < 0
            || ClientReleaseSemanticVersion.Compare(
                manifest.DependencyHostVersion,
                manifest.MaxHostVersion) > 0
            || string.IsNullOrWhiteSpace(manifest.SourceCommit)
            || string.IsNullOrWhiteSpace(manifest.Publisher)
            || manifest.ReleaseSignature is null
            || !string.Equals(
                manifest.ReleaseSignature.Algorithm,
                EdgePayloadManifestContract.Algorithm,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.ReleaseSignature.KeyId)
            || string.IsNullOrWhiteSpace(manifest.ReleaseSignature.Value)
            || string.IsNullOrWhiteSpace(manifest.PackageFileName)
            || string.IsNullOrWhiteSpace(manifest.ReleaseNotes)
            || manifest.CreatedAtUtc is null
            || manifest.Dependencies is null
            || manifest.Dependencies.Any(string.IsNullOrWhiteSpace))
        {
            return "Edge 插件发布包 manifest 不完整。";
        }

        if (!manifest.PackageFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || !IsSafeRelativeFileName(manifest.PackageFileName))
        {
            return "Edge 插件发布包文件名非法。";
        }

        if (!IsCanonicalSha256(manifest.Sha256)
            || !IsCanonicalSha256(manifest.FileManifestSha256)
            || !IsCanonicalSha256(manifest.DataCapabilitiesSha256)
            || !IsCanonicalSha256(manifest.DependencyClosureSha256)
            || !IsCanonicalSha256(
                manifest.DependencyHostFileManifestSha256)
            || !IsCanonicalSha256(manifest.BusinessDocumentSha256)
            || manifest.PackageSize <= 0)
        {
            return "Edge 插件发布包 sha256 或 size 非法。";
        }

        if (manifest.CreatedAtUtc is { Kind: DateTimeKind.Unspecified })
        {
            return "Edge 插件发布包 createdAtUtc 必须包含 UTC 标记或明确时区偏移。";
        }

        return null;
    }

    internal static byte[] CanonicalizeReleaseSignature(
        PluginPackageReleaseManifest manifest)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteNumber("packageSchemaVersion", manifest.PackageSchemaVersion);
            writer.WriteString("channel", manifest.Channel);
            writer.WriteString("moduleId", manifest.ModuleId);
            writer.WriteString("processType", manifest.ProcessType);
            writer.WriteString("displayName", manifest.DisplayName);
            WriteNullableString(writer, "description", manifest.Description);
            WriteNullableString(writer, "iconKind", manifest.IconKind);
            WriteNullableString(writer, "accentColor", manifest.AccentColor);
            writer.WriteString("version", manifest.Version);
            writer.WriteString("hostApiVersion", manifest.HostApiVersion);
            writer.WriteString("minHostVersion", manifest.MinHostVersion);
            writer.WriteString("maxHostVersion", manifest.MaxHostVersion);
            writer.WriteStartArray("dependencies");
            foreach (var dependency in manifest.Dependencies!)
                writer.WriteStringValue(dependency);
            writer.WriteEndArray();
            writer.WriteString("targetRuntime", manifest.TargetRuntime);
            writer.WriteString("targetFramework", manifest.TargetFramework);
            writer.WriteString("packageFileName", manifest.PackageFileName);
            writer.WriteNumber("packageSize", manifest.PackageSize);
            writer.WriteString("sha256", manifest.Sha256.ToLowerInvariant());
            writer.WriteString("publisher", manifest.Publisher);
            writer.WriteString("sourceCommit", manifest.SourceCommit);
            writer.WriteString(
                "businessDocumentRef",
                manifest.BusinessDocumentRef);
            writer.WriteString(
                "businessDocumentSha256",
                manifest.BusinessDocumentSha256.ToLowerInvariant());
            writer.WriteString(
                "fileManifestSha256",
                manifest.FileManifestSha256!.ToLowerInvariant());
            writer.WriteNumber(
                "fileManifestFileCount",
                manifest.FileManifestFileCount);
            writer.WriteString(
                "dataCapabilitiesFileName",
                manifest.DataCapabilitiesFileName);
            writer.WriteString(
                "dataCapabilitiesSha256",
                manifest.DataCapabilitiesSha256.ToLowerInvariant());
            writer.WriteString(
                "dependencyClosureSha256",
                manifest.DependencyClosureSha256.ToLowerInvariant());
            writer.WriteNumber(
                "dependencyCount",
                manifest.DependencyCount);
            writer.WriteString(
                "dependencyHostComponent",
                manifest.DependencyHostComponent);
            writer.WriteString(
                "dependencyHostVersion",
                manifest.DependencyHostVersion);
            writer.WriteString(
                "dependencyHostFileManifestSha256",
                manifest.DependencyHostFileManifestSha256.ToLowerInvariant());
            writer.WriteString("releaseNotes", manifest.ReleaseNotes);
            writer.WriteString(
                "createdAtUtc",
                FormatUtc(manifest.CreatedAtUtc!.Value));
            writer.WriteEndObject();
        }
        return memory.ToArray();
    }

    private static void WriteNullableString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is null)
            writer.WriteNull(propertyName);
        else
            writer.WriteString(propertyName, value);
    }

    private static string FormatUtc(DateTime value)
        => (value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime())
            .ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                System.Globalization.CultureInfo.InvariantCulture);

    private static bool IsCanonicalSha256(string? value)
        => ClientReleaseFileFacts.IsSha256(value)
           && string.Equals(value, value!.ToLowerInvariant(), StringComparison.Ordinal);

    private static string? VerifyReleaseSignature(
        PluginPackageReleaseManifest manifest,
        PluginReleaseSignatureOptions options)
    {
        PluginReleaseTrustedKeyRing? keyRing;
        try
        {
            var path = Path.GetFullPath(options.TrustedPublicKeysFile);
            if (!File.Exists(path))
                return "Edge 插件发布签名信任库不可用。";
            keyRing = JsonSerializer.Deserialize<PluginReleaseTrustedKeyRing>(
                File.ReadAllBytes(path),
                JsonOptions);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            return "Edge 插件发布签名信任库不可用。";
        }

        if (keyRing is null
            || keyRing.SchemaVersion != 1
            || keyRing.Keys is null
            || keyRing.Keys.GroupBy(candidate => candidate.KeyId, StringComparer.Ordinal)
                .Any(group => group.Count() != 1))
        {
            return "Edge 插件发布签名信任库无效。";
        }

        var signature = manifest.ReleaseSignature!;
        var key = keyRing?.Keys?.SingleOrDefault(candidate =>
            string.Equals(candidate.KeyId, signature.KeyId, StringComparison.Ordinal)
            && string.Equals(
                candidate.Algorithm,
                EdgePayloadManifestContract.Algorithm,
                StringComparison.Ordinal));
        if (key is null || string.IsNullOrWhiteSpace(key.PublicKeyPem))
            return "Edge 插件发布签名 keyId 未受信任。";

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(key.PublicKeyPem);
            if (rsa.KeySize < 2048)
                return "Edge 插件发布签名密钥强度不足。";
            var bytes = Convert.FromBase64String(signature.Value);
            if (!rsa.VerifyData(
                    CanonicalizeReleaseSignature(manifest),
                    bytes,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pss))
            {
                return "Edge 插件发布签名验证失败。";
            }
        }
        catch (Exception exception) when (
            exception is CryptographicException
                or FormatException
                or ArgumentException)
        {
            return "Edge 插件发布签名格式无效。";
        }

        return null;
    }

    private static string? ValidatePluginPackageZip(
        string packagePath,
        PluginPackageReleaseManifest manifest)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var normalizedEntries = new Dictionary<string, ZipArchiveEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var normalized = ClientReleaseZipArchive.NormalizeEntryPath(
                entry.FullName,
                "Edge 插件 zip");
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (!normalizedEntries.TryAdd(normalized, entry))
                return $"Edge 插件 zip 包含大小写重复路径: {normalized}";

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


        var fileManifestError = ValidatePluginFileManifest(
            normalizedEntries,
            manifest);
        if (fileManifestError is not null)
            return fileManifestError;

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

        var dependencyClosureError = ValidateDependencyClosure(
            normalizedEntries,
            manifest,
            pluginManifest.EntryAssembly);
        if (dependencyClosureError is not null)
            return dependencyClosureError;

        var capabilityEntry = archive.Entries.SingleOrDefault(entry =>
            entry.FullName.Equals(
                "data-capabilities.json",
                StringComparison.OrdinalIgnoreCase));
        if (capabilityEntry is null)
        {
            return "Edge 插件 zip 缺少 data-capabilities.json。";
        }
        var capabilityBytes = ReadEntryBytes(capabilityEntry);
        if (!string.Equals(
                Convert.ToHexString(SHA256.HashData(capabilityBytes)),
                manifest.DataCapabilitiesSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Edge 插件 data-capabilities.json 摘要与发布 manifest 不一致。";
        }
        if (capabilityEntry is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(capabilityBytes);
                if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion)
                    || schemaVersion.GetInt32() != 1
                    || !document.RootElement.TryGetProperty("moduleId", out var capabilityModuleId)
                    || capabilityModuleId.ValueKind != JsonValueKind.String
                    || !string.Equals(
                        capabilityModuleId.GetString()?.Trim(),
                        manifest.ModuleId,
                        StringComparison.Ordinal)
                    || !document.RootElement.TryGetProperty("capabilities", out var capabilities)
                    || capabilities.ValueKind != JsonValueKind.Array)
                {
                    return "Edge 插件 data-capabilities.json 格式不完整。";
                }

                var typeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var capability in capabilities.EnumerateArray())
                {
                    if (!capability.TryGetProperty("typeKey", out var typeKeyProperty)
                        || string.IsNullOrWhiteSpace(typeKeyProperty.GetString())
                        || !typeKeys.Add(typeKeyProperty.GetString()!.Trim())
                        || !TryGetNonEmptyString(capability, "displayName")
                        || !TryGetPositiveInt(capability, "schemaVersion")
                        || !TryGetNonEmptyString(capability, "schemaName")
                        || !TryGetNonEmptyString(capability, "scope")
                        || !TryGetStringArray(capability, "legacyTypeKeys", out var legacyTypeKeys)
                        || !TryGetStringArray(capability, "queryModes", out var queryModes)
                        || queryModes.Count == 0
                        || !TryGetStringArray(capability, "publicFields", out var publicFields)
                        || !capability.TryGetProperty("fields", out var fields)
                        || fields.ValueKind != JsonValueKind.Array)
                    {
                        return "Edge 插件数据能力声明不完整或 typeKey 重复。";
                    }

                    var fieldNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var field in fields.EnumerateArray())
                    {
                        if (!TryGetString(field, "name", out var fieldName)
                            || !fieldNames.Add(fieldName)
                            || !TryGetString(field, "dataType", out var dataType)
                            || dataType is not (
                                "string" or "datetime" or "integer"
                                or "decimal" or "boolean")
                            || !field.TryGetProperty("nullable", out var nullable)
                            || nullable.ValueKind is not (
                                JsonValueKind.True or JsonValueKind.False))
                        {
                            return "Edge 插件数据能力 fields 声明无效。";
                        }
                    }

                    if (publicFields.Any(field => !fieldNames.Contains(field))
                        || legacyTypeKeys.Any(alias =>
                            typeKeys.Contains(alias)
                            || string.Equals(
                                alias,
                                typeKeyProperty.GetString()!.Trim(),
                                StringComparison.OrdinalIgnoreCase)))
                    {
                        return "Edge 插件数据能力的公开字段或历史 TypeKey 别名无效。";
                    }
                }

                manifest.DataCapabilitiesJson = capabilities.GetRawText();
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                return "Edge 插件 data-capabilities.json 无法解析。";
            }
        }

        return null;
    }

    private static string? ValidateDependencyClosure(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        PluginPackageReleaseManifest releaseManifest,
        string entryAssembly)
    {
        if (!entries.TryGetValue(
                "dependency-closure.json",
                out var closureEntry))
        {
            return "Edge 插件 zip 缺少 dependency-closure.json。";
        }

        var closureBytes = ReadEntryBytes(closureEntry);
        var closureSha256 = Convert.ToHexString(
                SHA256.HashData(closureBytes))
            .ToLowerInvariant();
        if (!string.Equals(
                closureSha256,
                releaseManifest.DependencyClosureSha256,
                StringComparison.Ordinal))
        {
            return "Edge 插件 dependency-closure.json 摘要与发布 manifest 不一致。";
        }

        try
        {
            using var document = JsonDocument.Parse(closureBytes);
            var root = document.RootElement;
            if (!TryGetPositiveInt(root, "schemaVersion")
                || root.GetProperty("schemaVersion").GetInt32() != 2
                || !TryGetNonEmptyString(root, "entryAssembly")
                || !string.Equals(
                    root.GetProperty("entryAssembly").GetString(),
                    entryAssembly,
                    StringComparison.Ordinal)
                || !root.TryGetProperty("plugin", out var plugin)
                || plugin.ValueKind != JsonValueKind.Object
                || !TryGetString(plugin, "moduleId", out var moduleId)
                || !TryGetString(plugin, "version", out var pluginVersion)
                || !TryGetString(plugin, "targetRuntime", out var targetRuntime)
                || !string.Equals(
                    moduleId,
                    releaseManifest.ModuleId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    pluginVersion,
                    releaseManifest.Version,
                    StringComparison.Ordinal)
                || !string.Equals(
                    targetRuntime,
                    releaseManifest.TargetRuntime,
                    StringComparison.Ordinal)
                || !root.TryGetProperty("host", out var host)
                || host.ValueKind != JsonValueKind.Object
                || !TryGetString(host, "component", out var hostComponent)
                || !TryGetString(host, "version", out var hostVersion)
                || !TryGetString(
                    host,
                    "fileManifestSha256",
                    out var hostFileManifestSha256)
                || !string.Equals(
                    hostComponent,
                    releaseManifest.DependencyHostComponent,
                    StringComparison.Ordinal)
                || !string.Equals(
                    hostVersion,
                    releaseManifest.DependencyHostVersion,
                    StringComparison.Ordinal)
                || !string.Equals(
                    hostFileManifestSha256,
                    releaseManifest.DependencyHostFileManifestSha256,
                    StringComparison.OrdinalIgnoreCase)
                || !root.TryGetProperty("dependencies", out var dependencies)
                || dependencies.ValueKind != JsonValueKind.Array
                || dependencies.GetArrayLength()
                   != releaseManifest.DependencyCount)
            {
                return "Edge 插件 dependency-closure.json 身份、Host 证据或数量与发布 manifest 不一致。";
            }

            var paths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var entryMatches = 0;
            foreach (var dependency in dependencies.EnumerateArray())
            {
                if (!TryGetString(
                        dependency,
                        "library",
                        out _)
                    || !TryGetString(
                        dependency,
                        "asset",
                        out _)
                    || !TryGetString(
                        dependency,
                        "kind",
                        out var kind)
                    || kind is not ("runtime" or "native" or "resources")
                    || !TryGetString(
                        dependency,
                        "publishPath",
                        out var publishPath)
                    || !IsSafeZipEntryPath(publishPath)
                    || !paths.Add(publishPath)
                    || !TryGetString(
                        dependency,
                        "source",
                        out var source)
                    || source is not ("host" or "plugin")
                    || !TryGetString(
                        dependency,
                        "owner",
                        out var owner)
                    || !dependency.TryGetProperty(
                        "size",
                        out var sizeElement)
                    || !sizeElement.TryGetInt64(out var size)
                    || size < 0
                    || !TryGetString(
                        dependency,
                        "sha256",
                        out var sha256)
                    || !ClientReleaseFileFacts.IsSha256(sha256)
                    || !TryGetString(
                        dependency,
                        "version",
                        out var dependencyVersion))
                {
                    return "Edge 插件 dependency-closure.json 包含无效或重复依赖。";
                }

                if (source == "plugin")
                {
                    if (!string.Equals(
                            owner,
                            releaseManifest.ModuleId,
                            StringComparison.Ordinal)
                        || !string.Equals(
                            dependencyVersion,
                            releaseManifest.Version,
                            StringComparison.Ordinal)
                        || !entries.TryGetValue(
                            publishPath,
                            out var packagedEntry)
                        || packagedEntry.Length != size
                        || !string.Equals(
                            Convert.ToHexString(
                                SHA256.HashData(
                                    ReadEntryBytes(packagedEntry))),
                            sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return "Edge 插件自有依赖与 dependency-closure.json 不一致。";
                    }
                }
                else if (!string.Equals(
                             owner,
                             releaseManifest.DependencyHostComponent,
                             StringComparison.Ordinal)
                         || !string.Equals(
                             dependencyVersion,
                             releaseManifest.DependencyHostVersion,
                             StringComparison.Ordinal))
                {
                    return "Edge 插件 Host 公共依赖与精确 Host 证据不一致。";
                }

                if (string.Equals(
                        publishPath,
                        entryAssembly,
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (source != "plugin")
                        return "Edge 插件入口程序集不能由 Host 代管。";
                    entryMatches++;
                }
            }

            if (entryMatches != 1)
                return "Edge 插件入口程序集未在依赖闭包中唯一声明。";
        }
        catch (Exception exception) when (
            exception is JsonException
                or InvalidOperationException
                or KeyNotFoundException)
        {
            return "Edge 插件 dependency-closure.json 无法解析。";
        }

        return null;
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string? ValidatePluginFileManifest(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        PluginPackageReleaseManifest releaseManifest)
    {
        if (!entries.TryGetValue("file-manifest.json", out var manifestEntry))
            return "Edge 插件 zip 缺少 file-manifest.json。";

        byte[] manifestBytes;
        using (var stream = manifestEntry.Open())
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            manifestBytes = memory.ToArray();
        }
        var manifestSha256 = Convert.ToHexString(
            SHA256.HashData(manifestBytes));
        if (!string.Equals(
                manifestSha256,
                releaseManifest.FileManifestSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Edge 插件 file-manifest.json 摘要与发布 manifest 不一致。";
        }

        PluginFileManifest? fileManifest;
        try
        {
            fileManifest = JsonSerializer.Deserialize<PluginFileManifest>(
                manifestBytes,
                JsonOptions);
        }
        catch (JsonException)
        {
            return "Edge 插件 file-manifest.json 无法解析。";
        }
        if (fileManifest is null
            || fileManifest.SchemaVersion != 1
            || !string.Equals(
                fileManifest.Component,
                releaseManifest.ModuleId,
                StringComparison.Ordinal)
            || !string.Equals(
                fileManifest.Version,
                releaseManifest.Version,
                StringComparison.Ordinal)
            || fileManifest.Files is null
            || fileManifest.Files.Count == 0
            || fileManifest.Files.Count != releaseManifest.FileManifestFileCount)
        {
            return "Edge 插件 file-manifest.json 与发布 manifest 不一致。";
        }

        var declared = new Dictionary<string, PluginFileManifestEntry>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var item in fileManifest.Files)
        {
            string path;
            try
            {
                path = ClientReleaseZipArchive.NormalizeEntryPath(
                    item.Path,
                    "Edge 插件 file manifest");
            }
            catch (ClientReleaseValidationException)
            {
                return "Edge 插件 file-manifest.json 包含非法路径。";
            }
            if (string.IsNullOrWhiteSpace(path)
                || string.Equals(
                    path,
                    "file-manifest.json",
                    StringComparison.OrdinalIgnoreCase)
                || !declared.TryAdd(path, item)
                || item.Size < 0
                || !ClientReleaseFileFacts.IsSha256(item.Sha256)
                || string.IsNullOrWhiteSpace(item.Type)
                || !string.Equals(
                    item.Component,
                    releaseManifest.ModuleId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    item.Version,
                    releaseManifest.Version,
                    StringComparison.Ordinal))
            {
                return "Edge 插件 file-manifest.json 文件项无效或重复。";
            }
        }

        var actualPaths = entries
            .Where(pair =>
                !string.Equals(
                    pair.Key,
                    "file-manifest.json",
                    StringComparison.OrdinalIgnoreCase)
                && !pair.Value.FullName.EndsWith("/", StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actualPaths.SetEquals(declared.Keys))
            return "Edge 插件 zip 与 file-manifest.json 文件集不一致（存在缺失或额外文件）。";

        foreach (var pair in declared)
        {
            var entry = entries[pair.Key];
            using var source = entry.Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long size = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                size += read;
            }
            var sha256 = Convert.ToHexString(hash.GetHashAndReset());
            if (size != pair.Value.Size
                || !string.Equals(
                    sha256,
                    pair.Value.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return $"Edge 插件文件与 file-manifest.json 不一致: {pair.Key}";
            }
        }

        return null;
    }

    private static bool TryGetNonEmptyString(
        JsonElement element,
        string propertyName)
        => TryGetString(element, propertyName, out _);

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetPositiveInt(
        JsonElement element,
        string propertyName)
        => element.TryGetProperty(propertyName, out var property)
           && property.ValueKind == JsonValueKind.Number
           && property.TryGetInt32(out var value)
           && value > 0;

    private static bool TryGetStringArray(
        JsonElement element,
        string propertyName,
        out IReadOnlyList<string> values)
    {
        values = [];
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var result = new List<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in property.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String
                ? item.GetString()?.Trim()
                : null;
            if (string.IsNullOrWhiteSpace(value) || !unique.Add(value))
            {
                return false;
            }

            result.Add(value);
        }

        values = result;
        return true;
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

    private static bool IsSafeZipEntryPath(string path)
    {
        try
        {
            var normalized = ClientReleaseZipArchive.NormalizeEntryPath(
                path,
                "Edge 插件 dependency closure");
            return !string.IsNullOrWhiteSpace(normalized)
                   && string.Equals(
                       normalized,
                       path.Replace('\\', '/'),
                       StringComparison.Ordinal)
                   && !normalized.EndsWith("/", StringComparison.Ordinal);
        }
        catch (InvalidDataException)
        {
            return false;
        }
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

    internal sealed class PluginPackageReleaseManifest
    {
        public int PackageSchemaVersion { get; set; }

        public string Channel { get; set; } = string.Empty;

        public string ModuleId { get; set; } = string.Empty;

        public string ProcessType { get; set; } = string.Empty;

        public string? BusinessDocumentRef { get; set; }

        public string BusinessDocumentSha256 { get; set; } = string.Empty;

        public string? FileManifestSha256 { get; set; }

        public int FileManifestFileCount { get; set; }

        public string DataCapabilitiesJson { get; set; } = "[]";

        public string DataCapabilitiesFileName { get; set; } = string.Empty;

        public string DataCapabilitiesSha256 { get; set; } = string.Empty;

        public string DependencyClosureSha256 { get; set; } = string.Empty;

        public int DependencyCount { get; set; }

        public string DependencyHostComponent { get; set; } = string.Empty;

        public string DependencyHostVersion { get; set; } = string.Empty;

        public string DependencyHostFileManifestSha256 { get; set; } = string.Empty;

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

        public PluginReleaseSignatureEnvelope? ReleaseSignature { get; set; }

        public string? Publisher { get; set; }

        public string SourceCommit { get; set; } = string.Empty;

        public DateTime? CreatedAtUtc { get; set; }
    }

    internal sealed class PluginReleaseSignatureEnvelope
    {
        public string Algorithm { get; set; } = string.Empty;
        public string KeyId { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    private sealed class PluginReleaseTrustedKeyRing
    {
        public int SchemaVersion { get; set; }
        public List<PluginReleaseTrustedKey>? Keys { get; set; }
    }

    private sealed class PluginReleaseTrustedKey
    {
        public string KeyId { get; set; } = string.Empty;
        public string Algorithm { get; set; } = string.Empty;
        public string PublicKeyPem { get; set; } = string.Empty;
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

    private sealed class PluginFileManifest
    {
        public int SchemaVersion { get; set; }

        public string Component { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public List<PluginFileManifestEntry>? Files { get; set; }
    }

    private sealed class PluginFileManifestEntry
    {
        public string Path { get; set; } = string.Empty;

        public long Size { get; set; }

        public string Sha256 { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string Component { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;
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
