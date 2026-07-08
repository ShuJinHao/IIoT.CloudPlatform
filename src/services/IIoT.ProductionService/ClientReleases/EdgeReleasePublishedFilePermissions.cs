namespace IIoT.ProductionService.ClientReleases;

internal static class EdgeReleasePublishedFilePermissions
{
    private const UnixFileMode GatewayDirectoryMode =
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead
        | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead
        | UnixFileMode.OtherExecute;

    private const UnixFileMode GatewayFileMode =
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.GroupRead
        | UnixFileMode.OtherRead;

    public static void EnsureGatewayReadable(
        string edgeRoot,
        IEnumerable<string> publishedDirectories,
        IEnumerable<string> publishedFiles)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var normalizedRoot = Path.GetFullPath(edgeRoot);
        SetDirectoryMode(normalizedRoot);

        foreach (var directory in publishedDirectories.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            EnsureDirectoryTreeReadable(normalizedRoot, directory);
        }

        foreach (var file in publishedFiles.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            EnsureFileReadable(normalizedRoot, file);
        }
    }

    private static void EnsureDirectoryTreeReadable(string edgeRoot, string directory)
    {
        var normalizedDirectory = Path.GetFullPath(directory);
        EnsureChildOrSelf(edgeRoot, normalizedDirectory);
        EnsureAncestorDirectoriesReadable(edgeRoot, normalizedDirectory);

        if (!Directory.Exists(normalizedDirectory))
        {
            return;
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(
                     normalizedDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            SetDirectoryMode(childDirectory);
        }

        foreach (var file in Directory.EnumerateFiles(
                     normalizedDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            SetFileMode(file);
        }
    }

    private static void EnsureFileReadable(string edgeRoot, string file)
    {
        var normalizedFile = Path.GetFullPath(file);
        EnsureChildOrSelf(edgeRoot, normalizedFile);

        var parent = Path.GetDirectoryName(normalizedFile);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            EnsureAncestorDirectoriesReadable(edgeRoot, parent);
        }

        if (File.Exists(normalizedFile))
        {
            SetFileMode(normalizedFile);
        }
    }

    private static void EnsureAncestorDirectoriesReadable(string edgeRoot, string path)
    {
        var stack = new Stack<string>();
        var current = Path.GetFullPath(path);
        while (!string.Equals(current, edgeRoot, StringComparison.Ordinal))
        {
            stack.Push(current);
            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent))
            {
                break;
            }

            current = Path.GetFullPath(parent);
        }

        stack.Push(edgeRoot);
        while (stack.Count > 0)
        {
            var directory = stack.Pop();
            if (Directory.Exists(directory))
            {
                SetDirectoryMode(directory);
            }
        }
    }

    private static void EnsureChildOrSelf(string edgeRoot, string path)
    {
        var normalizedRoot = edgeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(normalizedRoot, normalizedPath, StringComparison.Ordinal))
        {
            return;
        }

        if (!normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Edge 发布文件权限收口只能作用于受控 edge-updates 目录。");
        }
    }

#pragma warning disable CA1416
    private static void SetDirectoryMode(string directory)
        => File.SetUnixFileMode(directory, GatewayDirectoryMode);

    private static void SetFileMode(string file)
        => File.SetUnixFileMode(file, GatewayFileMode);
#pragma warning restore CA1416
}
