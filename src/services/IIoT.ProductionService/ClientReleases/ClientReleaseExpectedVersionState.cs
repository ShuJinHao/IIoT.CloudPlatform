using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;

namespace IIoT.ProductionService.ClientReleases;

internal sealed record ClientReleaseExpectedVersionState(
    ClientReleaseVersionIdentity Identity,
    string DisplayName,
    string? Description,
    string? IconKind,
    string? AccentColor,
    string HostApiVersion,
    string? MinHostVersion,
    string? MaxHostVersion,
    string? TargetFramework,
    string DownloadUrl,
    string Sha256,
    long PackageSize,
    string? ReleaseNotes,
    string DependenciesJson,
    string? Signature,
    string? Publisher,
    DateTime? PublishedAtUtc,
    IReadOnlyList<ClientReleaseArtifactObservation> Artifacts)
{
    public static ClientReleaseExpectedVersionState From(
        ClientReleaseComponent component,
        ClientReleaseVersion version)
    {
        return new ClientReleaseExpectedVersionState(
            new ClientReleaseVersionIdentity(
                component.ComponentKind,
                component.ComponentKey,
                component.Channel,
                component.TargetRuntime,
                version.Version),
            component.DisplayName,
            component.Description,
            component.IconKind,
            component.AccentColor,
            version.HostApiVersion,
            version.MinHostVersion,
            version.MaxHostVersion,
            version.TargetFramework,
            version.DownloadUrl,
            version.Sha256,
            version.PackageSize,
            version.ReleaseNotes,
            version.DependenciesJson,
            version.Signature,
            version.Publisher,
            version.PublishedAtUtc,
            version.Artifacts
                .Select(artifact => new ClientReleaseArtifactObservation(
                    artifact.ArtifactKind,
                    artifact.RelativePath,
                    artifact.Sha256,
                    artifact.Size))
                .ToArray());
    }
}

internal static class ClientReleaseExpectedVersionMatcher
{
    public static bool Matches(
        ClientReleaseExpectedVersionState expected,
        ClientReleaseVersionObservation observed)
    {
        if (observed.ComponentKind != expected.Identity.ComponentKind
            || !string.Equals(observed.ComponentKey, expected.Identity.ComponentKey, StringComparison.Ordinal)
            || !string.Equals(observed.Channel, expected.Identity.Channel, StringComparison.Ordinal)
            || !string.Equals(observed.TargetRuntime, expected.Identity.TargetRuntime, StringComparison.Ordinal)
            || !string.Equals(observed.Version, expected.Identity.Version, StringComparison.Ordinal)
            || !string.Equals(observed.DisplayName, expected.DisplayName, StringComparison.Ordinal)
            || !string.Equals(observed.Description, expected.Description, StringComparison.Ordinal)
            || !string.Equals(observed.IconKind, expected.IconKind, StringComparison.Ordinal)
            || !string.Equals(observed.AccentColor, expected.AccentColor, StringComparison.Ordinal)
            || !string.Equals(observed.HostApiVersion, expected.HostApiVersion, StringComparison.Ordinal)
            || !string.Equals(observed.MinHostVersion, expected.MinHostVersion, StringComparison.Ordinal)
            || !string.Equals(observed.MaxHostVersion, expected.MaxHostVersion, StringComparison.Ordinal)
            || !string.Equals(observed.TargetFramework, expected.TargetFramework, StringComparison.Ordinal)
            || !string.Equals(observed.DownloadUrl, expected.DownloadUrl, StringComparison.Ordinal)
            || !string.Equals(observed.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase)
            || observed.PackageSize != expected.PackageSize
            || !string.Equals(observed.ReleaseNotes, expected.ReleaseNotes, StringComparison.Ordinal)
            || !string.Equals(observed.DependenciesJson, expected.DependenciesJson, StringComparison.Ordinal)
            || observed.Status != ClientReleaseStatus.Published
            || !string.Equals(observed.Signature, expected.Signature, StringComparison.Ordinal)
            || !string.Equals(observed.Publisher, expected.Publisher, StringComparison.Ordinal)
            || observed.DeletedAtUtc is not null
            || observed.DeletionReason is not null
            || observed.DeletionFailure is not null
            || !PublishedAtMatches(expected.PublishedAtUtc, observed.PublishedAtUtc))
        {
            return false;
        }

        var expectedArtifacts = expected.Artifacts
            .OrderBy(artifact => artifact.ArtifactKind)
            .ThenBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var observedArtifacts = observed.Artifacts
            .OrderBy(artifact => artifact.ArtifactKind)
            .ThenBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();
        return expectedArtifacts.Length == observedArtifacts.Length
               && expectedArtifacts.Zip(observedArtifacts).All(pair =>
                   pair.First.ArtifactKind == pair.Second.ArtifactKind
                   && string.Equals(pair.First.RelativePath, pair.Second.RelativePath, StringComparison.Ordinal)
                   && string.Equals(pair.First.Sha256, pair.Second.Sha256, StringComparison.OrdinalIgnoreCase)
                   && pair.First.Size == pair.Second.Size);
    }

    private static bool PublishedAtMatches(DateTime? expected, DateTime? observed)
    {
        if (observed is null || observed.Value.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        if (expected is null)
        {
            return true;
        }

        return expected.Value.Kind == DateTimeKind.Utc
               && TruncateToPostgresMicrosecond(expected.Value)
               == TruncateToPostgresMicrosecond(observed.Value);
    }

    private static DateTime TruncateToPostgresMicrosecond(DateTime value)
        => new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);
}
