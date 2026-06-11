namespace IIoT.ProductionService.ClientReleases;

public sealed class EdgeInstallerArtifactOptions
{
    public const string SectionName = "EdgeInstallerArtifacts";

    public string RootPath { get; set; } = "edge-updates/installers";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RootPath))
        {
            throw new InvalidOperationException($"{SectionName}:RootPath must be configured.");
        }
    }
}
