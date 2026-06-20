namespace IIoT.ProductionService.ClientReleases;

public sealed class EdgeReleaseRetentionOptions
{
    public const string SectionName = "EdgeRelease";

    public int MaxVersionsPerComponent { get; set; } = 3;

    public void Validate()
    {
        if (MaxVersionsPerComponent is < 1 or > 20)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(MaxVersionsPerComponent)} 必须在 1 到 20 之间。");
        }
    }
}
