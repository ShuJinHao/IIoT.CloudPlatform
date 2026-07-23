using System.Security.Cryptography;
using System.Text;
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
/// 受控文件安全失败（穿透/事实不匹配/reparse，抛 <see cref="ClientReleaseValidationException"/>）同样落
/// Failed + 稳定失败码 <see cref="ClientReleaseComponentDeletionExecutor.FailureFileFactsMismatch"/> + 失败审计。
/// 白名单只取同 channel 全部 runtime 的存活 Host 版本，Archived/Deleted/DeleteRequested 版本不进入白名单。
/// 成功审计以 deletionId 为幂等键、摘要为有界字段（计数 + 路径 digest，不超过审计列宽）；
/// 审计未写稳则操作保持 CleanupCompleted 且 outcome.AuditConfirmed=false，调用方不得报告删除已完成。
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
        ClientReleaseComponentDeletionOutcome outcome;
        try
        {
            var (survivingNupkgFileNames, hasSurvivingHost) = await CollectSurvivingHostStateAsync(
                deletion,
                cancellationToken);
            outcome = new ClientReleaseComponentDeletionExecutor(artifactOptions, logger)
                .Execute(deletion, survivingNupkgFileNames, hasSurvivingHost, cancellationToken);
        }
        catch (ClientReleaseValidationException ex)
        {
            // 受控文件安全失败（穿透/事实不匹配/reparse）：落 Failed + 稳定失败码 + 失败审计。
            logger.LogWarning(
                new EventId(4615, "ClientReleaseComponentDeletionSecurityFailure"),
                "Client release component deletion hit a controlled-file security failure. DeletionId={DeletionId} Reason={Reason}.",
                deletion.Id,
                ex.SafeMessage);
            outcome = new ClientReleaseComponentDeletionOutcome(
                false,
                [],
                [],
                ClientReleaseComponentDeletionExecutor.FailureFileFactsMismatch,
                false);
            deletion.MarkFailed(outcome.FailureCode!);
            await deletionStore.SaveChangesAsync(cancellationToken);
            await WriteAuditAsync(deletion, outcome, succeeded: false, CancellationToken.None);
            return outcome;
        }

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
        // 由启动恢复或管理员重试重放并补写审计；重放产生的是同一 deletionId、同一内容的审计，
        // 语义幂等。审计未确认时 outcome.AuditConfirmed=false，调用方不得报告删除已完成。
        var auditPersisted = await WriteAuditAsync(deletion, outcome, succeeded: true, CancellationToken.None);
        if (!auditPersisted)
        {
            logger.LogWarning(
                new EventId(4614, "ClientReleaseComponentDeletionAuditPending"),
                "Client release component deletion cleanup completed but success audit was not persisted. DeletionId={DeletionId} kept for recovery.",
                deletion.Id);
            return outcome with { AuditConfirmed = false };
        }

        deletionStore.Remove(deletion);
        await deletionStore.SaveChangesAsync(CancellationToken.None);
        return outcome with { AuditConfirmed = true };
    }

    /// <summary>
    /// 写审计。摘要有界：只放固定字段 + 计数 + 路径集合 digest，避免超过审计列宽导致持续写失败。
    /// 成功审计的摘要由 deletionId 与清理结果确定性生成，重放产生相同内容，配合 confirmed 写入实现幂等。
    /// </summary>
    private async Task<bool> WriteAuditAsync(
        ClientReleaseComponentDeletion deletion,
        ClientReleaseComponentDeletionOutcome outcome,
        bool succeeded,
        CancellationToken cancellationToken)
    {
        var summary = BuildBoundedSummary(deletion, outcome);
        return await auditTrailService.TryWriteConfirmedAsync(
            new AuditTrailEntry(
                deletion.RequestedByUserId,
                deletion.RequestedByUserName,
                AuditAction,
                "ClientRelease",
                deletion.Id.ToString(),
                deletion.UpdatedAtUtc,
                succeeded,
                summary,
                succeeded ? null : outcome.FailureCode),
            cancellationToken);
    }

    /// <summary>
    /// 有界摘要：组件标识 + 状态 + 计数 + 路径 digest + 身份/原因。不包含不受限的完整路径列表，
    /// 保证不超过审计 Summary 列宽（512）。TargetIdOrKey 用 deletionId 作为幂等键。
    /// </summary>
    private static string BuildBoundedSummary(
        ClientReleaseComponentDeletion deletion,
        ClientReleaseComponentDeletionOutcome outcome)
    {
        var pathsDigest = ComputePathsDigest(outcome.DeletedPaths, outcome.SkippedPaths);
        var payload = new
        {
            action = AuditAction,
            deletion.ComponentKind,
            deletion.ComponentKey,
            deletion.Channel,
            deletion.RetryCount,
            deleted = outcome.DeletedPaths.Count,
            skipped = outcome.SkippedPaths.Count,
            digest = pathsDigest,
            manifestChanged = outcome.ManifestChanged,
            by = deletion.RequestedByUserName,
            reason = deletion.Reason,
            failure = outcome.FailureCode
        };
        var json = JsonSerializer.Serialize(payload);
        // 双保险：极端超长原因仍截断，保证审计可写。
        return json.Length <= 480 ? json : json[..480];
    }

    private static string ComputePathsDigest(
        IReadOnlyList<string> deletedPaths,
        IReadOnlyList<string> skippedPaths)
    {
        var builder = new StringBuilder();
        foreach (var path in deletedPaths.OrderBy(path => path, StringComparer.Ordinal))
        {
            builder.Append("D:").Append(path).Append('\n');
        }

        foreach (var path in skippedPaths.OrderBy(path => path, StringComparer.Ordinal))
        {
            builder.Append("S:").Append(path).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
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
