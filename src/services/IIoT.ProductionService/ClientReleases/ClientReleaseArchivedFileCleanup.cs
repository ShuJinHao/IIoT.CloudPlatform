using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseArchivedFileCleanup
{
    public static void DeletePluginDirectories(
        string edgeRoot,
        IEnumerable<ClientReleaseComponent> components)
    {
        foreach (var component in components.Where(
                     component => component.ComponentKind == ClientReleaseComponentKind.Plugin))
        {
            foreach (var release in component.Versions.Where(
                         release => release.Status == ClientReleaseStatus.Archived))
            {
                var directory = Path.Combine(
                    edgeRoot,
                    "plugins",
                    component.Channel,
                    ClientReleaseArtifactBuilder.EscapePathSegment(component.ComponentKey),
                    release.Version);
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }
}
