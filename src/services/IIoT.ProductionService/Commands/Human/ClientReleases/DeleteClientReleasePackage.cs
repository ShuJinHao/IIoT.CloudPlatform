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
using IIoT.Services.CrossCutting.Persistence;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AdminOnly]
[AuthorizeRequirement(ClientReleasePermissions.HardDelete)]
[DistributedLock(
    ClientReleasePublishLock.Resource,
    TimeoutSeconds = ClientReleasePublishLock.AcquireTimeoutSeconds)]
public sealed record DeleteClientReleasePackageCommand(Guid ReleaseId, string? Reason = null)
    : IHumanCommand<Result<ClientReleaseFileDeletionResultDto>>;

public sealed record ClientReleaseFileDeletionResultDto(
    Guid ReleaseId,
    string ComponentKind,
    string ComponentName,
    string Channel,
    string Version,
    bool FilesDeleted,
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<string> SkippedPaths,
    string? Warning);

public sealed class DeleteClientReleasePackageHandler(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    IRepository<ClientReleaseComponent> componentRepository,
    IDeviceClientStateStore clientStateStore,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService,
    ILogger<DeleteClientReleasePackageHandler> logger,
    IUnitOfWork unitOfWork,
    IClientReleaseWriteObservationReader observationReader)
    : ICommandHandler<DeleteClientReleasePackageCommand, Result<ClientReleaseFileDeletionResultDto>>
{
    private const string AuditAction = "ClientRelease.DeletePackage";
    private const int AuditSummaryMaxLength = 512;
    private const string NoFilesDeletionReason =
        "发布文件未找到或不在受控发布目录下，已移出可分发 catalog，历史更新内容保留。";
    private static readonly TimeSpan PostCommitFinalizationTimeout =
        TimeSpan.FromSeconds(30);

    public async Task<Result<ClientReleaseFileDeletionResultDto>> Handle(
        DeleteClientReleasePackageCommand request,
        CancellationToken cancellationToken)
    {
        var component = await componentRepository.GetSingleOrDefaultAsync(
            new ClientReleaseComponentByVersionIdSpec(request.ReleaseId),
            cancellationToken);
        if (component is null)
        {
            return Result.NotFound("发布版本不存在。");
        }

        var version = component.FindVersion(request.ReleaseId);
        if (version is null)
        {
            return Result.NotFound("发布版本不存在。");
        }

        var baselineObservation =
            await CloudWriteCommitRecovery
                .TryObserveOptionalAttemptAsync(
            token => observationReader.ObserveVersionAsync(
                request.ReleaseId,
                token),
            cancellationToken);
        var baseline = baselineObservation?.Value
                       ?? throw new CloudWriteCommitUnknownException();

        if (ClientReleaseWriteStateFingerprint.ForVersion(
                component,
                version) != baseline)
        {
            throw new CloudWriteConflictException();
        }

        var isHost =
            component.ComponentKind == ClientReleaseComponentKind.Host;
        var edgeRoot = artifactOptions.Value.ResolveEdgeUpdatesRoot();
        var plan = ClientReleaseFileDeletionPlan.ForRelease(
            edgeRoot,
            component,
            version);
        var componentKind = isHost ? "Host" : "Plugin";
        var componentName =
            isHost ? "Edge Host" : component.ComponentKey;
        var deleteRequestedAtUtc =
            ClientReleaseWriteCommitRecovery.NormalizeUtc(DateTime.UtcNow);
        var deletedAtUtc =
            ClientReleaseWriteCommitRecovery.NormalizeUtc(DateTime.UtcNow);
        var requestedPhysicalDeletionReason =
            string.IsNullOrWhiteSpace(request.Reason)
                ? "管理员删除发布包。"
                : request.Reason.Trim();

        if (baseline.Status == ClientReleaseStatus.Deleted)
        {
            var committedReceipt =
                ClientReleasePackageDeletionReceipt.Parse(
                baseline.DeletionReceiptJson)
                ?? throw new CloudWriteCommitUnknownException();
            var expectedReason = committedReceipt.PhysicalDeletion
                ? requestedPhysicalDeletionReason
                : NoFilesDeletionReason;
            if (baseline.DeletedAtUtc is null
                || baseline.DeletionFailure is not null
                || !string.Equals(
                    baseline.DeletionReason,
                    expectedReason,
                    StringComparison.Ordinal)
                || !string.Equals(
                    committedReceipt.DeletionReason,
                    expectedReason,
                    StringComparison.Ordinal))
            {
                throw new CloudWriteConflictException();
            }

            await WriteAuditAsync(
                version.Id,
                componentKind,
                componentName,
                component.Channel,
                version.Version,
                succeeded: true,
                committedReceipt.DeletedPaths,
                committedReceipt.SkippedPaths,
                committedReceipt.Warning,
                baseline.DeletedAtUtc.Value,
                cancellationToken);
            return BuildSuccess(
                committedReceipt.PhysicalDeletion,
                committedReceipt.DeletedPaths,
                committedReceipt.SkippedPaths,
                committedReceipt.Warning);
        }

        var snapshots =
            await clientStateStore.GetVersionSnapshotsByDevicesAsync(
                cancellationToken: cancellationToken);
        var inUse = isHost
            ? snapshots.Any(snapshot =>
                string.Equals(
                    snapshot.HostVersion,
                    version.Version,
                    StringComparison.OrdinalIgnoreCase))
            : snapshots.Any(snapshot =>
                snapshot.InstalledPlugins.Any(plugin =>
                    string.Equals(
                        plugin.ModuleId,
                        component.ComponentKey,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        plugin.Version,
                        version.Version,
                        StringComparison.OrdinalIgnoreCase)));
        if (inUse)
        {
            var inUseReason = isHost
                ? "已有设备当前宿主版本等于目标版本，禁止物理删除发布文件。"
                : "已有设备当前插件版本等于目标版本，禁止物理删除发布文件。";
            await WriteAuditAsync(
                version.Id,
                componentKind,
                componentName,
                component.Channel,
                version.Version,
                succeeded: false,
                [],
                [],
                inUseReason,
                DateTime.UtcNow,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return Result.Invalid(inUseReason);
        }

        var currentDeletedPaths = plan.Targets
            .SelectMany(target => target.RelativeFiles)
            .ToArray();
        var currentWarning = plan.SkippedPaths.Count == 0
            ? null
            : $"部分文件仍被 manifest 引用或不在受控范围，已跳过 {plan.SkippedPaths.Count} 项。";
        var receipt =
            ClientReleasePackageDeletionReceipt.Parse(
                baseline.DeletionReceiptJson);
        if (baseline.Status is
                ClientReleaseStatus.DeleteRequested or
                ClientReleaseStatus.DeleteFailed)
        {
            if (receipt is null
                || !receipt.PhysicalDeletion
                || !string.Equals(
                    receipt.DeletionReason,
                    requestedPhysicalDeletionReason,
                    StringComparison.Ordinal)
                || currentDeletedPaths.Any(path =>
                    !receipt.DeletedPaths.Contains(
                        path.Replace('\\', '/'),
                        StringComparer.Ordinal))
                || !receipt.SkippedPaths.SequenceEqual(
                    plan.SkippedPaths
                        .Select(path => path.Replace('\\', '/'))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(path => path, StringComparer.Ordinal),
                    StringComparer.Ordinal))
            {
                throw new CloudWriteConflictException();
            }
        }
        else
        {
            var physicalDeletion = plan.Targets.Count > 0;
            var deletionReason = physicalDeletion
                ? requestedPhysicalDeletionReason
                : NoFilesDeletionReason;
            receipt = ClientReleasePackageDeletionReceipt.Create(
                deletionReason,
                physicalDeletion,
                currentDeletedPaths,
                plan.SkippedPaths,
                physicalDeletion ? currentWarning : NoFilesDeletionReason);
        }

        var stableReceipt = receipt
            ?? throw new CloudWriteCommitUnknownException();
        var stableDeletionReason = stableReceipt.DeletionReason;
        if (!stableReceipt.PhysicalDeletion)
        {
            await PersistVersionTargetAsync(
                baseline,
                ClientReleaseStatus.Deleted,
                deletedAtUtc,
                stableDeletionReason,
                null,
                stableReceipt.Json,
                (currentComponent, currentVersion) =>
                    currentComponent.MarkVersionDeleted(
                        currentVersion.Id,
                        stableDeletionReason,
                        deletedAtUtc,
                        stableReceipt.Json),
                cancellationToken);
            return await FinalizePostCommitAsync(
                async finalizationToken =>
                {
                    await WriteAuditAsync(
                        version.Id,
                        componentKind,
                        componentName,
                        component.Channel,
                        version.Version,
                        succeeded: true,
                        stableReceipt.DeletedPaths,
                        stableReceipt.SkippedPaths,
                        stableReceipt.Warning,
                        deletedAtUtc,
                        finalizationToken);
                    return BuildSuccess(
                        filesDeleted: false,
                        stableReceipt.DeletedPaths,
                        stableReceipt.SkippedPaths,
                        stableReceipt.Warning);
                },
                cancellationToken);
        }

        var deleteRequested = await PersistVersionTargetAsync(
            baseline,
            ClientReleaseStatus.DeleteRequested,
            null,
            null,
            null,
            stableReceipt.Json,
            (currentComponent, currentVersion) =>
                currentComponent.MarkVersionDeleteRequested(
                    currentVersion.Id,
                    deleteRequestedAtUtc,
                    stableReceipt.Json),
            cancellationToken);

        return await FinalizePostCommitAsync(
            async finalizationToken =>
            {
                var deletedPaths = new List<string>();
                try
                {
                    foreach (var target in plan.Targets)
                    {
                        finalizationToken.ThrowIfCancellationRequested();
                        target.AssertSafe();
                        deletedPaths.AddRange(target.RelativeFiles);
                        target.Delete();
                    }

                    await PersistVersionTargetAsync(
                        deleteRequested,
                        ClientReleaseStatus.Deleted,
                        deletedAtUtc,
                        stableDeletionReason,
                        null,
                        stableReceipt.Json,
                        (currentComponent, currentVersion) =>
                            currentComponent.MarkVersionDeleted(
                                currentVersion.Id,
                                stableDeletionReason,
                                deletedAtUtc,
                                stableReceipt.Json),
                        finalizationToken);

                    await WriteAuditAsync(
                        version.Id,
                        componentKind,
                        componentName,
                        component.Channel,
                        version.Version,
                        succeeded: true,
                        stableReceipt.DeletedPaths,
                        stableReceipt.SkippedPaths,
                        stableReceipt.Warning,
                        deletedAtUtc,
                        cancellationToken);

                    return BuildSuccess(
                        stableReceipt.PhysicalDeletion,
                        stableReceipt.DeletedPaths,
                        stableReceipt.SkippedPaths,
                        stableReceipt.Warning);
                }
                catch (Exception ex) when (ex is
                    IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException)
                {
                    var failureAtUtc =
                        ClientReleaseWriteCommitRecovery.NormalizeUtc(
                            DateTime.UtcNow);
                    await PersistVersionTargetAsync(
                        deleteRequested,
                        ClientReleaseStatus.DeleteFailed,
                        null,
                        null,
                        ex.Message,
                        stableReceipt.Json,
                        (currentComponent, currentVersion) =>
                            currentComponent.MarkVersionDeleteFailed(
                                currentVersion.Id,
                                ex.Message,
                                failureAtUtc,
                                stableReceipt.Json),
                        finalizationToken);
                    logger.LogError(
                        new EventId(
                            4603,
                            "ClientReleaseDeletePackageFailure"),
                        "Delete release package files failed. ComponentKind={ComponentKind} Channel={Channel} ErrorType={ErrorType}.",
                        componentKind,
                        component.Channel,
                        ex.GetType().Name);
                    await WriteAuditAsync(
                        version.Id,
                        componentKind,
                        componentName,
                        component.Channel,
                        version.Version,
                        succeeded: false,
                        deletedPaths,
                        plan.SkippedPaths,
                        ex.Message,
                        failureAtUtc,
                        cancellationToken);
                    return Result.Invalid(
                        $"删除发布包失败: {ex.Message}");
                }
            },
            cancellationToken);

        Result<ClientReleaseFileDeletionResultDto> BuildSuccess(
            bool filesDeleted,
            IReadOnlyList<string> deletedPaths,
            IReadOnlyList<string> skippedPaths,
            string? warning)
            => Result.Success(new ClientReleaseFileDeletionResultDto(
                version.Id,
                componentKind,
                componentName,
                component.Channel,
                version.Version,
                filesDeleted,
                deletedPaths,
                skippedPaths,
                warning));
    }

    private static async Task<T> FinalizePostCommitAsync<T>(
        Func<CancellationToken, Task<T>> finalization,
        CancellationToken callerCancellationToken)
    {
        using var timeout =
            new CancellationTokenSource(PostCommitFinalizationTimeout);
        try
        {
            var result = await finalization(timeout.Token);
            callerCancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested)
        {
            callerCancellationToken.ThrowIfCancellationRequested();
            throw new CloudWriteCommitUnknownException();
        }
        catch
        {
            callerCancellationToken.ThrowIfCancellationRequested();
            throw;
        }
    }

    private async Task<ClientReleaseVersionWriteState>
        PersistVersionTargetAsync(
            ClientReleaseVersionWriteState baseline,
            ClientReleaseStatus targetStatus,
            DateTime? deletedAtUtc,
            string? deletionReason,
            string? deletionFailure,
            string? deletionReceiptJson,
            Action<ClientReleaseComponent, ClientReleaseVersion> mutate,
            CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.ExecuteResilientAsync(
                ExecuteAttemptAsync,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            var current = await ObserveCommitAsync();
            if (current is not null
                && ClientReleaseWriteCommitRecovery.MatchesVersionTarget(
                    current,
                    baseline,
                    targetStatus,
                    deletedAtUtc,
                    deletionReason,
                    deletionFailure,
                    deletionReceiptJson))
            {
                return current;
            }

            throw new OperationCanceledException(cancellationToken);
        }
        catch (CloudWriteException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch
        {
            return await ResolveCommitAsync();
        }

        var confirmedObservation =
            await CloudWriteCommitRecovery
                .TryObserveOptionalCommitAsync(
                token => observationReader.ObserveVersionAsync(
                    baseline.VersionId,
                    token));
        var confirmed = confirmedObservation?.Value;
        if (confirmed is not null
            && ClientReleaseWriteCommitRecovery.MatchesVersionTarget(
                confirmed,
                baseline,
                targetStatus,
                deletedAtUtc,
                deletionReason,
                deletionFailure,
                deletionReceiptJson))
        {
            return confirmed;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (confirmed == baseline)
        {
            throw new CloudWriteCommitUnknownException();
        }

        throw confirmed is null
            ? new CloudWriteCommitUnknownException()
            : new CloudWriteConflictException();

        async Task<bool> ExecuteAttemptAsync(
            CancellationToken callbackCancellationToken)
        {
            var currentObservation =
                await CloudWriteCommitRecovery
                    .TryObserveOptionalAttemptAsync(
                    token => observationReader.ObserveVersionAsync(
                        baseline.VersionId,
                        token),
                    callbackCancellationToken)
                ?? throw new CloudWriteCommitUnknownException();
            var current = currentObservation.Value
                          ?? throw new CloudWriteCommitUnknownException();
            if (ClientReleaseWriteCommitRecovery.MatchesVersionTarget(
                    current,
                    baseline,
                    targetStatus,
                    deletedAtUtc,
                    deletionReason,
                    deletionFailure,
                    deletionReceiptJson))
            {
                return true;
            }

            if (current != baseline)
            {
                throw new CloudWriteConflictException();
            }

            var currentComponent =
                await componentRepository.GetSingleOrDefaultAsync(
                    new ClientReleaseComponentByVersionIdSpec(
                        baseline.VersionId),
                    callbackCancellationToken)
                ?? throw new CloudWriteConflictException();
            var currentVersion =
                currentComponent.FindVersion(baseline.VersionId)
                ?? throw new CloudWriteConflictException();
            if (ClientReleaseWriteStateFingerprint.ForVersion(
                    currentComponent,
                    currentVersion) != baseline)
            {
                throw new CloudWriteConflictException();
            }

            mutate(currentComponent, currentVersion);
            await componentRepository.SaveChangesAsync(
                callbackCancellationToken);
            return true;
        }

        async Task<ClientReleaseVersionWriteState> ResolveCommitAsync()
        {
            var current = await ObserveCommitAsync();
            if (current is not null
                && ClientReleaseWriteCommitRecovery.MatchesVersionTarget(
                    current,
                    baseline,
                    targetStatus,
                    deletedAtUtc,
                    deletionReason,
                    deletionFailure,
                    deletionReceiptJson))
            {
                return current;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (current is null || current == baseline)
            {
                throw new CloudWriteCommitUnknownException();
            }

            throw new CloudWriteConflictException();
        }

        async Task<ClientReleaseVersionWriteState?> ObserveCommitAsync()
        {
            var observation =
                await CloudWriteCommitRecovery
                    .TryObserveOptionalCommitAsync(
                        token => observationReader.ObserveVersionAsync(
                            baseline.VersionId,
                            token));
            return observation?.Value;
        }
    }

    private async Task WriteAuditAsync(
        Guid releaseId,
        string componentKind,
        string componentName,
        string channel,
        string version,
        bool succeeded,
        IReadOnlyList<string> deletedPaths,
        IReadOnlyList<string> skippedPaths,
        string? failureOrWarning,
        DateTime executedAtUtc,
        CancellationToken cancellationToken)
    {
        var summary = BuildAuditSummary(
            componentKind,
            componentName,
            channel,
            version,
            deletedPaths,
            skippedPaths);

        var entry = new AuditTrailEntry(
                ClientReleaseAuditActor.ParseId(currentUser.Id),
                currentUser.UserName,
                AuditAction,
                "ClientRelease",
                releaseId.ToString(),
                ClientReleaseWriteCommitRecovery.NormalizeUtc(
                    executedAtUtc),
                succeeded,
                summary,
                succeeded ? null : failureOrWarning,
                succeeded
                    ? $"client-release-package-delete:{releaseId:N}"
                    : null);
        if (succeeded)
        {
            try
            {
                await CloudWriteCommitRecovery.ConfirmRecoveredAuditAsync(
                    auditTrailService,
                    entry);
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            return;
        }

        await CloudWriteCommitRecovery.TryWriteRecoveredAuditAsync(
            auditTrailService,
            entry);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static string BuildAuditSummary(
        string componentKind,
        string componentName,
        string channel,
        string version,
        IReadOnlyCollection<string> deletedPaths,
        IReadOnlyCollection<string> skippedPaths)
    {
        var inventorySha256 = ComputeInventorySha256(
            deletedPaths,
            skippedPaths);
        var summary = JsonSerializer.Serialize(new
        {
            action = AuditAction,
            componentKind,
            componentName,
            channel,
            version,
            deletedCount = deletedPaths.Count,
            skippedCount = skippedPaths.Count,
            inventorySha256
        });
        if (summary.Length <= AuditSummaryMaxLength)
        {
            return summary;
        }

        summary = JsonSerializer.Serialize(new
        {
            action = AuditAction,
            componentKind,
            componentIdentitySha256 = ComputeComponentIdentitySha256(
                componentName,
                channel,
                version),
            deletedCount = deletedPaths.Count,
            skippedCount = skippedPaths.Count,
            inventorySha256
        });
        if (summary.Length > AuditSummaryMaxLength)
        {
            throw new InvalidOperationException(
                "Client release package deletion audit summary exceeds the persistence limit.");
        }

        return summary;
    }

    private static string ComputeComponentIdentitySha256(
        string componentName,
        string channel,
        string version)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            componentName,
            channel,
            version
        });
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string ComputeInventorySha256(
        IEnumerable<string> deletedPaths,
        IEnumerable<string> skippedPaths)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            deletedPaths = deletedPaths
                .Select(path => path.Replace('\\', '/'))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray(),
            skippedPaths = skippedPaths
                .Select(path => path.Replace('\\', '/'))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray()
        });
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

internal sealed class ClientReleasePackageDeletionReceipt
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private ClientReleasePackageDeletionReceipt(
        string deletionReason,
        bool physicalDeletion,
        IReadOnlyList<string> deletedPaths,
        IReadOnlyList<string> skippedPaths,
        string? warning)
    {
        DeletionReason = string.IsNullOrWhiteSpace(deletionReason)
            ? throw new ArgumentException(
                "Deletion receipt reason cannot be empty.",
                nameof(deletionReason))
            : deletionReason.Trim();
        PhysicalDeletion = physicalDeletion;
        DeletedPaths = NormalizePaths(deletedPaths);
        SkippedPaths = NormalizePaths(skippedPaths);
        Warning = string.IsNullOrWhiteSpace(warning)
            ? null
            : warning.Trim();
        Json = JsonSerializer.Serialize(
            new ReceiptPayload(
                DeletionReason,
                PhysicalDeletion,
                DeletedPaths,
                SkippedPaths,
                Warning),
            JsonOptions);
    }

    public string DeletionReason { get; }

    public bool PhysicalDeletion { get; }

    public IReadOnlyList<string> DeletedPaths { get; }

    public IReadOnlyList<string> SkippedPaths { get; }

    public string? Warning { get; }

    public string Json { get; }

    public static ClientReleasePackageDeletionReceipt Create(
        string deletionReason,
        bool physicalDeletion,
        IReadOnlyList<string> deletedPaths,
        IReadOnlyList<string> skippedPaths,
        string? warning)
        => new(
            deletionReason,
            physicalDeletion,
            deletedPaths,
            skippedPaths,
            warning);

    public static ClientReleasePackageDeletionReceipt? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<ReceiptPayload>(
                json,
                JsonOptions);
            return string.IsNullOrWhiteSpace(payload?.DeletionReason)
                   || payload.DeletedPaths is null
                   || payload.SkippedPaths is null
                ? null
                : new ClientReleasePackageDeletionReceipt(
                    payload.DeletionReason,
                    payload.PhysicalDeletion,
                    payload.DeletedPaths,
                    payload.SkippedPaths,
                    payload.Warning);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> NormalizePaths(
        IEnumerable<string> paths)
        => paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private sealed record ReceiptPayload(
        string DeletionReason,
        bool PhysicalDeletion,
        IReadOnlyList<string> DeletedPaths,
        IReadOnlyList<string> SkippedPaths,
        string? Warning);
}

internal sealed class ClientReleaseFileDeletionPlan
{
    private ClientReleaseFileDeletionPlan(
        IReadOnlyList<ClientReleaseFileDeletionTarget> targets,
        IReadOnlyList<string> skippedPaths)
    {
        Targets = targets;
        SkippedPaths = skippedPaths;
    }

    public IReadOnlyList<ClientReleaseFileDeletionTarget> Targets { get; }

    public IReadOnlyList<string> SkippedPaths { get; }

    public static ClientReleaseFileDeletionPlan ForRelease(
        string edgeRoot,
        ClientReleaseComponent component,
        ClientReleaseVersion release)
    {
        var targets = new List<ClientReleaseFileDeletionTarget>();
        var skipped = new List<string>();
        foreach (var artifact in release.Artifacts)
        {
            var fullPath = Path.Combine(edgeRoot, artifact.RelativePath);
            switch (artifact.ArtifactKind)
            {
                case ClientReleaseArtifactKind.InstallerDirectory:
                case ClientReleaseArtifactKind.PluginPackageDirectory:
                    TryAddDirectory(targets, skipped, edgeRoot, fullPath);
                    break;
                case ClientReleaseArtifactKind.ManifestFile:
                case ClientReleaseArtifactKind.PackageFile:
                    TryAddFile(targets, skipped, edgeRoot, fullPath);
                    break;
                case ClientReleaseArtifactKind.VelopackFile:
                    TryAddVelopackFile(targets, skipped, edgeRoot, component.Channel, fullPath);
                    break;
                default:
                    skipped.Add(artifact.RelativePath);
                    break;
            }
        }

        return new ClientReleaseFileDeletionPlan(targets, skipped);
    }

    private static void TryAddVelopackFile(
        ICollection<ClientReleaseFileDeletionTarget> targets,
        ICollection<string> skipped,
        string edgeRoot,
        string channel,
        string path)
    {
        var velopackRoot = Path.Combine(edgeRoot, "velopack", channel);
        var name = Path.GetFileName(path);
        if (!Directory.Exists(velopackRoot) || !File.Exists(path))
        {
            return;
        }

        if (ClientReleaseVelopackPaths.IsProtectedChannelManifest(name))
        {
            skipped.Add(ToRelative(edgeRoot, path));
            return;
        }

        var manifestPaths = Directory.EnumerateFiles(
                velopackRoot,
                "*",
                SearchOption.TopDirectoryOnly)
            .Where(candidate => ClientReleaseVelopackPaths.IsProtectedChannelManifest(
                Path.GetFileName(candidate)));
        if (ClientReleaseVelopackPaths.IsReferencedByManifests(manifestPaths, name))
        {
            skipped.Add(ToRelative(edgeRoot, path));
            return;
        }

        TryAddFile(targets, skipped, edgeRoot, path);
    }

    private static void TryAddDirectory(
        ICollection<ClientReleaseFileDeletionTarget> targets,
        ICollection<string> skipped,
        string edgeRoot,
        string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        targets.Add(ClientReleaseFileDeletionTarget.Directory(edgeRoot, path));
    }

    private static void TryAddFile(
        ICollection<ClientReleaseFileDeletionTarget> targets,
        ICollection<string> skipped,
        string edgeRoot,
        string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        targets.Add(ClientReleaseFileDeletionTarget.File(edgeRoot, path));
    }

    private static string ToRelative(string edgeRoot, string path)
        => Path.GetRelativePath(edgeRoot, path).Replace('\\', '/');
}

internal sealed class ClientReleaseFileDeletionTarget
{
    private ClientReleaseFileDeletionTarget(
        string edgeRoot,
        string path,
        bool isDirectory)
    {
        EdgeRoot = Path.GetFullPath(edgeRoot);
        PathToDelete = Path.GetFullPath(path);
        IsDirectory = isDirectory;
        RelativeFiles = ResolveRelativeFiles(EdgeRoot, PathToDelete, isDirectory);
    }

    public string EdgeRoot { get; }

    public string PathToDelete { get; }

    public bool IsDirectory { get; }

    public IReadOnlyList<string> RelativeFiles { get; }

    public static ClientReleaseFileDeletionTarget Directory(string edgeRoot, string path)
        => new(edgeRoot, path, isDirectory: true);

    public static ClientReleaseFileDeletionTarget File(string edgeRoot, string path)
        => new(edgeRoot, path, isDirectory: false);

    public void AssertSafe()
    {
        var root = EnsureTrailingSeparator(EdgeRoot);
        var target = Path.GetFullPath(PathToDelete);
        if (!target.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("发布文件路径越过受控发布目录。");
        }

        if (target.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("发布文件路径包含非法上级目录片段。");
        }

        AssertNoReparsePoint(target, IsDirectory);
    }

    public void Delete()
    {
        if (IsDirectory)
        {
            if (System.IO.Directory.Exists(PathToDelete))
            {
                System.IO.Directory.Delete(PathToDelete, recursive: true);
            }

            return;
        }

        if (System.IO.File.Exists(PathToDelete))
        {
            System.IO.File.Delete(PathToDelete);
        }
    }

    private static IReadOnlyList<string> ResolveRelativeFiles(
        string edgeRoot,
        string path,
        bool isDirectory)
    {
        if (isDirectory)
        {
            return System.IO.Directory.Exists(path)
                ? System.IO.Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Select(file => ToRelative(edgeRoot, file))
                    .OrderBy(file => file, StringComparer.Ordinal)
                    .ToList()
                : [];
        }

        return System.IO.File.Exists(path) ? [ToRelative(edgeRoot, path)] : [];
    }

    private static void AssertNoReparsePoint(string path, bool isDirectory)
    {
        if (IsReparsePoint(path))
        {
            throw new InvalidOperationException("发布文件路径包含符号链接或重解析点。");
        }

        if (!isDirectory || !System.IO.Directory.Exists(path))
        {
            return;
        }

        foreach (var entry in System.IO.Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
        {
            if (IsReparsePoint(entry))
            {
                throw new InvalidOperationException("发布文件目录中包含符号链接或重解析点。");
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (System.IO.File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static string ToRelative(string edgeRoot, string path)
        => System.IO.Path.GetRelativePath(edgeRoot, path).Replace('\\', '/');

    private static string EnsureTrailingSeparator(string path)
    {
        var separator = System.IO.Path.DirectorySeparatorChar;
        return path.EndsWith(separator)
            ? path
            : $"{path}{separator}";
    }
}
