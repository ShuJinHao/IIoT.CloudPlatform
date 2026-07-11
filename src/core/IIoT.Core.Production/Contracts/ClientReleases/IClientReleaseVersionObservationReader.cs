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
/// Reads a release version through a newly-created persistence context. A null result means only
/// that the requested version was not observed by this read; it is not proof that no commit occurred.
/// </summary>
public interface IClientReleaseVersionObservationReader
{
    Task<ClientReleaseVersionObservation?> ObserveAsync(
        ClientReleaseVersionIdentity identity,
        CancellationToken cancellationToken);
}
