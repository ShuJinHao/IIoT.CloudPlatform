using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.Core.Production.Contracts.ClientReleases;

public sealed record ClientReleaseVersionIdentity(
    ClientReleaseComponentKind ComponentKind,
    string ComponentKey,
    string Channel,
    string TargetRuntime,
    string Version);

public sealed record ClientReleaseArtifactObservation(
    ClientReleaseArtifactKind ArtifactKind,
    string RelativePath,
    string? Sha256,
    long? Size);

public sealed record ClientReleaseVersionObservation(
    Guid ComponentId,
    ClientReleaseComponentKind ComponentKind,
    string ComponentKey,
    string DisplayName,
    string? Description,
    string? IconKind,
    string? AccentColor,
    string Channel,
    string TargetRuntime,
    Guid VersionId,
    string Version,
    string HostApiVersion,
    string? MinHostVersion,
    string? MaxHostVersion,
    string? TargetFramework,
    string DownloadUrl,
    string Sha256,
    long PackageSize,
    string? ReleaseNotes,
    string DependenciesJson,
    ClientReleaseStatus Status,
    string? Signature,
    string? Publisher,
    DateTime? PublishedAtUtc,
    DateTime? DeletedAtUtc,
    string? DeletionReason,
    string? DeletionFailure,
    IReadOnlyList<ClientReleaseArtifactObservation> Artifacts);

/// <summary>
/// Reads an expected release-version set through one newly-created persistence context and one
/// database snapshot. A missing item means only that the version was not observed by this read;
/// it is not proof that no commit occurred.
/// </summary>
public interface IClientReleaseVersionObservationReader
{
    Task<IReadOnlyList<ClientReleaseVersionObservation>> ObserveAsync(
        IReadOnlyCollection<ClientReleaseVersionIdentity> identities,
        CancellationToken cancellationToken);
}
