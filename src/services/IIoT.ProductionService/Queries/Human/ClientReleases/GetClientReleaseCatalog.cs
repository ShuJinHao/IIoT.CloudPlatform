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
    IReadRepository<ClientReleaseComponent> componentRepository)
    : IQueryHandler<GetClientReleaseCatalogQuery, Result<ClientReleaseCatalogDto>>
{
    public async Task<Result<ClientReleaseCatalogDto>> Handle(
        GetClientReleaseCatalogQuery request,
        CancellationToken cancellationToken)
    {
        var channel = NormalizeChannel(request.Channel);
        var components = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(
                channel,
                request.TargetRuntime,
                request.OnlyPublished,
                includeArchived: request.IncludeArchived),
            cancellationToken);

        return Result.Success(new ClientReleaseCatalogDto(
            ClientReleaseCatalogSchema.Version,
            channel,
            NormalizeOptional(request.TargetRuntime),
            ClientReleaseMapping.ToHostComponent(
                components,
                onlyPublished: request.OnlyPublished,
                includeArchived: request.IncludeArchived),
            ClientReleaseMapping.ToPluginComponents(
                components,
                onlyPublished: request.OnlyPublished,
                includeArchived: request.IncludeArchived),
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
