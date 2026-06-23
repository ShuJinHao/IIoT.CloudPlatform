using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.ClientReleases;

[AuthorizeRequirement(ClientReleasePermissions.Read)]
public sealed record GetClientReleaseCatalogQuery(
    string? Channel = null,
    string? TargetRuntime = null,
    bool OnlyPublished = false,
    bool IncludeArchived = false) : IHumanQuery<Result<ClientReleaseCatalogDto>>;

public sealed class GetClientReleaseCatalogHandler(
    IReadRepository<ClientHostRelease> hostReleaseRepository,
    IReadRepository<ClientPluginRelease> pluginReleaseRepository,
    IEdgeInstallerArtifactCatalogReader artifactCatalogReader)
    : IQueryHandler<GetClientReleaseCatalogQuery, Result<ClientReleaseCatalogDto>>
{
    public async Task<Result<ClientReleaseCatalogDto>> Handle(
        GetClientReleaseCatalogQuery request,
        CancellationToken cancellationToken)
    {
        var channel = NormalizeChannel(request.Channel);
        var hostReleases = await hostReleaseRepository.GetListAsync(
            new ClientHostReleasesByChannelSpec(
                channel,
                request.TargetRuntime,
                onlyPublished: false,
                includeArchived: true),
            cancellationToken);
        var pluginReleases = await pluginReleaseRepository.GetListAsync(
            new ClientPluginReleasesByChannelSpec(
                channel,
                request.TargetRuntime,
                onlyPublished: false,
                includeArchived: true),
            cancellationToken);
        var artifactCatalog = await artifactCatalogReader.ReadAsync(channel, request.TargetRuntime, cancellationToken);
        hostReleases = ClientReleaseCatalogMerge.MergeHostReleases(
            hostReleases,
            artifactCatalog.HostReleases,
            request.OnlyPublished,
            request.IncludeArchived).ToList();
        pluginReleases = ClientReleaseCatalogMerge.MergePluginReleases(
            pluginReleases,
            artifactCatalog.PluginReleases,
            request.OnlyPublished,
            request.IncludeArchived).ToList();

        return Result.Success(new ClientReleaseCatalogDto(
            ClientReleaseCatalogSchema.Version,
            channel,
            NormalizeOptional(request.TargetRuntime),
            ClientReleaseMapping.ToHostComponent(hostReleases),
            ClientReleaseMapping.ToPluginComponents(pluginReleases),
            DateTime.UtcNow));
    }

    private static string NormalizeChannel(string? channel)
    {
        return string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
