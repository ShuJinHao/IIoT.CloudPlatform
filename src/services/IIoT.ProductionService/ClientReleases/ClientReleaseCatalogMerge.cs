using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseCatalogMerge
{
    public static IReadOnlyList<ClientHostRelease> MergeHostReleases(
        IEnumerable<ClientHostRelease> databaseReleases,
        IEnumerable<ClientHostRelease> artifactReleases,
        bool onlyPublished,
        bool includeArchived = false)
    {
        var databaseList = databaseReleases.ToList();
        var databaseKeys = databaseList
            .Select(HostKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleDatabaseReleases = databaseList.Where(release => IsVisible(release.Status, onlyPublished, includeArchived));
        var visibleArtifactReleases = artifactReleases.Where(release => !databaseKeys.Contains(HostKey(release)));

        return visibleDatabaseReleases
            .Concat(visibleArtifactReleases)
            .ToList();
    }

    public static IReadOnlyList<ClientPluginRelease> MergePluginReleases(
        IEnumerable<ClientPluginRelease> databaseReleases,
        IEnumerable<ClientPluginRelease> artifactReleases,
        bool onlyPublished,
        bool includeArchived = false)
    {
        var databaseList = databaseReleases.ToList();
        var databaseKeys = databaseList
            .Select(PluginKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visibleDatabaseReleases = databaseList.Where(release => IsVisible(release.Status, onlyPublished, includeArchived));
        var visibleArtifactReleases = artifactReleases.Where(release => !databaseKeys.Contains(PluginKey(release)));

        return visibleDatabaseReleases
            .Concat(visibleArtifactReleases)
            .ToList();
    }

    private static bool IsVisible(
        ClientReleaseStatus status,
        bool onlyPublished,
        bool includeArchived)
    {
        if (!includeArchived && status == ClientReleaseStatus.Archived)
        {
            return false;
        }

        return !onlyPublished
            || status is ClientReleaseStatus.Published or ClientReleaseStatus.Deprecated;
    }

    private static string HostKey(ClientHostRelease release)
        => $"{release.Channel}\u001f{release.Version}\u001f{release.TargetRuntime}";

    private static string PluginKey(ClientPluginRelease release)
        => $"{release.ModuleId}\u001f{release.Channel}\u001f{release.Version}\u001f{release.TargetRuntime}";
}
