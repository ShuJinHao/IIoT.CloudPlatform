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

        // 先删数据库元数据并记录组件的受控相对路径；文件清理在下面用同一份路径幂等重放。
        // 数据库不长期保存删除任务状态：成功只有本条审计，失败响应只携带稳定失败码，
        // 管理员用同一命令重试即可（组件元数据还在时重放文件删除，不在时按已删元数据处理）。
        var edgeRoot = artifactOptions.Value.ResolveEdgeUpdatesRoot();
        var relativePaths = ClientReleaseComponentRelativePaths.Collect(edgeRoot, component);
        var sharedNupkgNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (component.ComponentKind == ClientReleaseComponentKind.Host)
        {
            var survivingComponents = await componentRepository.GetListAsync(
                new ClientReleaseComponentsByChannelSpec(
                    component.Channel,
                    component.TargetRuntime,
                    onlyPublished: false),
                cancellationToken);
            foreach (var surviving in survivingComponents.Where(item => item.Id != component.Id))
            {
                foreach (var version in surviving.Versions)
                {
                    foreach (var artifact in version.Artifacts)
                    {
                        if (artifact.ArtifactKind == ClientReleaseArtifactKind.VelopackFile
                            && artifact.RelativePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                        {
                            sharedNupkgNames.Add(Path.GetFileName(artifact.RelativePath));
                        }
                    }
                }
            }
        }

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
                [],
                [],
                "发布组件元数据删除提交失败，未清理任何文件，请重试永久删除。",
                CancellationToken.None);
            throw;
        }

        var cleanup = new ClientReleaseComponentDeletionExecutor(artifactOptions, logger)
            .Execute(component, cancellationToken, sharedNupkgNames);
        if (!cleanup.Succeeded)
        {
            await WriteAuditAsync(
                component.Id,
                componentKind,
                componentName,
                channel,
                versions,
                succeeded: false,
                cleanup.DeletedPaths,
                cleanup.SkippedPaths,
                cleanup.FailureCode,
                CancellationToken.None);
            return Result.Invalid(
                $"发布组件元数据已删除，但发布文件清理未完成（{cleanup.FailureCode}）。请修复文件状态后重试永久删除命令以完成文件清理。");
        }

        var warning = cleanup.SkippedPaths.Count == 0
            ? null
            : $"部分文件仍被存活版本 manifest 引用或不在受控范围，已跳过 {cleanup.SkippedPaths.Count} 项。";
        await WriteAuditAsync(
            component.Id,
            componentKind,
            componentName,
            channel,
            versions,
            succeeded: true,
            cleanup.DeletedPaths,
            cleanup.SkippedPaths,
            warning,
            cancellationToken);

        return Result.Success(new ClientReleaseComponentHardDeletionResultDto(
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
