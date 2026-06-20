namespace IIoT.ProductionService.ClientReleases;

public sealed class EdgeReleaseUploadOptions
{
    public const string SectionName = "EdgeReleaseUpload";
    public const long DefaultMaxBundleBytes = 2L * 1024 * 1024 * 1024;

    public int MaxUploadMbps { get; set; } = 100;

    public long MaxBundleBytes { get; set; } = DefaultMaxBundleBytes;

    public string StagingDirectoryName { get; set; } = ".staging";

    public void Validate()
    {
        if (MaxUploadMbps is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(MaxUploadMbps)} 必须在 1 到 1000 Mbps 之间。");
        }

        if (MaxBundleBytes is < 10 * 1024 * 1024 or > DefaultMaxBundleBytes)
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(MaxBundleBytes)} 必须在 10 MB 到 2 GB 之间。");
        }

        if (string.IsNullOrWhiteSpace(StagingDirectoryName)
            || StagingDirectoryName.Contains('/')
            || StagingDirectoryName.Contains('\\')
            || StagingDirectoryName.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{SectionName}:{nameof(StagingDirectoryName)} 必须是安全的单级目录名。");
        }
    }
}
