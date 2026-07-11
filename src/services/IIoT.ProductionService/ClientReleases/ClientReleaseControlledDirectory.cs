using IIoT.Services.Contracts;

namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseControlledDirectory
{
    public static void ValidateChain(
        string controlledRoot,
        string directory,
        string validationMessage,
        bool requireStrictChild = false)
    {
        var fullRoot = Path.GetFullPath(controlledRoot);
        var fullDirectory = Path.GetFullPath(directory);
        var isRoot = string.Equals(fullDirectory, fullRoot, StringComparison.Ordinal);
        if ((requireStrictChild && isRoot)
            || (!isRoot && !ClientReleaseFileFacts.IsStrictChildPath(fullRoot, fullDirectory)))
        {
            throw new ClientReleaseValidationException(validationMessage);
        }

        var rootAttributes = TryGetAttributes(fullRoot);
        if (rootAttributes is null
            || (rootAttributes.Value & FileAttributes.Directory) == 0
            || (rootAttributes.Value & FileAttributes.ReparsePoint) != 0)
        {
            throw new ClientReleaseValidationException(validationMessage);
        }

        var current = fullDirectory;
        while (true)
        {
            var attributes = TryGetAttributes(current);
            if (attributes is not null
                && ((attributes.Value & FileAttributes.Directory) == 0
                    || (attributes.Value & FileAttributes.ReparsePoint) != 0))
            {
                throw new ClientReleaseValidationException(validationMessage);
            }

            if (string.Equals(current, fullRoot, StringComparison.Ordinal))
            {
                return;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new ClientReleaseValidationException(validationMessage);
        }
    }

    public static void EnsureExists(
        string controlledRoot,
        string directory,
        ICollection<string>? createdDirectories,
        string validationMessage)
    {
        var fullRoot = Path.GetFullPath(controlledRoot);
        var fullDirectory = Path.GetFullPath(directory);
        ValidateChain(fullRoot, fullDirectory, validationMessage);
        if (Directory.Exists(fullDirectory))
        {
            return;
        }

        var pending = new Stack<string>();
        var current = fullDirectory;
        while (!string.Equals(current, fullRoot, StringComparison.Ordinal)
               && !Directory.Exists(current))
        {
            pending.Push(current);
            current = Path.GetDirectoryName(current)
                ?? throw new ClientReleaseValidationException(validationMessage);
        }

        while (pending.TryPop(out var item))
        {
            ValidateChain(fullRoot, Path.GetDirectoryName(item)!, validationMessage);
            Directory.CreateDirectory(item);
            ValidateChain(fullRoot, item, validationMessage);
            createdDirectories?.Add(item);
        }
    }

    public static bool IsExistingDirectory(string controlledRoot, string directory)
    {
        try
        {
            ValidateChain(controlledRoot, directory, "Client release 发布目录非法。");
            var attributes = TryGetAttributes(Path.GetFullPath(directory));
            return attributes is not null
                   && (attributes.Value & FileAttributes.Directory) != 0
                   && (attributes.Value & FileAttributes.ReparsePoint) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static FileAttributes? TryGetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }
}
