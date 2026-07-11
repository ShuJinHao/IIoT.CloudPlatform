using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.ClientReleases;

public sealed class EfClientReleaseVersionObservationReader(
    DbContextOptions<IIoTDbContext> options)
    : IClientReleaseVersionObservationReader
{
    public async Task<IReadOnlyList<ClientReleaseVersionObservation>> ObserveAsync(
        IReadOnlyCollection<ClientReleaseVersionIdentity> identities,
        CancellationToken cancellationToken)
    {
        if (identities.Count == 0)
        {
            return [];
        }

        var requested = identities.Distinct().ToArray();
        var componentKinds = requested.Select(identity => identity.ComponentKind).Distinct().ToArray();
        var componentKeys = requested.Select(identity => identity.ComponentKey).Distinct().ToArray();
        var channels = requested.Select(identity => identity.Channel).Distinct().ToArray();
        var targetRuntimes = requested.Select(identity => identity.TargetRuntime).Distinct().ToArray();
        var versions = requested.Select(identity => identity.Version).Distinct().ToArray();
        await using var context = new IIoTDbContext(options);
        var components = await context.ClientReleaseComponents
            .AsNoTracking()
            .Where(component =>
                componentKinds.Contains(component.ComponentKind)
                && componentKeys.Contains(component.ComponentKey)
                && channels.Contains(component.Channel)
                && targetRuntimes.Contains(component.TargetRuntime))
            .Include(component => component.Versions.Where(version => versions.Contains(version.Version)))
            .ThenInclude(version => version.Artifacts)
            .AsSingleQuery()
            .ToListAsync(cancellationToken);

        var observations = new List<ClientReleaseVersionObservation>(requested.Length);
        foreach (var identity in requested)
        {
            var component = components.SingleOrDefault(item =>
                item.ComponentKind == identity.ComponentKind
                && string.Equals(item.ComponentKey, identity.ComponentKey, StringComparison.Ordinal)
                && string.Equals(item.Channel, identity.Channel, StringComparison.Ordinal)
                && string.Equals(item.TargetRuntime, identity.TargetRuntime, StringComparison.Ordinal));
            var version = component?.Versions.SingleOrDefault(item =>
                string.Equals(item.Version, identity.Version, StringComparison.Ordinal));
            if (component is null || version is null)
            {
                continue;
            }

            var artifacts = version.Artifacts
                .OrderBy(artifact => artifact.ArtifactKind)
                .ThenBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(artifact => new ClientReleaseArtifactObservation(
                    artifact.ArtifactKind,
                    artifact.RelativePath,
                    artifact.Sha256,
                    artifact.Size))
                .ToList();

            observations.Add(new ClientReleaseVersionObservation(
                component.Id,
                component.ComponentKind,
                component.ComponentKey,
                component.DisplayName,
                component.Description,
                component.IconKind,
                component.AccentColor,
                component.Channel,
                component.TargetRuntime,
                version.Id,
                version.Version,
                version.HostApiVersion,
                version.MinHostVersion,
                version.MaxHostVersion,
                version.TargetFramework,
                version.DownloadUrl,
                version.Sha256,
                version.PackageSize,
                version.ReleaseNotes,
                version.DependenciesJson,
                version.Status,
                version.Signature,
                version.Publisher,
                version.PublishedAtUtc,
                version.DeletedAtUtc,
                version.DeletionReason,
                version.DeletionFailure,
                artifacts));
        }

        return observations;
    }
}
