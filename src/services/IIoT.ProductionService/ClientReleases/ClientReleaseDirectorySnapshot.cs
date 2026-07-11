namespace IIoT.ProductionService.ClientReleases;

internal sealed record ClientReleaseDirectoryFileFact(
    string RelativePath,
    string Sha256,
    long Size);

internal sealed record ClientReleaseDirectorySnapshot(
    IReadOnlyList<string> Directories,
    IReadOnlyList<ClientReleaseDirectoryFileFact> Files)
{
    public static ClientReleaseDirectorySnapshot Capture(
        string root,
        string? ignoredRelativeFile = null)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot)
            || (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Client release directory is unavailable or is a reparse point.");
        }

        var directories = new List<string>();
        var files = new List<ClientReleaseDirectoryFileFact>();
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(fullRoot);
        while (pendingDirectories.TryPop(out var currentDirectory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         currentDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(fullRoot, entry));
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Client release directory contains a reparse point.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Add(relativePath);
                    pendingDirectories.Push(entry);
                    continue;
                }

                if (string.Equals(relativePath, ignoredRelativeFile, StringComparison.Ordinal))
                {
                    continue;
                }

                var fact = ClientReleaseFileFacts.GetFileFact(entry);
                files.Add(new ClientReleaseDirectoryFileFact(
                    relativePath,
                    fact.Sha256,
                    fact.Size));
            }
        }

        return new ClientReleaseDirectorySnapshot(
            directories.Order(StringComparer.Ordinal).ToArray(),
            files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray());
    }

    public bool Matches(string root, string? ignoredRelativeFile = null)
    {
        var observed = Capture(root, ignoredRelativeFile);
        return Directories.SequenceEqual(observed.Directories, StringComparer.Ordinal)
               && Files.SequenceEqual(observed.Files);
    }

    private static string NormalizeRelativePath(string value)
    {
        var normalized = value.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized)
            || Path.IsPathRooted(value)
            || normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException("Client release directory contains an invalid path.");
        }

        return normalized;
    }
}
