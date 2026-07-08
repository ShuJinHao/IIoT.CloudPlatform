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

    public static void AssertPublishedPathsReady(
        string edgeRoot,
        IEnumerable<string> publishedDirectories,
        IEnumerable<string> publishedFiles)
    {
        var normalizedRoot = Path.GetFullPath(edgeRoot);
        AssertDirectoryReady(normalizedRoot, normalizedRoot);

        foreach (var directory in publishedDirectories.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            AssertDirectoryReady(normalizedRoot, directory);
        }

        foreach (var file in publishedFiles.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            AssertFileReady(normalizedRoot, file);
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

    private static void AssertDirectoryReady(string edgeRoot, string directory)
    {
        var normalizedDirectory = Path.GetFullPath(directory);
        EnsureChildOrSelf(edgeRoot, normalizedDirectory);

        if (!Directory.Exists(normalizedDirectory))
        {
            throw new InvalidDataException(
                $"Edge Published 发布目录缺失或不可分发: {FormatRelativePath(edgeRoot, normalizedDirectory)}");
        }

        var attributes = File.GetAttributes(normalizedDirectory);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Edge Published 发布目录不能是符号链接或重解析点: {FormatRelativePath(edgeRoot, normalizedDirectory)}");
        }

        if (RequiresUnixGatewayMode())
        {
            AssertDirectoryMode(edgeRoot, normalizedDirectory);
        }
    }

    private static void AssertFileReady(string edgeRoot, string file)
    {
        var normalizedFile = Path.GetFullPath(file);
        EnsureChildOrSelf(edgeRoot, normalizedFile);

        if (!File.Exists(normalizedFile))
        {
            throw new InvalidDataException(
                $"Edge Published 发布文件缺失或不可分发: {FormatRelativePath(edgeRoot, normalizedFile)}");
        }

        var attributes = File.GetAttributes(normalizedFile);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Edge Published 发布文件不能是符号链接或重解析点: {FormatRelativePath(edgeRoot, normalizedFile)}");
        }

        var parent = Path.GetDirectoryName(normalizedFile);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            AssertAncestorDirectoriesReady(edgeRoot, parent);
        }

        try
        {
            using var stream = new FileStream(
                normalizedFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            if (stream.Length <= 0)
            {
                throw new InvalidDataException(
                    $"Edge Published 发布文件为空，不允许置为 Published: {FormatRelativePath(edgeRoot, normalizedFile)}");
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"Edge Published 发布文件不可读，不允许置为 Published: {FormatRelativePath(edgeRoot, normalizedFile)}",
                ex);
        }

        if (RequiresUnixGatewayMode())
        {
            AssertFileMode(edgeRoot, normalizedFile);
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

    private static void AssertAncestorDirectoriesReady(string edgeRoot, string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.Equals(current, edgeRoot, StringComparison.Ordinal))
        {
            AssertDirectoryReady(edgeRoot, current);
            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent))
            {
                break;
            }

            current = Path.GetFullPath(parent);
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

    private static string FormatRelativePath(string edgeRoot, string path)
        => Path.GetRelativePath(edgeRoot, path).Replace('\\', '/');

    private static bool RequiresUnixGatewayMode()
        => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

#pragma warning disable CA1416
    private static void SetDirectoryMode(string directory)
        => File.SetUnixFileMode(directory, GatewayDirectoryMode);

    private static void SetFileMode(string file)
        => File.SetUnixFileMode(file, GatewayFileMode);

    private static void AssertDirectoryMode(string edgeRoot, string directory)
    {
        var mode = File.GetUnixFileMode(directory);
        if ((mode & UnixFileMode.OtherRead) == 0 || (mode & UnixFileMode.OtherExecute) == 0)
        {
            throw new InvalidDataException(
                $"Edge Published 发布目录缺少 nginx/static 可读执行权限: {FormatRelativePath(edgeRoot, directory)}");
        }
    }

    private static void AssertFileMode(string edgeRoot, string file)
    {
        var mode = File.GetUnixFileMode(file);
        if ((mode & UnixFileMode.OtherRead) == 0)
        {
            throw new InvalidDataException(
                $"Edge Published 发布文件缺少 nginx/static 可读权限: {FormatRelativePath(edgeRoot, file)}");
        }
    }
#pragma warning restore CA1416
}
