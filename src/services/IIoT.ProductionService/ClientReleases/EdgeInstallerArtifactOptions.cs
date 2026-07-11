namespace IIoT.ProductionService.ClientReleases;

public sealed class EdgeInstallerArtifactOptions
{
    public const string SectionName = "EdgeInstallerArtifacts";

    public string RootPath { get; set; } = "edge-updates/installers";

    public string? VelopackReleasesBaseUrl { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RootPath))
        {
            throw new InvalidOperationException($"{SectionName}:RootPath must be configured.");
        }
    }

    public string? BuildVelopackUpdateSource(string channel)
    {
        if (string.IsNullOrWhiteSpace(VelopackReleasesBaseUrl))
        {
            return null;
        }

        return $"{VelopackReleasesBaseUrl.TrimEnd('/')}/{channel}/";
    }

    public string ResolveEdgeUpdatesRoot()
    {
        var installerRoot = Path.GetFullPath(RootPath);
        var parent = Directory.GetParent(installerRoot);
        if (parent is null)
        {
            throw new InvalidOperationException(
                $"{SectionName}:RootPath 必须位于 edge-updates/installers 下。");
        }

        return parent.FullName;
    }
}
