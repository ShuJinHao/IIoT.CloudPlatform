using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace IIoT.ProductionService.ClientReleases;

internal sealed record ClientReleaseVelopackArtifactFact(
    string FileName,
    string Sha256,
    long SizeBytes);

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
    /// 按存活 Host 已登记的真实 Velopack 文件事实过滤三份现有 channel manifest。
    /// releases.{channel}.json 必须是 VelopackAssetFeed 对象（Assets 数组），
    /// assets.{channel}.json 必须是 Velopack CLI 生成的顶层 asset 数组，RELEASES 保留原始行格式。
    /// 任何 manifest 缺失、结构漂移、存活文件缺失或事实不一致都 fail closed；本方法绝不从文件名
    /// 猜造 PackageId/Version/Type/RelativeFileName/hash 等生产发布元数据。
    /// </summary>
    public static bool RebuildChannelManifestsFromWhitelist(
        string controlledRoot,
        string velopackChannelRoot,
        IReadOnlyCollection<ClientReleaseVelopackArtifactFact> survivingArtifacts,
        ILogger logger)
    {
        ClientReleaseControlledDirectory.ValidateChain(
            controlledRoot,
            velopackChannelRoot,
            "velopack channel 目录祖先链包含符号链接或越过受控发布目录。");

        if (!TryNormalizeSurvivingArtifacts(
                controlledRoot,
                velopackChannelRoot,
                survivingArtifacts,
                out var artifactsByName))
        {
            LogManifestRebuildFailure(logger, "channel", "surviving-artifact-missing");
            return false;
        }

        var releasesPath = Path.Combine(velopackChannelRoot, "releases.stable.json");
        var assetsPath = Path.Combine(velopackChannelRoot, "assets.stable.json");
        var legacyReleasesPath = Path.Combine(velopackChannelRoot, "RELEASES");
        foreach (var manifestPath in new[] { releasesPath, assetsPath, legacyReleasesPath })
        {
            if (!File.Exists(manifestPath))
            {
                LogManifestRebuildFailure(logger, Path.GetFileName(manifestPath), "missing");
                return false;
            }

            ValidateMutableManifest(controlledRoot, manifestPath);
        }

        try
        {
            if (!TryFilterReleaseFeed(
                    File.ReadAllText(releasesPath),
                    artifactsByName,
                    out var releasesJson)
                || !TryFilterAssetManifest(
                    File.ReadAllText(assetsPath),
                    artifactsByName,
                    out var assetsJson)
                || !TryFilterLegacyReleases(
                    File.ReadAllLines(legacyReleasesPath),
                    artifactsByName.Keys,
                    out var legacyReleaseLines))
            {
                LogManifestRebuildFailure(logger, "channel", "invalid-or-incomplete-schema");
                return false;
            }

            // 三份新内容都在内存中验证完成后才逐份原子替换。若中途 IO 失败，目标 Velopack
            // 文件尚未删除，旧/新 manifest 都只会引用仍存在的文件；操作保持 Failed，重试继续收敛。
            return TryWriteTextAtomic(controlledRoot, releasesPath, releasesJson, logger)
                   && TryWriteTextAtomic(controlledRoot, assetsPath, assetsJson, logger)
                   && TryWriteLinesAtomic(controlledRoot, legacyReleasesPath, legacyReleaseLines, logger);
        }
        catch (ClientReleaseValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ClientReleasePublishDiagnostics.LogFailure(
                logger,
                LogLevel.Warning,
                new EventId(4612, "ClientReleaseVelopackManifestRebuildFailure"),
                "velopack-manifest-rebuild",
                ex,
                "channel");
            return false;
        }
    }

    private static bool TryNormalizeSurvivingArtifacts(
        string controlledRoot,
        string velopackChannelRoot,
        IReadOnlyCollection<ClientReleaseVelopackArtifactFact> survivingArtifacts,
        out IReadOnlyDictionary<string, ClientReleaseVelopackArtifactFact> artifactsByName)
    {
        var normalized = new Dictionary<string, ClientReleaseVelopackArtifactFact>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in survivingArtifacts)
        {
            var fileName = Path.GetFileName(artifact.FileName);
            if (string.IsNullOrWhiteSpace(fileName)
                || !string.Equals(fileName, artifact.FileName, StringComparison.Ordinal)
                || !ClientReleaseFileFacts.IsSha256(artifact.Sha256)
                || artifact.SizeBytes < 0)
            {
                artifactsByName = normalized;
                return false;
            }

            if (normalized.TryGetValue(fileName, out var existing))
            {
                if (!string.Equals(existing.Sha256, artifact.Sha256, StringComparison.OrdinalIgnoreCase)
                    || existing.SizeBytes != artifact.SizeBytes)
                {
                    throw new ClientReleaseValidationException("存活 Velopack 文件登记事实冲突。");
                }

                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(velopackChannelRoot, fileName));
            if (!ClientReleaseFileFacts.IsStrictChildPath(velopackChannelRoot, fullPath)
                || !File.Exists(fullPath))
            {
                artifactsByName = normalized;
                return false;
            }

            ClientReleaseControlledDirectory.ValidateChain(
                controlledRoot,
                Path.GetDirectoryName(fullPath)!,
                "存活 Velopack 文件祖先链包含符号链接或越过受控发布目录。");
            if (!ClientReleaseFileFacts.IsExactRegularFile(
                    fullPath,
                    artifact.Sha256,
                    artifact.SizeBytes))
            {
                throw new ClientReleaseValidationException("存活 Velopack 文件事实不匹配。");
            }

            normalized.Add(fileName, artifact with { FileName = fileName });
        }

        artifactsByName = normalized;
        return true;
    }

    private static bool TryFilterReleaseFeed(
        string json,
        IReadOnlyDictionary<string, ClientReleaseVelopackArtifactFact> artifactsByName,
        out string filteredJson)
    {
        filteredJson = string.Empty;
        var root = JsonNode.Parse(json) as JsonObject;
        var assetsProperty = root?.FirstOrDefault(
            property => string.Equals(property.Key, "Assets", StringComparison.OrdinalIgnoreCase));
        if (root is null || assetsProperty?.Value is not JsonArray assets)
        {
            return false;
        }

        var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = assets.Count - 1; index >= 0; index--)
        {
            if (assets[index] is not JsonObject asset
                || !TryReadStringProperty(asset, "FileName", out var fileName))
            {
                return false;
            }

            fileName = Path.GetFileName(fileName);
            if (!artifactsByName.ContainsKey(fileName))
            {
                assets.RemoveAt(index);
                continue;
            }

            retained.Add(fileName);
        }

        var requiredPackages = artifactsByName.Keys.Where(IsNugetPackage);
        if (requiredPackages.Any(fileName => !retained.Contains(fileName)))
        {
            return false;
        }

        filteredJson = root.ToJsonString();
        return true;
    }

    private static bool TryFilterAssetManifest(
        string json,
        IReadOnlyDictionary<string, ClientReleaseVelopackArtifactFact> artifactsByName,
        out string filteredJson)
    {
        filteredJson = string.Empty;
        if (JsonNode.Parse(json) is not JsonArray assets)
        {
            return false;
        }

        var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = assets.Count - 1; index >= 0; index--)
        {
            if (assets[index] is not JsonObject asset
                || !TryReadStringProperty(asset, "RelativeFileName", out var relativeFileName))
            {
                return false;
            }

            var fileName = Path.GetFileName(relativeFileName);
            if (!artifactsByName.ContainsKey(fileName))
            {
                assets.RemoveAt(index);
                continue;
            }

            retained.Add(fileName);
        }

        if (artifactsByName.Keys.Any(fileName => !retained.Contains(fileName)))
        {
            return false;
        }

        filteredJson = assets.ToJsonString();
        return true;
    }

    private static bool TryFilterLegacyReleases(
        IReadOnlyList<string> lines,
        IEnumerable<string> survivingFileNames,
        out string[] filteredLines)
    {
        var survivingPackages = new HashSet<string>(
            survivingFileNames.Where(IsNugetPackage),
            StringComparer.OrdinalIgnoreCase);
        var retained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var segments = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var fileName = segments.FirstOrDefault(IsNugetPackage);
            if (fileName is null)
            {
                filteredLines = [];
                return false;
            }

            fileName = Path.GetFileName(fileName);
            if (!survivingPackages.Contains(fileName))
            {
                continue;
            }

            retained.Add(fileName);
            output.Add(line);
        }

        if (survivingPackages.Any(fileName => !retained.Contains(fileName)))
        {
            filteredLines = [];
            return false;
        }

        filteredLines = [.. output];
        return true;
    }

    private static bool TryReadStringProperty(
        JsonObject item,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        var property = item.FirstOrDefault(
            candidate => string.Equals(candidate.Key, propertyName, StringComparison.OrdinalIgnoreCase));
        if (property.Value is JsonValue jsonValue
            && jsonValue.TryGetValue<string>(out var candidate)
            && !string.IsNullOrWhiteSpace(candidate))
        {
            value = candidate;
            return true;
        }

        return false;
    }

    private static void ValidateMutableManifest(string controlledRoot, string manifestPath)
    {
        ClientReleaseControlledDirectory.ValidateChain(
            controlledRoot,
            Path.GetDirectoryName(manifestPath)!,
            "Velopack manifest 祖先链包含符号链接或越过受控发布目录。");
        var attributes = File.GetAttributes(manifestPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0
            || (attributes & FileAttributes.Directory) != 0)
        {
            throw new ClientReleaseValidationException("Velopack manifest 不是受控普通文件。");
        }
    }

    private static bool TryWriteTextAtomic(
        string controlledRoot,
        string targetPath,
        string content,
        ILogger logger)
    {
        var privatePath = CreatePrivateSiblingPath(targetPath, "rebuild");
        try
        {
            ValidateMutableManifest(controlledRoot, targetPath);
            using (var stream = new FileStream(
                       privatePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            // 生成私有同级文件后再次检查目标及其祖先链，拒绝“读取后、替换前”发生的
            // symlink/reparse 置换。仍由同目录原子替换保证不会暴露半写 manifest。
            ValidateMutableManifest(controlledRoot, targetPath);
            File.Move(privatePath, targetPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogManifestWriteFailure(logger, ex, Path.GetFileName(targetPath));
            TryDeletePrivateFile(privatePath);
            return false;
        }
    }

    private static bool TryWriteLinesAtomic(
        string controlledRoot,
        string targetPath,
        string[] lines,
        ILogger logger)
        => TryWriteTextAtomic(
            controlledRoot,
            targetPath,
            lines.Length == 0 ? string.Empty : string.Join(Environment.NewLine, lines) + Environment.NewLine,
            logger);

    private static void LogManifestWriteFailure(ILogger logger, Exception ex, string fileName)
    {
        ClientReleasePublishDiagnostics.LogFailure(
            logger,
            LogLevel.Warning,
            new EventId(4612, "ClientReleaseVelopackManifestRebuildFailure"),
            "velopack-manifest-rebuild",
            ex,
            fileName);
    }

    private static void LogManifestRebuildFailure(ILogger logger, string manifestName, string condition)
    {
        ClientReleasePublishDiagnostics.LogCondition(
            logger,
            new EventId(4612, "ClientReleaseVelopackManifestRebuildFailure"),
            "velopack-manifest-rebuild",
            condition,
            manifestName);
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
