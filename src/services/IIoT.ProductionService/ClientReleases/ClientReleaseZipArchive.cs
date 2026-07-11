using System.IO.Compression;

namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseZipArchive
{
    public static void ExtractToDirectory(
        string zipPath,
        string extractRoot,
        string packageLabel)
    {
        Directory.CreateDirectory(extractRoot);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var relativePath = NormalizeEntryPath(entry.FullName, packageLabel);
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(extractRoot, relativePath));
            if (!ClientReleaseFileFacts.IsStrictChildPath(extractRoot, targetPath))
            {
                throw new ClientReleaseValidationException($"{packageLabel} 包含非法路径。");
            }

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: false);
        }

        foreach (var fileSystemInfo in Directory.EnumerateFileSystemEntries(
                     extractRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(fileSystemInfo) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ClientReleaseValidationException(
                    $"{packageLabel} 不允许包含符号链接或重解析点。");
            }
        }
    }

    public static string NormalizeEntryPath(string path, string packageLabel)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains(':', StringComparison.Ordinal)
            || normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new ClientReleaseValidationException($"{packageLabel} 包含非法 zip 路径。");
        }

        return normalized;
    }
}
