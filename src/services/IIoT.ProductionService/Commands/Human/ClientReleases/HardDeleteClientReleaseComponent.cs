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
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AdminOnly]
[AuthorizeRequirement(ClientReleasePermissions.HardDelete)]
[DistributedLock(
    ClientReleasePublishLock.Resource,
    TimeoutSeconds = ClientReleasePublishLock.AcquireTimeoutSeconds)]
public sealed record HardDeleteClientReleaseComponentCommand(
    Guid ComponentId,
    string? Reason = null,
    string? Confirmation = null)
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
    IUnitOfWork unitOfWork,
    IClientReleaseWriteObservationReader observationReader)
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
            .OrderBy(version => version, Comparer<string>.Create(ClientReleaseSemanticVersion.Compare))
            .ToList();
        var validationError = ResolveValidationError(component, request);
        if (validationError is not null)
        {
            await WriteRequestAuditAsync(
                component.Id,
                componentKind,
                componentName,
                channel,
                versions,
                succeeded: false,
                validationError,
                cancellationToken);
            return Result.Invalid(validationError);
        }

        var baselineObservation =
            await CloudWriteCommitRecovery
                .TryObserveOptionalAttemptAsync(
                token => observationReader.ObserveComponentAsync(
                    component.Id,
                    token),
                cancellationToken)
            ?? throw new CloudWriteCommitUnknownException();
        var baseline = baselineObservation.Value
                       ?? throw new CloudWriteCommitUnknownException();
        if (ClientReleaseWriteStateFingerprint.ForComponent(component)
            != baseline)
        {
            throw new CloudWriteConflictException();
        }

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
        var deletionId = Guid.NewGuid();
        var createdAtUtc =
            ClientReleaseWriteCommitRecovery.NormalizeUtc(DateTime.UtcNow);
        var deletion = new ClientReleaseComponentDeletion(
            component.Id,
            componentKind,
            componentName,
            channel,
            component.TargetRuntime,
            versions,
            request.Reason!.Trim(),
            ClientReleaseAuditActor.ParseId(currentUser.Id),
            currentUser.UserName,
            fileTargets,
            deletionId,
            createdAtUtc);
        var expectedDeletion =
            ClientReleaseWriteStateFingerprint.ForDeletion(deletion);
        try
        {
            await unitOfWork.ExecuteResilientAsync(
                ExecuteAttemptAsync,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            var current =
                await CloudWriteCommitRecovery.TryObserveCommitAsync(
                    token => observationReader
                        .ObserveComponentDeletionAsync(
                            component.Id,
                            deletionId,
                            token));
            if (current is null
                || !MatchesCommittedTarget(current))
            {
                throw new OperationCanceledException(
                    cancellationToken);
            }
        }
        catch (CloudWriteException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch
        {
            var current =
                await CloudWriteCommitRecovery.TryObserveCommitAsync(
                    token => observationReader
                        .ObserveComponentDeletionAsync(
                            component.Id,
                            deletionId,
                            token));
            if (current is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new CloudWriteCommitUnknownException();
            }

            if (!MatchesCommittedTarget(current))
            {
                if (current.Component == baseline
                    && current.Deletion is null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new CloudWriteCommitUnknownException();
                }
                cancellationToken.ThrowIfCancellationRequested();
                throw new CloudWriteConflictException();
            }
        }

        using var deletionLoadTimeout =
            new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            deletion =
                await deletionStore.GetByIdAsync(
                    deletionId,
                    deletionLoadTimeout.Token);
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new CloudWriteCommitUnknownException();
        }

        if (deletion is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new CloudWriteCommitUnknownException();
        }

        var cleanup = await deletionProcessor.ProcessAsync(deletion, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!cleanup.Succeeded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Result.Invalid(
                $"发布组件元数据已删除，但发布文件清理未完成（{cleanup.FailureCode}）。请修复后通过永久删除重试入口按操作 ID {deletion.Id} 完成清理。");
        }

        // 成功审计未写稳时不得报告永久删除已完成：操作保持 CleanupCompleted，由重试/启动恢复补写审计。
        if (!cleanup.AuditConfirmed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Result.Invalid(
                $"发布文件清理已收敛，但成功审计尚未写稳，永久删除未完成。请通过永久删除重试入口按操作 ID {deletion.Id} 补写审计。");
        }

        var warning = cleanup.SkippedPaths.Count == 0
            ? null
            : $"部分文件仍被存活版本 manifest 引用或不在受控范围，已跳过 {cleanup.SkippedPaths.Count} 项。";

        cancellationToken.ThrowIfCancellationRequested();
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

        async Task<bool> ExecuteAttemptAsync(
            CancellationToken callbackCancellationToken)
        {
            var current =
                await CloudWriteCommitRecovery.TryObserveAttemptAsync(
                    token => observationReader
                        .ObserveComponentDeletionAsync(
                            component.Id,
                            deletionId,
                            token),
                    callbackCancellationToken)
                ?? throw new CloudWriteCommitUnknownException();
            if (MatchesCommittedTarget(current))
            {
                return true;
            }

            if (current.Component != baseline
                || current.Deletion is not null)
            {
                throw new CloudWriteConflictException();
            }

            var currentComponent =
                await componentRepository.GetSingleOrDefaultAsync(
                    new ClientReleaseComponentByComponentIdSpec(
                        component.Id),
                    callbackCancellationToken)
                ?? throw new CloudWriteConflictException();
            if (ClientReleaseWriteStateFingerprint.ForComponent(
                    currentComponent) != baseline)
            {
                throw new CloudWriteConflictException();
            }

            if (await deletionStore.GetByIdAsync(
                    deletionId,
                    callbackCancellationToken)
                is not null)
            {
                throw new CloudWriteConflictException();
            }

            deletionStore.Add(deletion);
            componentRepository.Delete(currentComponent);
            await componentRepository.SaveChangesAsync(
                callbackCancellationToken);
            return true;
        }

        bool MatchesCommittedTarget(
            ClientReleaseComponentDeletionWriteObservation current)
            => current.Component is null
               && current.Deletion is not null
               && ClientReleaseWriteCommitRecovery.MatchesDeletionTarget(
                   current.Deletion,
                   expectedDeletion);
    }

    private static string? ResolveValidationError(
        ClientReleaseComponent component,
        HardDeleteClientReleaseComponentCommand request)
    {
        if (component.ComponentKind != ClientReleaseComponentKind.Plugin)
        {
            return "Host 组件不允许整体删除。";
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return "整体删除必须提供非空原因。";
        }

        var expectedConfirmation = $"DELETE {component.ComponentKey}";
        if (!string.Equals(
                request.Confirmation,
                expectedConfirmation,
                StringComparison.Ordinal))
        {
            return $"二次确认不匹配，请精确输入 {expectedConfirmation}。";
        }

        if (component.Versions.Any(version =>
                version.Status is ClientReleaseStatus.Draft
                    or ClientReleaseStatus.Published
                    or ClientReleaseStatus.Deprecated
                    or ClientReleaseStatus.DeleteRequested
                    or ClientReleaseStatus.DeleteFailed))
        {
            return "仅允许整体删除全部版本均已归档或已删除的插件。";
        }

        return null;
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
