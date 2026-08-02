using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Services.Contracts.RecordQueries;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.QueryServices;

public sealed class ClientReleaseHistoryQueryService(IIoTDbContext dbContext)
    : IClientReleaseHistoryQueryService
{
    public async Task<(IReadOnlyList<ClientReleaseHistoryComponentReadItem> Items, int TotalCount)> GetPagedAsync(
        ClientReleaseHistoryQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        var channel = NormalizeOptional(request.Channel);
        var targetRuntime = NormalizeOptional(request.TargetRuntime);
        var skip = Math.Max(0, request.Skip);
        var take = Math.Clamp(request.Take, 1, 100);

        var components = dbContext.ClientReleaseComponents
            .AsNoTracking()
            .Where(component =>
                (channel == null || component.Channel == channel)
                && (targetRuntime == null || component.TargetRuntime == targetRuntime)
                && component.Versions.Any(version =>
                    version.Status == ClientReleaseStatus.Deprecated
                    || version.Status == ClientReleaseStatus.Archived
                    || version.Status == ClientReleaseStatus.Deleted
                    || version.Status == ClientReleaseStatus.DeleteFailed));

        var totalCount = await components.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return ([], 0);
        }

        var componentPage = await components
            .Select(component => new
            {
                component.Id,
                component.ComponentKind,
                component.ComponentKey,
                component.DisplayName,
                component.Channel,
                component.TargetRuntime,
                CanHardDelete = component.ComponentKind == ClientReleaseComponentKind.Plugin
                    && component.Versions.All(version =>
                        version.Status == ClientReleaseStatus.Archived
                        || version.Status == ClientReleaseStatus.Deleted),
                LatestHistoryAtUtc = component.Versions
                    .Where(version =>
                        version.Status == ClientReleaseStatus.Deprecated
                        || version.Status == ClientReleaseStatus.Archived
                        || version.Status == ClientReleaseStatus.Deleted
                        || version.Status == ClientReleaseStatus.DeleteFailed)
                    .Max(version => version.DeletedAtUtc ?? version.PublishedAtUtc ?? version.CreatedAtUtc)
            })
            .OrderByDescending(component => component.LatestHistoryAtUtc)
            .ThenBy(component => component.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (componentPage.Count == 0)
        {
            return ([], totalCount);
        }

        var componentIds = componentPage
            .Select(component => component.Id)
            .ToArray();
        var versionRows = await dbContext.Set<ClientReleaseVersion>()
            .AsNoTracking()
            .Where(version =>
                componentIds.Contains(version.ClientReleaseComponentId)
                && (version.Status == ClientReleaseStatus.Deprecated
                    || version.Status == ClientReleaseStatus.Archived
                    || version.Status == ClientReleaseStatus.Deleted
                    || version.Status == ClientReleaseStatus.DeleteFailed))
            .OrderByDescending(version => version.DeletedAtUtc ?? version.PublishedAtUtc ?? version.CreatedAtUtc)
            .ThenBy(version => version.Id)
            .Select(version => new
            {
                version.ClientReleaseComponentId,
                version.Id,
                version.Version,
                version.Status,
                version.CreatedAtUtc,
                version.PublishedAtUtc,
                version.DeletedAtUtc,
                version.DeletionReason,
                version.DeletionFailure,
                version.ReleaseNotes,
                version.Sha256,
                version.PackageSize,
                version.Publisher,
                version.Signature,
                version.DownloadUrl,
                version.HostApiVersion,
                version.TargetFramework,
                version.MinHostVersion,
                version.MaxHostVersion,
                version.DependenciesJson
            })
            .ToListAsync(cancellationToken);

        var versionIds = versionRows.Select(version => version.Id).ToArray();
        var artifactRows = await dbContext.Set<ClientReleaseArtifact>()
            .AsNoTracking()
            .Where(artifact => versionIds.Contains(artifact.ClientReleaseVersionId))
            .OrderBy(artifact => artifact.RelativePath)
            .ThenBy(artifact => artifact.Id)
            .Select(artifact => new
            {
                artifact.ClientReleaseVersionId,
                artifact.ArtifactKind,
                artifact.RelativePath,
                artifact.Sha256,
                artifact.Size
            })
            .ToListAsync(cancellationToken);

        var artifactsByVersion = artifactRows
            .GroupBy(artifact => artifact.ClientReleaseVersionId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ClientReleaseHistoryArtifactReadItem>)group
                    .Select(artifact => new ClientReleaseHistoryArtifactReadItem(
                        artifact.ArtifactKind.ToString(),
                        artifact.RelativePath,
                        artifact.Sha256,
                        artifact.Size))
                    .ToList());

        var versionsByComponent = versionRows
            .GroupBy(version => version.ClientReleaseComponentId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ClientReleaseHistoryVersionReadItem>)group
                    .Select(version => new ClientReleaseHistoryVersionReadItem(
                        version.Id,
                        version.Version,
                        version.Status.ToString(),
                        version.CreatedAtUtc,
                        version.PublishedAtUtc,
                        version.DeletedAtUtc,
                        version.DeletionReason,
                        version.DeletionFailure,
                        version.ReleaseNotes,
                        version.Sha256,
                        version.PackageSize,
                        version.Publisher,
                        version.Signature,
                        version.DownloadUrl,
                        version.HostApiVersion,
                        version.TargetFramework,
                        version.MinHostVersion,
                        version.MaxHostVersion,
                        version.DependenciesJson,
                        artifactsByVersion.GetValueOrDefault(version.Id, [])))
                    .ToList());

        var items = componentPage
            .Select(component => new ClientReleaseHistoryComponentReadItem(
                component.Id,
                component.ComponentKind.ToString(),
                component.ComponentKey,
                component.DisplayName,
                component.Channel,
                component.TargetRuntime,
                versionsByComponent.GetValueOrDefault(component.Id, []),
                component.CanHardDelete))
            .ToList();

        return (items, totalCount);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
