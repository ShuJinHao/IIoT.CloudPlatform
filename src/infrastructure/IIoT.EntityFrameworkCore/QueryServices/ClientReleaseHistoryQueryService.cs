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
                    version.Status == ClientReleaseStatus.Archived
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
                LatestHistoryAtUtc = component.Versions
                    .Where(version =>
                        version.Status == ClientReleaseStatus.Archived
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
                && (version.Status == ClientReleaseStatus.Archived
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
                version.DeletionFailure
            })
            .ToListAsync(cancellationToken);

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
                        version.DeletionFailure))
                    .ToList());

        var items = componentPage
            .Select(component => new ClientReleaseHistoryComponentReadItem(
                component.Id,
                component.ComponentKind.ToString(),
                component.ComponentKey,
                component.DisplayName,
                component.Channel,
                component.TargetRuntime,
                versionsByComponent.GetValueOrDefault(component.Id, [])))
            .ToList();

        return (items, totalCount);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
