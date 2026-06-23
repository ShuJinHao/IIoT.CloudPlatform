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
    IRepository<ClientHostRelease> repository,
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

        var spec = new ClientHostReleaseByIdentitySpec(
            request.Channel,
            request.Version,
            request.TargetRuntime);
        var release = await repository.GetSingleOrDefaultAsync(spec, cancellationToken);

        if (release is null)
        {
            release = new ClientHostRelease(
                request.Channel,
                request.Version,
                request.HostApiVersion,
                request.TargetRuntime,
                request.TargetFramework,
                request.DownloadUrl,
                request.Sha256,
                request.PackageSize,
                request.ReleaseNotes,
                status,
                request.Signature,
                request.Publisher);
            repository.Add(release);
        }
        else
        {
            release.UpdateRelease(
                request.HostApiVersion,
                request.TargetFramework,
                request.DownloadUrl,
                request.Sha256,
                request.PackageSize,
                request.ReleaseNotes,
                status,
                request.Signature,
                request.Publisher);
        }

        await repository.SaveChangesAsync(cancellationToken);
        await retentionService.ApplyHostPolicyAsync(
            request.Channel,
            request.TargetRuntime,
            cancellationToken);
        return Result.Success(new UpsertClientHostReleaseResultDto(release.Id));
    }
}
