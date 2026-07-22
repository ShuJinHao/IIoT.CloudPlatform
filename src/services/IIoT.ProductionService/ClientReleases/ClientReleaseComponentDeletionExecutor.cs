using IIoT.Core.Production.Aggregates.ClientReleases;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.ClientReleases;

/// <summary>
/// 按持久化删除操作快照执行幂等文件清理。
/// 只做纯文件操作，可在新进程/新 DbContext 中按操作 ID 重放到全部目标收敛。
/// 每个文件目标删除前重新校验从受控根到文件的完整祖先链与登记事实（SHA256/大小），
/// 防止持久化后祖先目录被替换为符号链接造成穿透。channel manifest 不作为普通文件目标，
/// 有存活 Host 时按白名单重建，没有存活 Host 时才统一清理。
/// </summary>
internal sealed class ClientReleaseComponentDeletionExecutor(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    ILogger logger)
{
    public const string FailureFileDeletion = "FileDeletionFailed";
    public const string FailureManifestRebuild = "ManifestRebuildFailed";
    public const string FailureFileFactsMismatch = "FileFactsMismatch";

    /// <summary>
    /// survivingNupkgFileNames 是同 channel 全部 runtime 存活 Host 版本仍引用的 .nupkg 文件名，
    /// 用于白名单重建 channel manifests 并保留共享包；hasSurvivingHost 为 false 时才清理 channel manifest。
    /// </summary>
    public ClientReleaseComponentDeletionOutcome Execute(
        ClientReleaseComponentDeletion deletion,
        IReadOnlyCollection<string> survivingNupkgFileNames,
        bool hasSurvivingHost,
        CancellationToken cancellationToken)
    {
        var edgeRoot = artifactOptions.Value.ResolveEdgeUpdatesRoot();
        ClientReleaseControlledDirectory.ValidateChain(
            edgeRoot,
            edgeRoot,
            "发布受控目录非法。");

        var isHost = string.Equals(deletion.ComponentKind, "Host", StringComparison.OrdinalIgnoreCase);
        var velopackChannelPrefix = $"velopack/{deletion.Channel}/";
        var deletedPaths = new List<string>();
        var targets = deletion.Files
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();
        foreach (var file in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = file.RelativePath;
            // Host 的 velopack .nupkg 与 channel manifest 都不在第一阶段删除，
            // 分别在白名单判定与 manifest 阶段处理。
            if (isHost
                && relativePath.StartsWith(velopackChannelPrefix, StringComparison.Ordinal)
                && (relativePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                    || ClientReleaseVelopackPaths.IsChannelManifest(Path.GetFileName(relativePath))))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(edgeRoot, relativePath));
            try
            {
                if (!ClientReleaseFileFacts.IsStrictChildPath(edgeRoot, fullPath)
                    || fullPath.Contains("..", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("删除操作文件目标越过受控发布目录。");
                }

                if (!File.Exists(fullPath))
                {
                    continue;
                }

                // 重校验完整祖先链（持久化后祖先目录可能被替换为符号链接）。
                ClientReleaseControlledDirectory.ValidateChain(
                    edgeRoot,
                    Path.GetDirectoryName(fullPath)!,
                    "删除操作文件目标祖先链包含符号链接或越过受控发布目录。");
                AssertFileFacts(fullPath, file);

                File.Delete(fullPath);
                deletedPaths.Add(relativePath);
            }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    ClientReleasePublishDiagnostics.LogFailure(
                        logger,
                        LogLevel.Warning,
                        new EventId(4610, "ClientReleaseComponentFileDeletionFailure"),
                        "component-file-deletion",
                        ex,
                        deletion.ComponentKey);
                    return new ClientReleaseComponentDeletionOutcome(
                        false,
                        deletedPaths,
                        [],
                        FailureFileDeletion,
                        false);
                }
        }

        var skippedPaths = new List<string>();
        var manifestChanged = false;
        if (isHost)
        {
            var velopackRoot = Path.Combine(edgeRoot, "velopack", deletion.Channel);
            var operationNupkgs = targets
                .Select(file => file.RelativePath)
                .Where(path =>
                    path.StartsWith(velopackChannelPrefix, StringComparison.Ordinal)
                    && path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (hasSurvivingHost)
            {
                // 仍有存活 Host：按白名单重建 manifest，manifest 必须保留。
                if (Directory.Exists(velopackRoot)
                    && !ClientReleaseVelopackPaths.TryRebuildChannelManifestsFromWhitelist(
                        velopackRoot,
                        survivingNupkgFileNames,
                        logger))
                {
                    return new ClientReleaseComponentDeletionOutcome(
                        false,
                        deletedPaths,
                        skippedPaths,
                        FailureManifestRebuild,
                        false);
                }

                manifestChanged = Directory.Exists(velopackRoot);
            }
            else if (Directory.Exists(velopackRoot))
            {
                // 没有任何存活 Host：channel manifest 已无服务对象，统一清理。
                var before = deletedPaths.Count;
                if (!TryDeleteChannelManifests(velopackRoot, deletion, deletedPaths))
                {
                    return new ClientReleaseComponentDeletionOutcome(
                        false,
                        deletedPaths,
                        skippedPaths,
                        FailureFileDeletion,
                        false);
                }

                manifestChanged = deletedPaths.Count > before;
            }

            // 操作目标里仍被白名单保留的 .nupkg 属于共享包，跳过删除；其余在白名单判定后删除。
            var shared = new HashSet<string>(survivingNupkgFileNames, StringComparer.OrdinalIgnoreCase);
            foreach (var name in operationNupkgs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(velopackRoot, name);
                if (!File.Exists(path))
                {
                    continue;
                }

                if (shared.Contains(name))
                {
                    skippedPaths.Add($"{velopackChannelPrefix}{name}");
                    continue;
                }

                try
                {
                    File.Delete(path);
                    deletedPaths.Add($"{velopackChannelPrefix}{name}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ClientReleasePublishDiagnostics.LogFailure(
                        logger,
                        LogLevel.Warning,
                        new EventId(4610, "ClientReleaseComponentFileDeletionFailure"),
                        "component-orphan-nupkg-deletion",
                        ex,
                        deletion.ComponentKey);
                    return new ClientReleaseComponentDeletionOutcome(
                        false,
                        deletedPaths,
                        skippedPaths,
                        FailureFileDeletion,
                        manifestChanged);
                }
            }
        }

        TryRemoveEmptyDirectories(edgeRoot, deletion, cancellationToken);

        return new ClientReleaseComponentDeletionOutcome(
            true,
            deletedPaths,
            skippedPaths,
            null,
            manifestChanged);
    }

    private static void AssertFileFacts(string fullPath, ClientReleaseComponentDeletionFile file)
    {
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("删除操作文件目标包含符号链接或重解析点。");
        }

        if (!string.IsNullOrEmpty(file.Sha256) && file.SizeBytes.HasValue)
        {
            if (!ClientReleaseFileFacts.IsExactRegularFile(fullPath, file.Sha256, file.SizeBytes.Value))
            {
                throw new InvalidOperationException($"删除操作文件事实不匹配: {file.RelativePath}");
            }

            return;
        }

        if (file.SizeBytes.HasValue && new FileInfo(fullPath).Length != file.SizeBytes.Value)
        {
            throw new InvalidOperationException($"删除操作文件大小不匹配: {file.RelativePath}");
        }
    }

    private bool TryDeleteChannelManifests(
        string velopackRoot,
        ClientReleaseComponentDeletion deletion,
        ICollection<string> deletedPaths)
    {
        foreach (var entry in Directory.EnumerateFiles(velopackRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(entry);
            if (!ClientReleaseVelopackPaths.IsChannelManifest(name))
            {
                continue;
            }

            try
            {
                File.Delete(entry);
                deletedPaths.Add($"velopack/{deletion.Channel}/{name}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ClientReleasePublishDiagnostics.LogFailure(
                    logger,
                    LogLevel.Warning,
                    new EventId(4610, "ClientReleaseComponentFileDeletionFailure"),
                    "channel-manifest-deletion",
                    ex,
                    deletion.ComponentKey);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 空目录清理是最佳努力收尾：已删文件的父目录链只在目录树全空时移除；插件操作额外把
    /// plugins/{channel}/{moduleId} 根目录纳入。目录树不空时保留，避免误删仍被引用的文件所在目录。
    /// 失败不阻断删除收敛，目录本身不是可分发文件。
    /// </summary>
    private static void TryRemoveEmptyDirectories(
        string edgeRoot,
        ClientReleaseComponentDeletion deletion,
        CancellationToken cancellationToken)
    {
        var fullRoot = Path.GetFullPath(edgeRoot);
        var deletedDirectories = new List<string>();
        foreach (var relativePath in deletion.Files.Select(file => file.RelativePath))
        {
            var directory = Path.GetDirectoryName(relativePath);
            while (!string.IsNullOrEmpty(directory))
            {
                deletedDirectories.Add(directory);
                directory = Path.GetDirectoryName(directory);
            }
        }

        // 插件永久删除覆盖整个 plugins/{channel}/{moduleId} 目录：文件目标收敛后整个目录树应为空，
        // 把操作根目录也纳入空目录链清理，保证成功后目录不残留。
        if (string.Equals(deletion.ComponentKind, "Plugin", StringComparison.OrdinalIgnoreCase))
        {
            deletedDirectories.Add(Path.Combine("plugins", deletion.Channel, deletion.ComponentKey));
        }

        foreach (var relativeDirectory in deletedDirectories
                     .Distinct(StringComparer.Ordinal)
                     .OrderByDescending(directory => directory.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullDirectory = Path.GetFullPath(Path.Combine(fullRoot, relativeDirectory));
            if (!ClientReleaseFileFacts.IsStrictChildPath(fullRoot, fullDirectory)
                || !IsEmptyDirectoryChain(fullDirectory))
            {
                continue;
            }

            try
            {
                Directory.Delete(fullDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _ = ex;
            }
        }
    }

    /// <summary>
    /// 确认目录树里只剩空目录，没有任何文件/链接；逐层先拒绝 symlink/reparse 再进入。
    /// </summary>
    private static bool IsEmptyDirectoryChain(string root)
    {
        if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(current, "*", SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint)
                    || !attributes.HasFlag(FileAttributes.Directory))
                {
                    return false;
                }

                pending.Push(entry);
            }
        }

        return true;
    }
}

public sealed record ClientReleaseComponentDeletionOutcome(
    bool Succeeded,
    IReadOnlyList<string> DeletedPaths,
    IReadOnlyList<string> SkippedPaths,
    string? FailureCode,
    bool ManifestChanged);
