using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.ClientReleases;

public sealed class EfClientReleaseVersionObservationReader(
    DbContextOptions<IIoTDbContext> options)
    : IClientReleaseVersionObservationReader
{
    public async Task<ClientReleaseVersionObservation?> ObserveAsync(
        ClientReleaseVersionIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var context = new IIoTDbContext(options);
        var component = await context.ClientReleaseComponents
            .AsNoTracking()
            .Where(component =>
                component.ComponentKind == identity.ComponentKind
                && component.ComponentKey == identity.ComponentKey
                && component.Channel == identity.Channel
                && component.TargetRuntime == identity.TargetRuntime)
            .Include(component => component.Versions.Where(version => version.Version == identity.Version))
            .ThenInclude(version => version.Artifacts)
            .AsSingleQuery()
            .SingleOrDefaultAsync(cancellationToken);
        var version = component?.Versions.SingleOrDefault();
        if (component is null || version is null)
        {
            return null;
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

        return new ClientReleaseVersionObservation(
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
            artifacts);
    }
}
