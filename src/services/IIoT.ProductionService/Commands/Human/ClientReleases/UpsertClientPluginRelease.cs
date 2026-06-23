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
public sealed record UpsertClientPluginReleaseCommand(
    string ModuleId,
    string DisplayName,
    string? Description,
    string? IconKind,
    string? AccentColor,
    string Channel,
    string Version,
    string HostApiVersion,
    string MinHostVersion,
    string MaxHostVersion,
    string TargetRuntime,
    string? TargetFramework,
    string DownloadUrl,
    string Sha256,
    long PackageSize,
    string? ReleaseNotes,
    string? DependenciesJson,
    string Status,
    string? Signature,
    string? Publisher) : IHumanCommand<Result<UpsertClientPluginReleaseResultDto>>;

public sealed class UpsertClientPluginReleaseHandler(
    IRepository<ClientPluginRelease> repository,
    IClientReleaseRetentionService retentionService)
    : ICommandHandler<UpsertClientPluginReleaseCommand, Result<UpsertClientPluginReleaseResultDto>>
{
    public async Task<Result<UpsertClientPluginReleaseResultDto>> Handle(
        UpsertClientPluginReleaseCommand request,
        CancellationToken cancellationToken)
    {
        if (!ClientReleaseMapping.TryParseStatus(request.Status, out var status))
        {
            return Result.Invalid($"发布状态不支持: {request.Status}");
        }

        var dependenciesJson = string.IsNullOrWhiteSpace(request.DependenciesJson)
            ? "[]"
            : request.DependenciesJson.Trim();
        var spec = new ClientPluginReleaseByIdentitySpec(
            request.ModuleId,
            request.Channel,
            request.Version,
            request.TargetRuntime);
        var release = await repository.GetSingleOrDefaultAsync(spec, cancellationToken);

        if (release is null)
        {
            release = new ClientPluginRelease(
                request.ModuleId,
                request.DisplayName,
                request.Description,
                request.IconKind,
                request.AccentColor,
                request.Channel,
                request.Version,
                request.HostApiVersion,
                request.MinHostVersion,
                request.MaxHostVersion,
                request.TargetRuntime,
                request.TargetFramework,
                request.DownloadUrl,
                request.Sha256,
                request.PackageSize,
                request.ReleaseNotes,
                dependenciesJson,
                status,
                request.Signature,
                request.Publisher);
            repository.Add(release);
        }
        else
        {
            release.UpdateRelease(
                request.DisplayName,
                request.Description,
                request.IconKind,
                request.AccentColor,
                request.HostApiVersion,
                request.MinHostVersion,
                request.MaxHostVersion,
                request.TargetFramework,
                request.DownloadUrl,
                request.Sha256,
                request.PackageSize,
                request.ReleaseNotes,
                dependenciesJson,
                status,
                request.Signature,
                request.Publisher);
        }

        await repository.SaveChangesAsync(cancellationToken);
        await retentionService.ApplyPluginPolicyAsync(
            request.ModuleId,
            request.Channel,
            request.TargetRuntime,
            cancellationToken);
        return Result.Success(new UpsertClientPluginReleaseResultDto(release.Id));
    }
}
