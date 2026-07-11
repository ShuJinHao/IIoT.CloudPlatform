using System.Security.Cryptography;
using System.Text;

namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseFileFacts
{
    public static ClientReleaseFileFact GetFileFact(string path)
    {
        if (!File.Exists(path)
            || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidDataException("Client release artifact is not a regular file.");
        }

        return new ClientReleaseFileFact(ComputeSha256(path), new FileInfo(path).Length);
    }

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

    public static string ComputeDirectorySha256(string directory)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(directory, file).Replace('\\', '/');
            hasher.AppendData(Encoding.UTF8.GetBytes(relativePath));
            hasher.AppendData([0]);
            using var stream = File.OpenRead(file);
            stream.CopyTo(new HashAppendStream(hasher));
            hasher.AppendData([10]);
        }

        return Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    public static long GetDirectorySize(string directory)
        => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);

    private sealed class HashAppendStream(IncrementalHash hasher) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => hasher.AppendData(buffer.AsSpan(offset, count));
    }
}

internal sealed record ClientReleaseFileFact(string Sha256, long Size);
