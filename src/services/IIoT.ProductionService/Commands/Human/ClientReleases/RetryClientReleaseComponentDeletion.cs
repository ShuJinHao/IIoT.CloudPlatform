using System.Text.Json;
using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
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
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<string> SkippedPaths,
    string? FailureCode);

public sealed class RetryClientReleaseComponentDeletionHandler(
    IClientReleaseComponentDeletionStore deletionStore,
    IClientReleaseComponentDeletionProcessor deletionProcessor,
    ICurrentUser currentUser,
    IAuditTrailService auditTrailService)
    : ICommandHandler<RetryClientReleaseComponentDeletionCommand, Result<ClientReleaseComponentDeletionRetryResultDto>>
{
    private const string AuditAction = "ClientRelease.RetryHardDeleteComponent";

    public async Task<Result<ClientReleaseComponentDeletionRetryResultDto>> Handle(
        RetryClientReleaseComponentDeletionCommand request,
        CancellationToken cancellationToken)
    {
        var deletion = await deletionStore.GetByIdAsync(request.DeletionId, cancellationToken);
        if (deletion is null)
        {
            return Result.NotFound("删除操作不存在或已完成清理。");
        }

        deletion.ResetForRetry();
        await deletionStore.SaveChangesAsync(cancellationToken);

        var outcome = await deletionProcessor.ProcessAsync(deletion, cancellationToken);

        await WriteAuditAsync(deletion, outcome.Succeeded, outcome, cancellationToken);
        return Result.Success(new ClientReleaseComponentDeletionRetryResultDto(
            deletion.Id,
            deletion.ComponentId,
            deletion.ComponentKind,
            deletion.ComponentKey,
            deletion.Channel,
            outcome.Succeeded,
            outcome.DeletedPaths,
            outcome.SkippedPaths,
            outcome.FailureCode));
    }

    private async Task WriteAuditAsync(
        ClientReleaseComponentDeletion deletion,
        bool succeeded,
        ClientReleaseComponentDeletionOutcome outcome,
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
            deletedPaths = outcome.DeletedPaths,
            skippedPaths = outcome.SkippedPaths,
            failureCode = outcome.FailureCode
        });

        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ClientReleaseAuditActor.ParseId(currentUser.Id),
                currentUser.UserName,
                AuditAction,
                "ClientRelease",
                deletion.ComponentId.ToString(),
                DateTime.UtcNow,
                succeeded,
                summary,
                succeeded ? null : outcome.FailureCode),
            cancellationToken);
    }
}
