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
            await WriteAuditAsync(
                component.Id,
                componentKind,
                componentName,
                channel,
                versions,
                succeeded: false,
                [],
                [],
                inUseReason,
                cancellationToken);
            return Result.Invalid(inUseReason);
        }

        var edgeRoot = artifactOptions.Value.ResolveEdgeUpdatesRoot();
        var plan = ClientReleaseComponentDeletionPlan.ForComponent(edgeRoot, component);

        var deletedPaths = new List<string>();
        Exception? deletionFailure = null;
        foreach (var target in plan.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                target.AssertSafe();
                deletedPaths.AddRange(target.RelativeFiles);
                target.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                deletionFailure = ex;
                break;
            }
        }

        if (deletionFailure is not null)
        {
            logger.LogError(
                new EventId(4601, "ClientReleaseHardDeleteFileFailure"),
                "Hard delete release component files failed. ComponentKind={ComponentKind} Channel={Channel} ErrorType={ErrorType}.",
                componentKind,
                channel,
                deletionFailure.GetType().Name);
            await WriteAuditAsync(
                component.Id,
                componentKind,
                componentName,
                channel,
                versions,
                succeeded: false,
                deletedPaths,
                plan.SkippedPaths,
                "部分发布文件删除失败，发布组件已保留，请修复文件状态后重试永久删除。",
                cancellationToken);
            return Result.Invalid("部分发布文件删除失败，发布组件未删除，可修复后重试。");
        }

        // 文件全部清理成功后，显式编排聚合删除并借助既有 cascade 移除 versions/artifacts。
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
            await WriteAuditAsync(
                component.Id,
                componentKind,
                componentName,
                channel,
                versions,
                succeeded: false,
                deletedPaths,
                plan.SkippedPaths,
                "发布文件已清理，但发布元数据删除提交失败，请重试永久删除以完成元数据清理。",
                CancellationToken.None);
            throw;
        }

        var warning = plan.SkippedPaths.Count == 0
            ? null
            : $"部分文件仍被存活版本 manifest 引用或不在受控范围，已跳过 {plan.SkippedPaths.Count} 项。";
        await WriteAuditAsync(
            component.Id,
            componentKind,
            componentName,
            channel,
            versions,
            succeeded: true,
            deletedPaths,
            plan.SkippedPaths,
            warning,
            cancellationToken);

        return Result.Success(new ClientReleaseComponentHardDeletionResultDto(
            component.Id,
            componentKind,
            componentName,
            channel,
            versions,
            deletedPaths.Count > 0,
            deletedPaths,
            plan.SkippedPaths,
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

    private async Task WriteAuditAsync(
        Guid componentId,
        string componentKind,
        string componentName,
        string channel,
        IReadOnlyList<string> versions,
        bool succeeded,
        IReadOnlyList<string> deletedPaths,
        IReadOnlyList<string> skippedPaths,
        string? failureOrWarning,
        CancellationToken cancellationToken)
    {
        var summary = JsonSerializer.Serialize(new
        {
            action = AuditAction,
            componentKind,
            componentName,
            channel,
            versions,
            deletedPaths,
            skippedPaths
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

internal sealed class ClientReleaseComponentDeletionPlan
{
    private ClientReleaseComponentDeletionPlan(
        IReadOnlyList<ClientReleaseFileDeletionTarget> targets,
        IReadOnlyList<string> skippedPaths)
    {
        Targets = targets;
        SkippedPaths = skippedPaths;
    }

    public IReadOnlyList<ClientReleaseFileDeletionTarget> Targets { get; }

    public IReadOnlyList<string> SkippedPaths { get; }

    public static ClientReleaseComponentDeletionPlan ForComponent(
        string edgeRoot,
        ClientReleaseComponent component)
    {
        var targets = new List<ClientReleaseFileDeletionTarget>();
        var skipped = new List<string>();

        if (component.ComponentKind == ClientReleaseComponentKind.Plugin)
        {
            // Plugin 永久删除清理整个 module 目录（覆盖该 channel 下全部版本）。
            var moduleRoot = Path.Combine(
                edgeRoot,
                "plugins",
                component.Channel,
                component.ComponentKey);
            TryAddDirectory(targets, edgeRoot, moduleRoot);
        }

        foreach (var version in component.Versions)
        {
            var versionPlan = ClientReleaseFileDeletionPlan.ForRelease(edgeRoot, component, version);
            targets.AddRange(versionPlan.Targets);
            foreach (var skip in versionPlan.SkippedPaths)
            {
                if (!skipped.Contains(skip, StringComparer.Ordinal))
                {
                    skipped.Add(skip);
                }
            }
        }

        return new ClientReleaseComponentDeletionPlan(targets, skipped);
    }

    private static void TryAddDirectory(
        ICollection<ClientReleaseFileDeletionTarget> targets,
        string edgeRoot,
        string path)
    {
        if (Directory.Exists(path))
        {
            targets.Add(ClientReleaseFileDeletionTarget.Directory(edgeRoot, path));
        }
    }
}
