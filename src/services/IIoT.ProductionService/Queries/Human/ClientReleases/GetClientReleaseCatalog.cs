using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Queries.ClientReleases;

[AuthorizeRequirement(ClientReleasePermissions.Read)]
public sealed record GetClientReleaseCatalogQuery(
    string? Channel = null,
    string? TargetRuntime = null,
    bool OnlyPublished = false,
    bool IncludeArchived = false) : IHumanQuery<Result<ClientReleaseCatalogDto>>;

public sealed class GetClientReleaseCatalogHandler(
    IReadRepository<ClientReleaseComponent> componentRepository,
    IOptions<EdgeInstallerArtifactOptions> artifactOptions)
    : IQueryHandler<GetClientReleaseCatalogQuery, Result<ClientReleaseCatalogDto>>
{
    public async Task<Result<ClientReleaseCatalogDto>> Handle(
        GetClientReleaseCatalogQuery request,
        CancellationToken cancellationToken)
    {
        var channel = ClientReleaseText.NormalizeChannel(request.Channel);
        var components = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(
                channel,
                request.TargetRuntime,
                request.OnlyPublished,
                includeArchived: request.IncludeArchived),
            cancellationToken);

        var edgeRoot = artifactOptions.Value.ResolveEdgeUpdatesRoot();
        var missingPaths = ClientReleaseMissingFiles.Collect(edgeRoot, components);

        return Result.Success(new ClientReleaseCatalogDto(
            ClientReleaseCatalogSchema.Version,
            channel,
            ClientReleaseText.NormalizeOptional(request.TargetRuntime),
            ClientReleaseMapping.ToHostComponentExcludingMissingFiles(
                components,
                missingPaths,
                onlyPublished: request.OnlyPublished,
                includeArchived: request.IncludeArchived),
            ClientReleaseMapping.ToPluginComponentsExcludingMissingFiles(
                components,
                missingPaths,
                onlyPublished: request.OnlyPublished,
                includeArchived: request.IncludeArchived),
            DateTime.UtcNow));
    }

}
