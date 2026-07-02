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
public sealed record UpsertClientHostReleaseCommand(
    string Channel,
    string Version,
    string HostApiVersion,
    string TargetRuntime,
    string? TargetFramework,
    string DownloadUrl,
    string Sha256,
    long PackageSize,
    string? ReleaseNotes,
    string Status,
    string? Signature,
    string? Publisher) : IHumanCommand<Result<UpsertClientHostReleaseResultDto>>;

public sealed class UpsertClientHostReleaseHandler(
    IRepository<ClientReleaseComponent> repository,
    IClientReleaseRetentionService retentionService)
    : ICommandHandler<UpsertClientHostReleaseCommand, Result<UpsertClientHostReleaseResultDto>>
{
    public async Task<Result<UpsertClientHostReleaseResultDto>> Handle(
        UpsertClientHostReleaseCommand request,
        CancellationToken cancellationToken)
    {
        if (!ClientReleaseMapping.TryParseStatus(request.Status, out var status))
        {
            return Result.Invalid($"发布状态不支持: {request.Status}");
        }

        var spec = new ClientReleaseComponentByIdentitySpec(
            ClientReleaseComponentKind.Host,
            ClientReleaseComponent.HostComponentKey,
            request.Channel,
            request.TargetRuntime);
        var component = await repository.GetSingleOrDefaultAsync(spec, cancellationToken);

        if (component is null)
        {
            component = ClientReleaseComponent.CreateHost(
                request.Channel,
                request.TargetRuntime);
            repository.Add(component);
        }

        component.UpdateHostMetadata();
        var release = component.UpsertHostVersion(
            request.Version,
            request.HostApiVersion,
            request.TargetFramework,
            request.DownloadUrl,
            request.Sha256,
            request.PackageSize,
            request.ReleaseNotes,
            status,
            request.Signature,
            request.Publisher,
            artifacts: ClientReleaseArtifactBuilder.FromHostDownloadUrl(
                request.DownloadUrl,
                request.Channel,
                request.Version,
                request.Sha256,
                request.PackageSize));

        await repository.SaveChangesAsync(cancellationToken);
        await retentionService.ApplyHostPolicyAsync(
            request.Channel,
            request.TargetRuntime,
            cancellationToken);
        return Result.Success(new UpsertClientHostReleaseResultDto(release.Id));
    }
}
