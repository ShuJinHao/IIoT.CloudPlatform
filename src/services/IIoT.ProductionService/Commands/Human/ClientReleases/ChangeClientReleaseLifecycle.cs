using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AuthorizeRequirement(ClientReleasePermissions.Manage)]
public sealed record ArchiveClientReleaseCommand(Guid ReleaseId)
    : IHumanCommand<Result>;

[AuthorizeRequirement(ClientReleasePermissions.Manage)]
public sealed record UpdateClientReleaseStatusCommand(
    Guid ReleaseId,
    string Status) : IHumanCommand<Result>;

public sealed class ArchiveClientReleaseHandler(
    IRepository<ClientReleaseComponent> componentRepository)
    : ICommandHandler<ArchiveClientReleaseCommand, Result>
{
    public async Task<Result> Handle(ArchiveClientReleaseCommand request, CancellationToken cancellationToken)
    {
        return await ClientReleaseLifecycleCommandHelper.ChangeStatus(
            componentRepository,
            request.ReleaseId,
            ClientReleaseStatus.Archived,
            cancellationToken);
    }
}

public sealed class UpdateClientReleaseStatusHandler(
    IRepository<ClientReleaseComponent> componentRepository)
    : ICommandHandler<UpdateClientReleaseStatusCommand, Result>
{
    public async Task<Result> Handle(UpdateClientReleaseStatusCommand request, CancellationToken cancellationToken)
    {
        if (!ClientReleaseMapping.TryParseStatus(request.Status, out var status))
        {
            return Result.Invalid($"发布状态不支持: {request.Status}");
        }

        return await ClientReleaseLifecycleCommandHelper.ChangeStatus(
            componentRepository,
            request.ReleaseId,
            status,
            cancellationToken);
    }
}

internal static class ClientReleaseLifecycleCommandHelper
{
    public static async Task<Result> ChangeStatus(
        IRepository<ClientReleaseComponent> componentRepository,
        Guid releaseId,
        ClientReleaseStatus status,
        CancellationToken cancellationToken)
    {
        var component = await componentRepository.GetSingleOrDefaultAsync(
            new ClientReleaseComponentByVersionIdSpec(releaseId),
            cancellationToken);
        if (component is not null)
        {
            component.ChangeVersionStatus(releaseId, status);
            await componentRepository.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        return Result.NotFound("发布版本不存在。");
    }
}
