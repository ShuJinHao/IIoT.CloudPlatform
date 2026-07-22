using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.Services.Contracts.Auditing;
using IIoT.SharedKernel.Repository;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.ClientReleases;

public interface IClientReleaseComponentDeletionProcessor
{
    Task<ClientReleaseComponentDeletionOutcome> ProcessAsync(
        ClientReleaseComponentDeletion deletion,
        CancellationToken cancellationToken);
}

/// <summary>
/// 执行一个持久化删除操作的文件清理，并按两阶段收敛：
/// 清理成功先把操作标为 CleanupCompleted 落库，写稳成功审计（含管理员身份与删除原因）后才删除操作记录。
/// 失败把操作标为 Failed 并写失败审计，等待管理员显式重试或启动恢复。
/// 白名单只取同 channel 全部 runtime 的存活 Host 版本，Archived/Deleted/DeleteRequested 版本不进入白名单。
/// </summary>
public sealed class ClientReleaseComponentDeletionProcessor(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    IRepository<ClientReleaseComponent> componentRepository,
    IClientReleaseComponentDeletionStore deletionStore,
    IAuditTrailService auditTrailService,
    ILogger<ClientReleaseComponentDeletionProcessor> logger)
    : IClientReleaseComponentDeletionProcessor
{
    private const string AuditAction = "ClientRelease.HardDeleteComponent";

    public async Task<ClientReleaseComponentDeletionOutcome> ProcessAsync(
        ClientReleaseComponentDeletion deletion,
        CancellationToken cancellationToken)
    {
        var (survivingNupkgFileNames, hasSurvivingHost) = await CollectSurvivingHostStateAsync(
            deletion,
            cancellationToken);
        var outcome = new ClientReleaseComponentDeletionExecutor(artifactOptions, logger)
            .Execute(deletion, survivingNupkgFileNames, hasSurvivingHost, cancellationToken);

        if (!outcome.Succeeded)
        {
            deletion.MarkFailed(outcome.FailureCode!);
            await deletionStore.SaveChangesAsync(cancellationToken);
            await WriteAuditAsync(deletion, outcome, succeeded: false, CancellationToken.None);
            return outcome;
        }

        // 第一阶段：清理收敛落库，操作进入 CleanupCompleted（审计待写）。
        deletion.MarkCleanupCompleted();
        await deletionStore.SaveChangesAsync(cancellationToken);

        // 第二阶段：成功审计写稳后才删除操作记录。审计未写稳则操作保持 CleanupCompleted，
        // 由启动恢复或管理员重试重放并补写审计，避免“实体、操作、审计三者都没有”。
        var auditPersisted = await WriteAuditAsync(deletion, outcome, succeeded: true, CancellationToken.None);
        if (!auditPersisted)
        {
            logger.LogWarning(
                new EventId(4614, "ClientReleaseComponentDeletionAuditPending"),
                "Client release component deletion cleanup completed but success audit was not persisted. DeletionId={DeletionId} kept for recovery.",
                deletion.Id);
            return outcome;
        }

        deletionStore.Remove(deletion);
        await deletionStore.SaveChangesAsync(CancellationToken.None);
        return outcome;
    }

    private async Task<bool> WriteAuditAsync(
        ClientReleaseComponentDeletion deletion,
        ClientReleaseComponentDeletionOutcome outcome,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var summary = JsonSerializer.Serialize(new
        {
            action = AuditAction,
            deletionId = deletion.Id,
            deletion.ComponentKind,
            deletion.ComponentKey,
            deletion.Channel,
            deletion.RetryCount,
            reason = deletion.Reason,
            requestedByUserName = deletion.RequestedByUserName,
            deletedPaths = outcome.DeletedPaths,
            skippedPaths = outcome.SkippedPaths,
            manifestChanged = outcome.ManifestChanged,
            failureCode = outcome.FailureCode
        });

        return await auditTrailService.TryWriteConfirmedAsync(
            new AuditTrailEntry(
                deletion.RequestedByUserId,
                deletion.RequestedByUserName,
                AuditAction,
                "ClientRelease",
                deletion.ComponentId.ToString(),
                DateTime.UtcNow,
                succeeded,
                summary,
                succeeded ? null : outcome.FailureCode),
            cancellationToken);
    }

    private async Task<(IReadOnlyCollection<string> NupkgFileNames, bool HasSurvivingHost)> CollectSurvivingHostStateAsync(
        ClientReleaseComponentDeletion deletion,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(deletion.ComponentKind, "Host", StringComparison.OrdinalIgnoreCase))
        {
            return (names, false);
        }

        // 白名单只覆盖同 channel 全部 runtime 的存活 Host 版本（不区分 targetRuntime），
        // Archived/Deleted/DeleteRequested/DeleteFailed 版本不进入白名单。
        var survivingComponents = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(
                deletion.Channel,
                targetRuntime: null,
                onlyPublished: false),
            cancellationToken);
        var hasSurvivingHost = false;
        foreach (var component in survivingComponents.Where(
                     component => component.ComponentKind == ClientReleaseComponentKind.Host))
        {
            foreach (var version in component.Versions.Where(IsSurvivingVersion))
            {
                hasSurvivingHost = true;
                foreach (var artifact in version.Artifacts)
                {
                    if (artifact.ArtifactKind == ClientReleaseArtifactKind.VelopackFile
                        && artifact.RelativePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    {
                        names.Add(Path.GetFileName(artifact.RelativePath));
                    }
                }
            }
        }

        return (names, hasSurvivingHost);
    }

    private static bool IsSurvivingVersion(ClientReleaseVersion version)
        => version.Status is ClientReleaseStatus.Published
            or ClientReleaseStatus.Deprecated
            or ClientReleaseStatus.Draft;
}
