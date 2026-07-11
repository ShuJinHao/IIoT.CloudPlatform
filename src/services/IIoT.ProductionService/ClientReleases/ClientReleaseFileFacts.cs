using System.Security.Cryptography;

namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseFileFacts
{
    public static bool IsStrictChildPath(string parentPath, string childPath)
    {
        var relativePath = Path.GetRelativePath(
            Path.GetFullPath(parentPath),
            Path.GetFullPath(childPath));
        return !string.Equals(relativePath, ".", StringComparison.Ordinal)
               && !Path.IsPathRooted(relativePath)
               && !string.Equals(relativePath, "..", StringComparison.Ordinal)
               && !relativePath.StartsWith(
                   $"..{Path.DirectorySeparatorChar}",
                   StringComparison.Ordinal)
               && !relativePath.StartsWith(
                   $"..{Path.AltDirectorySeparatorChar}",
                   StringComparison.Ordinal);
    }

    public static bool IsExactRegularFile(
        string path,
        string expectedSha256,
        long expectedSize)
    {
        return File.Exists(path)
               && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0
               && new FileInfo(path).Length == expectedSize
               && string.Equals(
                   ComputeSha256(path),
                   expectedSha256,
                   StringComparison.OrdinalIgnoreCase);
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
