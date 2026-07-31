using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AdminOnly]
[AuthorizeRequirement(ClientReleasePermissions.HardDelete)]
[DistributedLock(
    ClientReleasePublishLock.Resource,
    TimeoutSeconds = ClientReleasePublishLock.AcquireTimeoutSeconds)]
public sealed record RetryClientReleaseComponentDeletionCommand(Guid DeletionId)
    : IHumanCommand<Result<ClientReleaseComponentDeletionRetryResultDto>>;

public sealed record ClientReleaseComponentDeletionRetryResultDto(
    Guid DeletionId,
    Guid ComponentId,
    string ComponentKind,
    string ComponentKey,
    string Channel,
    bool Succeeded,
    bool AuditConfirmed,
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<string> SkippedPaths,
    string? FailureCode);

public sealed class RetryClientReleaseComponentDeletionHandler(
    IClientReleaseComponentDeletionStore deletionStore,
    IClientReleaseComponentDeletionProcessor deletionProcessor)
    : ICommandHandler<RetryClientReleaseComponentDeletionCommand, Result<ClientReleaseComponentDeletionRetryResultDto>>
{
    public async Task<Result<ClientReleaseComponentDeletionRetryResultDto>> Handle(
        RetryClientReleaseComponentDeletionCommand request,
        CancellationToken cancellationToken)
    {
        var deletion = await deletionStore.GetByIdAsync(request.DeletionId, cancellationToken);
        if (deletion is null)
        {
            return Result.NotFound("删除操作不存在或已完成清理。");
        }

        // Failed 的 CAS 重置、CleanupCompleted 的审计补写以及最终操作记录删除
        // 均由 processor 在 fresh observation 与 execution strategy 中收敛。
        var outcome = await deletionProcessor.ProcessAsync(deletion, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        return Result.Success(new ClientReleaseComponentDeletionRetryResultDto(
            deletion.Id,
            deletion.ComponentId,
            deletion.ComponentKind,
            deletion.ComponentKey,
            deletion.Channel,
            outcome.Succeeded && outcome.AuditConfirmed,
            outcome.AuditConfirmed,
            outcome.DeletedPaths,
            outcome.SkippedPaths,
            outcome.FailureCode));
    }
}
