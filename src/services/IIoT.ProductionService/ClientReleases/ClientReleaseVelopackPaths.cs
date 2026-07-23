using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace IIoT.ProductionService.ClientReleases;

internal static class ClientReleaseVelopackPaths
{
    private static readonly HashSet<string> KnownManifestFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "RELEASES"
    };

    public static IReadOnlyList<string> StableManifestNames { get; } = Array.AsReadOnly(
        ["releases.stable.json", "assets.stable.json"]);

    public static bool IsChannelManifest(string fileName)
        => KnownManifestFileNames.Contains(fileName)
           || IsStableJsonManifest(fileName);

    public static bool IsProtectedChannelManifest(string fileName)
        => IsChannelManifest(fileName);

    public static bool IsReferencedByManifests(
        IEnumerable<string> manifestPaths,
        string fileName)
    {
        foreach (var manifestPath in manifestPaths)
        {
            if (File.Exists(manifestPath)
                && File.ReadAllText(manifestPath).Contains(
                    fileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 以存活 Host 版本的 VelopackFile artifact 为白名单重建 channel manifests。
    /// manifest 中只允许保留白名单内且文件仍存在的 .nupkg 引用；其它内容原样保留。
    /// manifest 不是合法 JSON 对象时 fail closed（返回 false），绝不猜测内容重写。
    /// manifest 缺失时按白名单真正生成（存活 Host 仍需要更新清单），任一 manifest 无法重建返回 false。
    /// </summary>
    public static bool RebuildChannelManifestsFromWhitelist(
        string velopackChannelRoot,
        IReadOnlyCollection<string> survivingNupkgFileNames,
        ILogger logger)
    {
        var whitelist = new HashSet<string>(survivingNupkgFileNames, StringComparer.OrdinalIgnoreCase);
        var succeeded = true;
        foreach (var manifestName in StableManifestNames)
        {
            var manifestPath = Path.Combine(velopackChannelRoot, manifestName);
            if (!File.Exists(manifestPath))
            {
                // 真正重建缺失的 manifest：按白名单生成最小合法清单，不再静默跳过。
                if (!TryWriteMissingStableManifest(manifestPath, whitelist, logger))
                {
                    succeeded = false;
                }

                continue;
            }

            if (!TryRebuildStableManifest(manifestPath, whitelist, logger))
            {
                succeeded = false;
            }
        }

        return succeeded && TryRebuildReleasesFile(velopackChannelRoot, whitelist, logger);
    }

    /// <summary>
    /// 缺失的 stable JSON manifest 按白名单真正生成；写失败返回 false。
    /// </summary>
    private static bool TryWriteMissingStableManifest(
        string manifestPath,
        ISet<string> whitelist,
        ILogger logger)
    {
        var array = new JsonArray();
        foreach (var fileName in whitelist.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(Path.Combine(Path.GetDirectoryName(manifestPath)!, fileName)))
            {
                array.Add(fileName);
            }
        }

        var propertyName = Path.GetFileName(manifestPath)
            .StartsWith("assets.", StringComparison.OrdinalIgnoreCase)
            ? "assets"
            : "packages";
        var root = new JsonObject
        {
            [propertyName] = array
        };
        return TryAtomicReplaceJson(manifestPath, root, logger);
    }

    private static bool TryRebuildStableManifest(
        string manifestPath,
        ISet<string> whitelist,
        ILogger logger)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }

        if (root is null)
        {
            LogManifestRebuildFailure(logger, manifestPath, "invalid-json");
            return false;
        }

        var removed = false;
        foreach (var property in root.ToArray())
        {
            if (property.Value is not JsonArray entries)
            {
                continue;
            }

            for (var index = entries.Count - 1; index >= 0; index--)
            {
                var fileName = ResolvePackageFileName(entries[index]);
                if (fileName is null || !IsNugetPackage(fileName))
                {
                    continue;
                }

                if (!whitelist.Contains(fileName)
                    || !File.Exists(Path.Combine(Path.GetDirectoryName(manifestPath)!, fileName)))
                {
                    entries.RemoveAt(index);
                    removed = true;
                }
            }
        }

        if (!removed)
        {
            return true;
        }

        return TryAtomicReplaceJson(manifestPath, root, logger);
    }

    private static bool TryRebuildReleasesFile(
        string velopackChannelRoot,
        ISet<string> whitelist,
        ILogger logger)
    {
        var releasesPath = Path.Combine(velopackChannelRoot, "RELEASES");
        if (!File.Exists(releasesPath))
        {
            // RELEASES 缺失时按白名单真正生成（存活 Host 仍需要更新清单）。
            var lines = whitelist
                .Where(fileName => File.Exists(Path.Combine(velopackChannelRoot, fileName)))
                .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
                .Select(fileName => $"{ComputeSha256OrEmpty(Path.Combine(velopackChannelRoot, fileName))} {fileName} {new FileInfo(Path.Combine(velopackChannelRoot, fileName)).Length}")
                .ToArray();
            return TryWriteLinesAtomic(releasesPath, lines, logger);
        }

        var existing = File.ReadAllLines(releasesPath);
        var kept = new List<string>(existing.Length);
        var removed = false;
        foreach (var line in existing)
        {
            var segments = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var fileName = segments.FirstOrDefault(IsNugetPackage);
            if (fileName is not null
                && (!whitelist.Contains(fileName)
                    || !File.Exists(Path.Combine(velopackChannelRoot, fileName))))
            {
                removed = true;
                continue;
            }

            kept.Add(line);
        }

        if (!removed)
        {
            return true;
        }

        return TryWriteLinesAtomic(releasesPath, [.. kept], logger);
    }

    private static bool TryWriteLinesAtomic(string releasesPath, string[] lines, ILogger logger)
    {
        var privatePath = CreatePrivateSiblingPath(releasesPath, "rebuild");
        try
        {
            File.WriteAllLines(privatePath, lines);
            File.Move(privatePath, releasesPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                new EventId(4612, "ClientReleaseVelopackManifestRebuildFailure"),
                "velopack-manifest-rebuild",
                ex,
                "RELEASES");
            TryDeletePrivateFile(privatePath);
            return false;
        }
    }

    private static string ComputeSha256OrEmpty(string path)
    {
        try
        {
            return ClientReleaseFileFacts.ComputeSha256(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = ex;
            return string.Empty.PadRight(64, '0');
        }
    }

    private static string? ResolvePackageFileName(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return Path.GetFileName(text.Trim());
        }

        if (node is JsonObject entry)
        {
            foreach (var property in entry)
            {
                if (property.Value is JsonValue propertyValue
                    && propertyValue.TryGetValue<string>(out var propertyText)
                    && IsNugetPackage(propertyText.Trim()))
                {
                    return Path.GetFileName(propertyText.Trim());
                }
            }
        }

        return null;
    }

    private static bool TryAtomicReplaceJson(string manifestPath, JsonObject root, ILogger logger)
    {
        var privatePath = CreatePrivateSiblingPath(manifestPath, "rebuild");
        try
        {
            File.WriteAllText(privatePath, root.ToJsonString());
            File.Move(privatePath, manifestPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                new EventId(4612, "ClientReleaseVelopackManifestRebuildFailure"),
                "velopack-manifest-rebuild",
                ex,
                Path.GetFileName(manifestPath));
            TryDeletePrivateFile(privatePath);
            return false;
        }
    }

    private static void LogManifestRebuildFailure(ILogger logger, string manifestPath, string condition)
    {
        ClientReleasePublishDiagnostics.LogCondition(
            logger,
            new EventId(4612, "ClientReleaseVelopackManifestRebuildFailure"),
            "velopack-manifest-rebuild",
            condition,
            Path.GetFileName(manifestPath));
    }

    private static bool IsStableJsonManifest(string fileName)
        => fileName.StartsWith("releases.", StringComparison.OrdinalIgnoreCase)
           && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
           || fileName.StartsWith("assets.", StringComparison.OrdinalIgnoreCase)
              && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    private static bool IsNugetPackage(string fileName)
        => fileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

    private static string CreatePrivateSiblingPath(string targetPath, string purpose)
        => Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".{Path.GetFileName(targetPath)}.{purpose}-{Guid.NewGuid():N}");

    private static void TryDeletePrivateFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
