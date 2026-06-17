using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.ClientReleases;

public sealed record GetPublicClientDownloadsQuery(
    string? Channel = null,
    string? TargetRuntime = null) : IPublicQuery<Result<PublicClientDownloadCatalogDto>>;

public sealed class GetPublicClientDownloadsHandler(
    IReadRepository<ClientHostRelease> hostReleaseRepository,
    IReadRepository<ClientPluginRelease> pluginReleaseRepository,
    IClientReleaseRetentionPolicyReader retentionPolicyReader)
    : IQueryHandler<GetPublicClientDownloadsQuery, Result<PublicClientDownloadCatalogDto>>
{
    public async Task<Result<PublicClientDownloadCatalogDto>> Handle(
        GetPublicClientDownloadsQuery request,
        CancellationToken cancellationToken)
    {
        var channel = Normalize(request.Channel, "stable");
        var targetRuntime = Normalize(request.TargetRuntime, "win-x64");
        var hostReleases = await hostReleaseRepository.GetListAsync(
            new ClientHostReleasesByChannelSpec(channel, targetRuntime, onlyPublished: true),
            cancellationToken);
        var pluginReleases = await pluginReleaseRepository.GetListAsync(
            new ClientPluginReleasesByChannelSpec(channel, targetRuntime, onlyPublished: true),
            cancellationToken);
        var maxVersions = await retentionPolicyReader.GetMaxVersionsPerComponentAsync(cancellationToken);

        return Result.Success(new PublicClientDownloadCatalogDto(
            ClientReleaseCatalogSchema.Version,
            channel,
            targetRuntime,
            ClientReleaseMapping.ToPublicHostComponent(hostReleases, maxVersions),
            ClientReleaseMapping.ToPublicPluginComponents(pluginReleases, maxVersions),
            DateTime.UtcNow));
    }

    private static string Normalize(string? value, string fallback)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }
}
