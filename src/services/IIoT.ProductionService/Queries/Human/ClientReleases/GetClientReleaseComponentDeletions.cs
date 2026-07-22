using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.ClientReleases;

/// <summary>
/// 列出待清理/清理失败/审计待写的永久删除操作，供管理员找回 deletionId 并决定重试。
/// </summary>
[AdminOnly]
[AuthorizeRequirement(ClientReleasePermissions.HardDelete)]
public sealed record GetClientReleaseComponentDeletionsQuery()
    : IHumanQuery<Result<IReadOnlyList<ClientReleaseComponentDeletionDto>>>;

public sealed record ClientReleaseComponentDeletionDto(
    Guid DeletionId,
    Guid ComponentId,
    string ComponentKind,
    string ComponentKey,
    string Channel,
    string TargetRuntime,
    string Status,
    string? FailureCode,
    int RetryCount,
    string? Reason,
    string? RequestedByUserName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed class GetClientReleaseComponentDeletionsHandler(
    IClientReleaseComponentDeletionStore deletionStore)
    : IQueryHandler<GetClientReleaseComponentDeletionsQuery, Result<IReadOnlyList<ClientReleaseComponentDeletionDto>>>
{
    public async Task<Result<IReadOnlyList<ClientReleaseComponentDeletionDto>>> Handle(
        GetClientReleaseComponentDeletionsQuery request,
        CancellationToken cancellationToken)
    {
        var pending = await deletionStore.GetPendingAsync(cancellationToken);
        var result = pending
            .Select(deletion => new ClientReleaseComponentDeletionDto(
                deletion.Id,
                deletion.ComponentId,
                deletion.ComponentKind,
                deletion.ComponentKey,
                deletion.Channel,
                deletion.TargetRuntime,
                deletion.Status.ToString(),
                deletion.FailureCode,
                deletion.RetryCount,
                deletion.Reason,
                deletion.RequestedByUserName,
                deletion.CreatedAtUtc,
                deletion.UpdatedAtUtc))
            .ToList();

        return Result.Success<IReadOnlyList<ClientReleaseComponentDeletionDto>>(result);
    }
}
