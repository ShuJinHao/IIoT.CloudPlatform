namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseVelopackPaths
{
    public static IReadOnlyList<string> StableManifestNames { get; } = Array.AsReadOnly(
        ["releases.stable.json", "assets.stable.json"]);

    public static bool IsChannelManifest(string relativePath)
        => StableManifestNames.Contains(relativePath, StringComparer.OrdinalIgnoreCase)
           || relativePath.Equals("RELEASES", StringComparison.OrdinalIgnoreCase)
           || relativePath.StartsWith("RELEASES-", StringComparison.OrdinalIgnoreCase);

    public static bool IsProtectedChannelManifest(string fileName)
        => fileName.StartsWith("releases.", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("assets.", StringComparison.OrdinalIgnoreCase)
           || fileName.Equals("RELEASES", StringComparison.OrdinalIgnoreCase);

    public static bool IsReferencedByManifests(
        IEnumerable<string> manifestPaths,
        string fileName)
    {
        foreach (var manifestPath in manifestPaths)
        {
            if (File.Exists(manifestPath)
                && File.ReadAllText(manifestPath).Contains(
                    fileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
