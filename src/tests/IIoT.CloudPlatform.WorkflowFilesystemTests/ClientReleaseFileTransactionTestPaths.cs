namespace IIoT.CloudPlatform.WorkflowTests;

internal sealed class ExternalDirectorySymlink : IDisposable
{
    private ExternalDirectorySymlink(string linkPath, string outsideRoot)
    {
        LinkPath = linkPath;
        OutsideRoot = outsideRoot;
    }

    public string LinkPath { get; }

    public string OutsideRoot { get; }

    public static bool IsSupported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    public static ExternalDirectorySymlink Create(string linkPath, string label)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        var outsideRoot = Path.Combine(
            Path.GetTempPath(),
            $"iiot-{label}-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideRoot);
        Directory.CreateSymbolicLink(linkPath, outsideRoot);
        return new ExternalDirectorySymlink(linkPath, outsideRoot);
    }

    public void Dispose()
    {
        try
        {
            if (new DirectoryInfo(LinkPath).LinkTarget is not null)
            {
                Directory.Delete(LinkPath);
            }
        }
        catch (DirectoryNotFoundException)
        {
        }

        if (Directory.Exists(OutsideRoot))
        {
            Directory.Delete(OutsideRoot, recursive: true);
        }
    }
}
