using System.Text;

namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseOwnershipMarker
{
    private const int TokenLength = 32;

    public static string CreateToken() => Guid.NewGuid().ToString("N");

    public static void Write(string path, string token)
    {
        var bytes = Encoding.ASCII.GetBytes(token);
        if (bytes.Length != TokenLength)
        {
            throw new InvalidOperationException("Client release ownership token length is invalid.");
        }

        using var marker = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough);
        marker.Write(bytes);
        marker.Flush(flushToDisk: true);
    }

    public static bool Matches(string path, string token)
    {
        if (!File.Exists(path)
            || (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            return false;
        }

        using var marker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        if (marker.Length != TokenLength)
        {
            return false;
        }

        Span<byte> actual = stackalloc byte[TokenLength];
        marker.ReadExactly(actual);
        Span<byte> expected = stackalloc byte[TokenLength];
        return Encoding.ASCII.TryGetBytes(token, expected, out var written)
               && written == expected.Length
               && actual.SequenceEqual(expected);
    }
}
