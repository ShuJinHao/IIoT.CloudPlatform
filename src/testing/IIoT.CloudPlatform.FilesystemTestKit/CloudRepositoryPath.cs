namespace IIoT.CloudPlatform.FilesystemTestKit;

public static class CloudRepositoryPath
{
    public static string Find(params string[] relativeSegments)
    {
        ArgumentNullException.ThrowIfNull(relativeSegments);
        if (relativeSegments.Length == 0 || relativeSegments.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-blank repository path segment is required.", nameof(relativeSegments));

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var firstSegment = Path.Combine(current.FullName, relativeSegments[0]);
            if (Directory.Exists(firstSegment) || File.Exists(firstSegment))
                return Path.Combine(current.FullName, Path.Combine(relativeSegments));

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Cloud repository root.");
    }
}
