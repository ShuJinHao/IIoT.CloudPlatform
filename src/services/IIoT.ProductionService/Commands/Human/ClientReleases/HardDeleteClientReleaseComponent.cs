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
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AdminOnly]
[AuthorizeRequirement(ClientReleasePermissions.HardDelete)]
[DistributedLock(
    ClientReleasePublishLock.Resource,
    TimeoutSeconds = ClientReleasePublishLock.AcquireTimeoutSeconds)]
public sealed record HardDeleteClientReleaseComponentCommand(Guid ComponentId, string? Reason = null)
    : IHumanCommand<Result<ClientReleaseComponentHardDeletionResultDto>>;

public sealed record ClientReleaseComponentHardDeletionResultDto(
    Guid DeletionId,
    Guid ComponentId,
    string ComponentKind,
    string ComponentName,
    string Channel,
    IReadOnlyList<string> Versions,
    bool FilesDeleted,
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<string> SkippedPaths,
    string? Warning);

public sealed class HardDeleteClientReleaseComponentHandler(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    IRepository<ClientReleaseComponent> componentRepository,
    IDeviceClientStateStore clientStateStore,
    IClientReleaseComponentDeletionStore deletionStore,
    IClientReleaseComponentDeletionProcessor deletionProcessor,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService,
    ILogger<HardDeleteClientReleaseComponentHandler> logger)
    : ICommandHandler<HardDeleteClientReleaseComponentCommand, Result<ClientReleaseComponentHardDeletionResultDto>>
{
    private const string AuditAction = "ClientRelease.HardDeleteComponent";

    public async Task<Result<ClientReleaseComponentHardDeletionResultDto>> Handle(
        HardDeleteClientReleaseComponentCommand request,
        CancellationToken cancellationToken)
    {
        var component = await componentRepository.GetSingleOrDefaultAsync(
            new ClientReleaseComponentByComponentIdSpec(request.ComponentId),
            cancellationToken);
        if (component is null)
        {
            return Result.NotFound("发布组件不存在。");
        }

        var componentKind = component.ComponentKind == ClientReleaseComponentKind.Host ? "Host" : "Plugin";
        var componentName = component.ComponentKey;
        var channel = component.Channel;
        var versions = component.Versions
            .Select(version => version.Version)
            .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 在锁内重新读取设备快照判断在用，不使用页面加载时的旧结论。
        var inUseReason = await ResolveInUseReasonAsync(component, cancellationToken);
        if (inUseReason is not null)
        {
            await WriteRequestAuditAsync(
                component.Id,
                componentKind,
                componentName,
                channel,
                versions,
                succeeded: false,
                inUseReason,
                cancellationToken);
            return Result.Invalid(inUseReason);
        }

        // 同事务写入持久化删除操作（含管理员身份、删除原因、精确文件事实）并删除组件元数据；
        // 文件清理由操作 ID 驱动，可在新进程重试。允许“只有元数据、没有文件”的错误组件。
        var edgeRoot = artifactOptions.Value.ResolveEdgeUpdatesRoot();
        var fileTargets = ClientReleaseComponentRelativePaths.Collect(edgeRoot, component);
        var deletion = new ClientReleaseComponentDeletion(
            component.Id,
            componentKind,
            componentName,
            channel,
            component.TargetRuntime,
            versions,
            request.Reason,
            ClientReleaseAuditActor.ParseId(currentUser.Id),
            currentUser.UserName,
            fileTargets);

        deletionStore.Add(deletion);
        componentRepository.Delete(component);
        try
        {
            await componentRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                new EventId(4602, "ClientReleaseHardDeleteCommitFailure"),
                "Hard delete release component database commit failed. ComponentKind={ComponentKind} Channel={Channel} ErrorType={ErrorType}.",
                componentKind,
                channel,
                ex.GetType().Name);
            await WriteRequestAuditAsync(
                component.Id,
                componentKind,
                componentName,
                channel,
                versions,
                succeeded: false,
                "发布组件元数据删除提交失败，未清理任何文件，请重试永久删除。",
                CancellationToken.None);
            throw;
        }

        var cleanup = await deletionProcessor.ProcessAsync(deletion, cancellationToken);
        if (!cleanup.Succeeded)
        {
            return Result.Invalid(
                $"发布组件元数据已删除，但发布文件清理未完成（{cleanup.FailureCode}）。请修复后通过永久删除重试入口按操作 ID {deletion.Id} 完成清理。");
        }

        // 成功审计未写稳时不得报告永久删除已完成：操作保持 CleanupCompleted，由重试/启动恢复补写审计。
        if (!cleanup.AuditConfirmed)
        {
            return Result.Invalid(
                $"发布文件清理已收敛，但成功审计尚未写稳，永久删除未完成。请通过永久删除重试入口按操作 ID {deletion.Id} 补写审计。");
        }

        var warning = cleanup.SkippedPaths.Count == 0
            ? null
            : $"部分文件仍被存活版本 manifest 引用或不在受控范围，已跳过 {cleanup.SkippedPaths.Count} 项。";

        return Result.Success(new ClientReleaseComponentHardDeletionResultDto(
            deletion.Id,
            component.Id,
            componentKind,
            componentName,
            channel,
            versions,
            cleanup.DeletedPaths.Count > 0,
            cleanup.DeletedPaths,
            cleanup.SkippedPaths,
            warning));
    }

    private async Task<string?> ResolveInUseReasonAsync(
        ClientReleaseComponent component,
        CancellationToken cancellationToken)
    {
        var snapshots = await clientStateStore.GetVersionSnapshotsByDevicesAsync(cancellationToken: cancellationToken);
        if (component.ComponentKind == ClientReleaseComponentKind.Host)
        {
            var inUse = snapshots.Any(snapshot =>
                component.Versions.Any(version =>
                    string.Equals(snapshot.HostVersion, version.Version, StringComparison.OrdinalIgnoreCase)));
            return inUse
                ? "已有设备当前宿主版本等于目标组件版本，禁止永久删除发布组件。"
                : null;
        }

        var pluginInUse = snapshots.Any(snapshot => snapshot.InstalledPlugins.Any(plugin =>
            string.Equals(plugin.ModuleId, component.ComponentKey, StringComparison.OrdinalIgnoreCase)));
        return pluginInUse
            ? "已有设备当前上报相同 ModuleId 的插件，禁止永久删除发布组件。"
            : null;
    }

    private async Task WriteRequestAuditAsync(
        Guid componentId,
        string componentKind,
        string componentName,
        string channel,
        IReadOnlyList<string> versions,
        bool succeeded,
        string? failureOrWarning,
        CancellationToken cancellationToken)
    {
        var summary = System.Text.Json.JsonSerializer.Serialize(new
        {
            action = AuditAction,
            componentKind,
            componentName,
            channel,
            versions
        });

        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ClientReleaseAuditActor.ParseId(currentUser.Id),
                currentUser.UserName,
                AuditAction,
                "ClientRelease",
                componentId.ToString(),
                DateTime.UtcNow,
                succeeded,
                summary,
                succeeded ? null : failureOrWarning),
            cancellationToken);
    }
}
