using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IIoT.ProductionService.ClientReleases;

public static class EdgePayloadManifestContract
{
    public const string FileName = "payload-manifest.json";
    public const int SchemaVersion = 1;
    public const string Algorithm = "rsa-pss-sha256";
}

public sealed record EdgePayloadManifestFile(
    string Path,
    long Size,
    string Sha256,
    string Type,
    string Component,
    string Version);

public sealed record EdgePayloadManifestUnsigned(
    int SchemaVersion,
    Guid GenerationId,
    string Component,
    string Version,
    string CreatedAtUtc,
    IReadOnlyList<EdgePayloadManifestFile> Files);

public sealed record EdgePayloadManifestSignature(
    string Algorithm,
    string KeyId,
    string Value);

public sealed record EdgePayloadManifest(
    int SchemaVersion,
    Guid GenerationId,
    string Component,
    string Version,
    string CreatedAtUtc,
    IReadOnlyList<EdgePayloadManifestFile> Files,
    EdgePayloadManifestSignature Signature);

internal static class EdgePayloadManifestWriter
{
    private static readonly JsonSerializerOptions CanonicalJsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = false
        };

    public static void AppendSignedManifest(
        Stream payloadZip,
        Guid generationId,
        DateTime generatedAtUtc,
        string hostVersion,
        string launcherDirectory,
        string hostDirectory,
        string pluginsRoot,
        IReadOnlyDictionary<string, string> pluginVersionsByDirectory,
        EdgeInstallerArtifactOptions options)
    {
        if (!payloadZip.CanRead || !payloadZip.CanWrite || !payloadZip.CanSeek)
            throw new InvalidDataException("Payload zip must be seekable, readable and writable.");

        var files = BuildFileFacts(
            payloadZip,
            hostVersion,
            launcherDirectory,
            hostDirectory,
            pluginsRoot,
            pluginVersionsByDirectory);
        var unsigned = new EdgePayloadManifestUnsigned(
            EdgePayloadManifestContract.SchemaVersion,
            generationId,
            "edge-installer-payload",
            hostVersion,
            FormatUtc(generatedAtUtc),
            files);
        var canonicalBytes = JsonSerializer.SerializeToUtf8Bytes(
            unsigned,
            CanonicalJsonOptions);
        var signature = Sign(canonicalBytes, options);
        var manifest = new EdgePayloadManifest(
            unsigned.SchemaVersion,
            unsigned.GenerationId,
            unsigned.Component,
            unsigned.Version,
            unsigned.CreatedAtUtc,
            unsigned.Files,
            signature);

        payloadZip.Position = 0;
        using var archive = new ZipArchive(
            payloadZip,
            ZipArchiveMode.Update,
            leaveOpen: true);
        if (archive.Entries.Any(entry => string.Equals(
                NormalizePath(entry.FullName),
                EdgePayloadManifestContract.FileName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("Payload contains duplicate payload manifest.");
        }

        var entry = archive.CreateEntry(
            EdgePayloadManifestContract.FileName,
            CompressionLevel.NoCompression);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, manifest, CanonicalJsonOptions);
    }

    public static byte[] CanonicalizeForSignature(EdgePayloadManifest manifest)
        => JsonSerializer.SerializeToUtf8Bytes(
            new EdgePayloadManifestUnsigned(
                manifest.SchemaVersion,
                manifest.GenerationId,
                manifest.Component,
                manifest.Version,
                manifest.CreatedAtUtc,
                manifest.Files
                    .OrderBy(file => file.Path, StringComparer.Ordinal)
                    .ToArray()),
            CanonicalJsonOptions);

    private static IReadOnlyList<EdgePayloadManifestFile> BuildFileFacts(
        Stream payloadZip,
        string hostVersion,
        string launcherDirectory,
        string hostDirectory,
        string pluginsRoot,
        IReadOnlyDictionary<string, string> pluginVersionsByDirectory)
    {
        payloadZip.Position = 0;
        using var archive = new ZipArchive(
            payloadZip,
            ZipArchiveMode.Read,
            leaveOpen: true);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<EdgePayloadManifestFile>();
        foreach (var entry in archive.Entries)
        {
            var path = NormalizePath(entry.FullName);
            if (string.IsNullOrWhiteSpace(path)
                || entry.FullName.EndsWith("/", StringComparison.Ordinal))
                continue;
            if (!paths.Add(path))
                throw new InvalidDataException($"Payload contains duplicate path: {path}");
            if (string.Equals(
                    path,
                    EdgePayloadManifestContract.FileName,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Payload manifest path is reserved.");

            using var source = entry.Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long size = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                size += read;
            }

            var component = ResolveComponent(
                path,
                launcherDirectory,
                hostDirectory,
                pluginsRoot,
                pluginVersionsByDirectory,
                out var version,
                hostVersion);
            files.Add(new EdgePayloadManifestFile(
                path,
                size,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                ResolveType(path),
                component,
                version));
        }

        return files
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private static EdgePayloadManifestSignature Sign(
        byte[] canonicalBytes,
        EdgeInstallerArtifactOptions options)
    {
        var keyId = options.PayloadSigningKeyId?.Trim();
        if (string.IsNullOrWhiteSpace(keyId))
            throw new InvalidDataException("Payload signing key id is not configured.");

        string? pem = options.PayloadSigningPrivateKeyPem;
        if (string.IsNullOrWhiteSpace(pem)
            && !string.IsNullOrWhiteSpace(options.PayloadSigningPrivateKeyPemPath))
        {
            var path = Path.GetFullPath(options.PayloadSigningPrivateKeyPemPath);
            if (!File.Exists(path))
                throw new InvalidDataException("Payload signing private key file does not exist.");
            pem = File.ReadAllText(path);
        }
        if (string.IsNullOrWhiteSpace(pem))
            throw new InvalidDataException("Payload signing private key is not configured.");

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            if (rsa.KeySize < 2048)
                throw new InvalidDataException("Payload signing RSA key must be at least 2048 bits.");
            var value = Convert.ToBase64String(rsa.SignData(
                canonicalBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss));
            return new EdgePayloadManifestSignature(
                EdgePayloadManifestContract.Algorithm,
                keyId,
                value);
        }
        catch (Exception exception) when (
            exception is CryptographicException
                or ArgumentException
                or IOException
                or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                "Payload signing private key is invalid or unavailable.",
                exception);
        }
    }

    private static string ResolveComponent(
        string path,
        string launcherDirectory,
        string hostDirectory,
        string pluginsRoot,
        IReadOnlyDictionary<string, string> pluginVersionsByDirectory,
        out string version,
        string hostVersion)
    {
        var launcherPrefix = NormalizePath(launcherDirectory) + "/";
        if (path.StartsWith(launcherPrefix, StringComparison.OrdinalIgnoreCase))
        {
            version = hostVersion;
            return "launcher";
        }
        var hostPrefix = NormalizePath(hostDirectory) + "/";
        if (path.StartsWith(hostPrefix, StringComparison.OrdinalIgnoreCase))
        {
            version = hostVersion;
            return "host";
        }

        foreach (var plugin in pluginVersionsByDirectory.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            var prefix = NormalizePath(pluginsRoot) + "/"
                         + NormalizePath(plugin.Key) + "/";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            version = plugin.Value;
            return "plugin:" + NormalizePath(plugin.Key).Split('/')[0];
        }

        version = hostVersion;
        return "installer";
    }

    private static string ResolveType(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
            return "deps";
        if (fileName.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            return "runtimeconfig";
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".exe" => "executable",
            ".dll" => "managed-or-native-library",
            ".json" => "json",
            ".pdb" => "symbols",
            ".xml" => "xml",
            _ => "resource"
        };
    }

    private static string NormalizePath(string value)
    {
        var normalized = value.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.StartsWith("/", StringComparison.Ordinal)
            || normalized.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException("Payload contains an unsafe path.");
        }
        return normalized;
    }

    private static string FormatUtc(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();
        return new DateTime(
                utc.Ticks - utc.Ticks % 10,
                DateTimeKind.Utc)
            .ToString(
                "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                CultureInfo.InvariantCulture);
    }
}
