using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
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
    private const string SuccessAuditIdempotencyKeyPrefix = "client-release-hard-delete-completed:";
    public const string FailureCleanupStateInvalid = "CleanupStateInvalid";

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<ClientReleaseComponentDeletionOutcome> ProcessAsync(
        ClientReleaseComponentDeletion deletion,
        CancellationToken cancellationToken)
    {
        // CleanupCompleted 的文件系统结果已经持久化。恢复/重试只能补写同一条成功审计，
        // 不能再次扫描当前文件系统重新计算结果，否则崩溃窗口会产生不同摘要和重复审计。
        if (deletion.Status == ClientReleaseComponentDeletionStatus.CleanupCompleted)
        {
            if (!deletion.TryGetCleanupResult(out var persistedResult)
                || persistedResult is null)
            {
                return await MarkFailedAsync(
                    deletion,
                    new ClientReleaseComponentDeletionOutcome(
                        false,
                        [],
                        [],
                        FailureCleanupStateInvalid,
                        false),
                    cancellationToken);
            }

            var persistedOutcome = new ClientReleaseComponentDeletionOutcome(
                true,
                persistedResult.DeletedPaths,
                persistedResult.SkippedPaths,
                null,
                persistedResult.ManifestChanged);
            return await ConfirmSuccessAndRemoveAsync(
                deletion,
                persistedOutcome,
                cancellationToken);
        }

        ClientReleaseComponentDeletionOutcome outcome;
        try
        {
            var (survivingVelopackArtifacts, hasSurvivingHost) = await CollectSurvivingHostStateAsync(
                deletion,
                cancellationToken);
            outcome = new ClientReleaseComponentDeletionExecutor(artifactOptions, logger)
                .Execute(deletion, survivingVelopackArtifacts, hasSurvivingHost, cancellationToken);
        }
        catch (ClientReleaseValidationException ex)
        {
            // 受控文件安全失败（穿透/事实不匹配/reparse）：落 Failed + 稳定失败码 + 失败审计。
            logger.LogWarning(
                new EventId(4615, "ClientReleaseComponentDeletionSecurityFailure"),
                "Client release component deletion hit a controlled-file security failure. DeletionId={DeletionId} Reason={Reason}.",
                deletion.Id,
                ex.SafeMessage);
            return await MarkFailedAsync(
                deletion,
                new ClientReleaseComponentDeletionOutcome(
                    false,
                    [],
                    [],
                    ClientReleaseComponentDeletionExecutor.FailureFileFactsMismatch,
                    false),
                cancellationToken);
        }

        if (!outcome.Succeeded)
        {
            return await MarkFailedAsync(deletion, outcome, cancellationToken);
        }

        // 第一阶段：清理收敛落库，操作进入 CleanupCompleted（审计待写）。
        deletion.MarkCleanupCompleted(
            outcome.DeletedPaths,
            outcome.SkippedPaths,
            outcome.ManifestChanged);
        await deletionStore.SaveChangesAsync(cancellationToken);

        return await ConfirmSuccessAndRemoveAsync(deletion, outcome, cancellationToken);
    }

    private async Task<ClientReleaseComponentDeletionOutcome> MarkFailedAsync(
        ClientReleaseComponentDeletion deletion,
        ClientReleaseComponentDeletionOutcome outcome,
        CancellationToken cancellationToken)
    {
        deletion.MarkFailed(outcome.FailureCode ?? FailureCleanupStateInvalid);
        await deletionStore.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(deletion, outcome, succeeded: false, CancellationToken.None);
        return outcome;
    }

    private async Task<ClientReleaseComponentDeletionOutcome> ConfirmSuccessAndRemoveAsync(
        ClientReleaseComponentDeletion deletion,
        ClientReleaseComponentDeletionOutcome outcome,
        CancellationToken cancellationToken)
    {
        // 第二阶段：成功审计写稳后才删除操作记录。审计未写稳则操作保持 CleanupCompleted，
        // 由启动恢复或管理员重试直接使用持久化清理结果补写；数据库唯一幂等键保证并发/崩溃
        // 重放只有一条成功审计。审计未确认时调用方不得报告删除已完成。
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
        await deletionStore.SaveChangesAsync(cancellationToken);
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
        var executedAtUtc = succeeded
            ? deletion.CleanupCompletedAtUtc ?? deletion.UpdatedAtUtc
            : deletion.UpdatedAtUtc;
        return await auditTrailService.TryWriteConfirmedAsync(
            new AuditTrailEntry(
                deletion.RequestedByUserId,
                deletion.RequestedByUserName,
                AuditAction,
                "ClientRelease",
                deletion.Id.ToString(),
                executedAtUtc,
                succeeded,
                summary,
                succeeded ? null : outcome.FailureCode,
                succeeded ? $"{SuccessAuditIdempotencyKeyPrefix}{deletion.Id:N}" : null),
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
        var reason = deletion.Reason ?? string.Empty;
        var payload = new
        {
            action = AuditAction,
            componentKind = BoundText(deletion.ComponentKind, 32),
            componentKey = BoundText(deletion.ComponentKey, 64),
            channel = BoundText(deletion.Channel, 32),
            retry = deletion.RetryCount,
            deleted = outcome.DeletedPaths.Count,
            skipped = outcome.SkippedPaths.Count,
            pathsDigest,
            manifestChanged = outcome.ManifestChanged,
            by = BoundText(deletion.RequestedByUserName, 48),
            reason = BoundText(reason, 64),
            reasonDigest = ComputeTextDigest(reason),
            failure = BoundText(outcome.FailureCode, 64)
        };
        var json = JsonSerializer.Serialize(payload, AuditJsonOptions);
        if (json.Length <= 512)
        {
            return json;
        }

        // 极端长 Unicode 转义或字段组合仍走合法 JSON 的确定性紧凑格式，绝不截断 JSON。
        return JsonSerializer.Serialize(
            new
            {
                kind = BoundText(deletion.ComponentKind, 16),
                key = BoundText(deletion.ComponentKey, 32),
                channel = BoundText(deletion.Channel, 16),
                deleted = outcome.DeletedPaths.Count,
                skipped = outcome.SkippedPaths.Count,
                pathsDigest,
                reason = BoundText(reason, 32),
                reasonDigest = ComputeTextDigest(reason),
                failure = BoundText(outcome.FailureCode, 32)
            },
            AuditJsonOptions);
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

    private static string ComputeTextDigest(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static string? BoundText(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        var length = maxLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length -= 1;
        }

        return value[..length];
    }

    private async Task<(IReadOnlyCollection<ClientReleaseVelopackArtifactFact> Artifacts, bool HasSurvivingHost)> CollectSurvivingHostStateAsync(
        ClientReleaseComponentDeletion deletion,
        CancellationToken cancellationToken)
    {
        var artifactsByName = new Dictionary<string, ClientReleaseVelopackArtifactFact>(
            StringComparer.OrdinalIgnoreCase);
        if (!string.Equals(deletion.ComponentKind, "Host", StringComparison.OrdinalIgnoreCase))
        {
            return (artifactsByName.Values, false);
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
                    if (artifact.ArtifactKind != ClientReleaseArtifactKind.VelopackFile)
                    {
                        continue;
                    }

                    var fileName = Path.GetFileName(artifact.RelativePath);
                    if (ClientReleaseVelopackPaths.IsChannelManifest(fileName))
                    {
                        continue;
                    }

                    var expectedPrefix = $"velopack/{deletion.Channel}/";
                    if (!artifact.RelativePath.StartsWith(expectedPrefix, StringComparison.Ordinal)
                        || artifact.RelativePath[expectedPrefix.Length..].Contains('/', StringComparison.Ordinal)
                        || !ClientReleaseFileFacts.IsSha256(artifact.Sha256)
                        || !artifact.Size.HasValue
                        || artifact.Size.Value < 0)
                    {
                        throw new ClientReleaseValidationException(
                            "存活 Host 的 Velopack 文件登记事实不完整或路径非法。");
                    }

                    var fact = new ClientReleaseVelopackArtifactFact(
                        fileName,
                        artifact.Sha256!,
                        artifact.Size.Value);
                    if (artifactsByName.TryGetValue(fileName, out var existing)
                        && (!string.Equals(existing.Sha256, fact.Sha256, StringComparison.OrdinalIgnoreCase)
                            || existing.SizeBytes != fact.SizeBytes))
                    {
                        throw new ClientReleaseValidationException(
                            "存活 Host 的 Velopack 文件登记事实冲突。");
                    }

                    artifactsByName[fileName] = fact;
                }
            }
        }

        return (artifactsByName.Values, hasSurvivingHost);
    }

    private static bool IsSurvivingVersion(ClientReleaseVersion version)
        => version.Status is ClientReleaseStatus.Published
            or ClientReleaseStatus.Deprecated
            or ClientReleaseStatus.Draft;
}
