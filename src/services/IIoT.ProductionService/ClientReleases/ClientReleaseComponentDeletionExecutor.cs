using IIoT.Core.Production.Aggregates.ClientReleases;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.ClientReleases;

/// <summary>
/// 按持久化删除操作快照执行幂等文件清理。
/// 只做纯文件操作，可在新进程/新 DbContext 中按操作 ID 重放到全部目标收敛。
/// </summary>
internal sealed class ClientReleaseComponentDeletionExecutor(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    ILogger logger)
{
    public const string FailureFileDeletion = "FileDeletionFailed";
    public const string FailureManifestRebuild = "ManifestRebuildFailed";

    /// <summary>
    /// survivingNupkgFileNames 是同 channel 全部 runtime 存活 Host 版本仍引用的 .nupkg 文件名，
    /// 用于白名单重建 channel manifests 并保留共享包。
    /// </summary>
    public ClientReleaseComponentDeletionOutcome Execute(
        ClientReleaseComponentDeletion deletion,
        IReadOnlyCollection<string> survivingNupkgFileNames,
        CancellationToken cancellationToken)
    {
        var edgeRoot = artifactOptions.Value.ResolveEdgeUpdatesRoot();
        ClientReleaseControlledDirectory.ValidateChain(
            edgeRoot,
            edgeRoot,
            "发布受控目录非法。");

        var deletedPaths = new List<string>();
        var isHost = string.Equals(deletion.ComponentKind, "Host", StringComparison.OrdinalIgnoreCase);
        var targets = deletion.Files
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        foreach (var relativePath in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Host 的 velopack .nupkg 先按白名单判断是否共享，共享的在后面保留，不能直接删。
            if (isHost
                && relativePath.StartsWith($"velopack/{deletion.Channel}/", StringComparison.Ordinal)
                && relativePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
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

                if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("删除操作文件目标包含符号链接或重解析点。");
                }

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
                    FailureFileDeletion);
            }
        }

        // 目录内容由删除操作中的精确文件目标删除；空目录由清理最佳努力移除。
        var skippedPaths = new List<string>();
        if (string.Equals(deletion.ComponentKind, "Host", StringComparison.OrdinalIgnoreCase))
        {
            var velopackRoot = Path.Combine(edgeRoot, "velopack", deletion.Channel);
            var operationNupkgs = targets
                .Where(path =>
                    path.StartsWith($"velopack/{deletion.Channel}/", StringComparison.Ordinal)
                    && path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 白名单 = 同 channel 全部 runtime 存活 Host 仍引用的 .nupkg；manifest 按白名单重建。
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
                    FailureManifestRebuild);
            }

            // 操作目标里仍被白名单保留的 .nupkg 属于共享包，跳过删除。
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
                    skippedPaths.Add($"velopack/{deletion.Channel}/{name}");
                    continue;
                }

                try
                {
                    File.Delete(path);
                    deletedPaths.Add($"velopack/{deletion.Channel}/{name}");
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
                        FailureFileDeletion);
                }
            }
        }

        TryRemoveEmptyDirectories(edgeRoot, deletion, cancellationToken);

        return new ClientReleaseComponentDeletionOutcome(
            true,
            deletedPaths,
            skippedPaths,
            null);
    }

    /// <summary>
    /// 空目录清理是最佳努力收尾：先清每个已删文件的父目录链，再对整个操作根目录
    /// （插件 plugins/{channel}/{moduleId}、Host velopack/{channel}）自底向上清一次。
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

        // 已删文件的父目录链只在目录树全空时移除；目录树不空时保留，避免误删仍被引用的文件所在目录。
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
    string? FailureCode);
