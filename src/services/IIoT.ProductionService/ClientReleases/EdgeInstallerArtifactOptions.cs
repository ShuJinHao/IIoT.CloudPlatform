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
}
