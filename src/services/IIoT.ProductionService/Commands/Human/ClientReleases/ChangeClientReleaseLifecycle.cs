using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.ClientReleases;

[AuthorizeRequirement("Device.Update")]
[AdminOnly]
public sealed record ArchiveClientReleaseCommand(Guid ReleaseId)
    : IHumanCommand<Result>;

[AuthorizeRequirement("Device.Update")]
[AdminOnly]
public sealed record UpdateClientReleaseStatusCommand(
    Guid ReleaseId,
    string Status) : IHumanCommand<Result>;

public sealed class ArchiveClientReleaseHandler(
    IRepository<ClientHostRelease> hostRepository,
    IRepository<ClientPluginRelease> pluginRepository)
    : ICommandHandler<ArchiveClientReleaseCommand, Result>
{
    public async Task<Result> Handle(ArchiveClientReleaseCommand request, CancellationToken cancellationToken)
    {
        return await ClientReleaseLifecycleCommandHelper.ChangeStatus(
            hostRepository,
            pluginRepository,
            request.ReleaseId,
            ClientReleaseStatus.Archived,
            cancellationToken);
    }
}

public sealed class UpdateClientReleaseStatusHandler(
    IRepository<ClientHostRelease> hostRepository,
    IRepository<ClientPluginRelease> pluginRepository)
    : ICommandHandler<UpdateClientReleaseStatusCommand, Result>
{
    public async Task<Result> Handle(UpdateClientReleaseStatusCommand request, CancellationToken cancellationToken)
    {
        if (!ClientReleaseMapping.TryParseStatus(request.Status, out var status))
        {
            return Result.Invalid($"发布状态不支持: {request.Status}");
        }

        return await ClientReleaseLifecycleCommandHelper.ChangeStatus(
            hostRepository,
            pluginRepository,
            request.ReleaseId,
            status,
            cancellationToken);
    }
}

internal static class ClientReleaseLifecycleCommandHelper
{
    public static async Task<Result> ChangeStatus(
        IRepository<ClientHostRelease> hostRepository,
        IRepository<ClientPluginRelease> pluginRepository,
        Guid releaseId,
        ClientReleaseStatus status,
        CancellationToken cancellationToken)
    {
        var host = await hostRepository.GetSingleOrDefaultAsync(
            new ClientHostReleaseByIdSpec(releaseId),
            cancellationToken);
        if (host is not null)
        {
            host.ChangeStatus(status);
            await hostRepository.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var plugin = await pluginRepository.GetSingleOrDefaultAsync(
            new ClientPluginReleaseByIdSpec(releaseId),
            cancellationToken);
        if (plugin is not null)
        {
            plugin.ChangeStatus(status);
            await pluginRepository.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        return Result.NotFound("发布版本不存在。");
    }
}
