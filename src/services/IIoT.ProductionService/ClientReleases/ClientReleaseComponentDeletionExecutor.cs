using IIoT.Core.Production.Aggregates.ClientReleases;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.ClientReleases;

/// <summary>
/// 按持久化删除操作快照执行幂等文件清理。
/// 只做纯文件操作，可在新进程/新 DbContext 中按操作 ID 重放到全部目标收敛。
/// 所有文件操作（普通文件、Host .nupkg、channel manifest 读取/覆盖/删除、空目录清理）前都重新校验
/// 从受控根到目标的完整祖先链；删除前核对登记事实（SHA256/大小），防止持久化后祖先目录被替换为
/// 符号链接或文件被篡改造成穿透/误删。受控文件安全失败（穿透/事实不匹配/reparse）抛出
/// <see cref="ClientReleaseValidationException"/>，由 processor 统一落 Failed 与稳定失败码。
/// channel manifest 不作为普通文件目标：有存活 Host 时按白名单重建（缺失则真正生成），
/// 没有任何存活 Host 时才统一清理。
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
        // 受控根本身的链校验失败属于受控文件安全失败，直接抛出由 processor 落 Failed。
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
            if (!ClientReleaseFileFacts.IsStrictChildPath(edgeRoot, fullPath)
                || fullPath.Contains("..", StringComparison.Ordinal))
            {
                throw new ClientReleaseValidationException("删除操作文件目标越过受控发布目录。");
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

            try
            {
                File.Delete(fullPath);
                deletedPaths.Add(relativePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
                .Where(file =>
                {
                    var path = file.RelativePath;
                    return path.StartsWith(velopackChannelPrefix, StringComparison.Ordinal)
                           && path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);
                })
                .GroupBy(file => Path.GetFileName(file.RelativePath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            // velopack channel 目录在操作前校验祖先链；被替换为 symlink 会抛受控安全失败。
            var velopackRootExists = Directory.Exists(velopackRoot);
            if (velopackRootExists)
            {
                ClientReleaseControlledDirectory.ValidateChain(
                    edgeRoot,
                    velopackRoot,
                    "velopack channel 目录祖先链包含符号链接或越过受控发布目录。");
            }

            if (hasSurvivingHost)
            {
                // 仍有存活 Host：manifest 必须真正重建。channel 目录存在但 manifest 缺失/非法 → 失败，
                // 不得静默成功给存活 Host 留下缺失清单。channel 目录本身不存在视为已收敛。
                if (velopackRootExists
                    && !TryRebuildChannelManifests(edgeRoot, velopackRoot, survivingNupkgFileNames))
                {
                    return new ClientReleaseComponentDeletionOutcome(
                        false,
                        deletedPaths,
                        skippedPaths,
                        FailureManifestRebuild,
                        false);
                }

                manifestChanged = velopackRootExists;
            }
            else if (velopackRootExists)
            {
                // 没有任何存活 Host：channel manifest 已无服务对象，统一清理。
                var before = deletedPaths.Count;
                if (!TryDeleteChannelManifests(edgeRoot, velopackRoot, deletion, deletedPaths))
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
            // 删除前重校验祖先链与登记事实（持久化后目录/文件可能被替换）。
            var shared = new HashSet<string>(survivingNupkgFileNames, StringComparer.OrdinalIgnoreCase);
            foreach (var file in operationNupkgs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file.RelativePath);
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

                ClientReleaseControlledDirectory.ValidateChain(
                    edgeRoot,
                    Path.GetDirectoryName(path)!,
                    "删除操作 nupkg 目标祖先链包含符号链接或越过受控发布目录。");
                AssertFileFacts(path, file);

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

    /// <summary>
    /// 白名单重建 channel manifest。manifest 缺失时真正从白名单生成；任一 manifest 无法重建返回 false。
    /// 读取/覆盖前都重校验 velopack 目录祖先链。
    /// </summary>
    private bool TryRebuildChannelManifests(
        string edgeRoot,
        string velopackRoot,
        IReadOnlyCollection<string> survivingNupkgFileNames)
    {
        ClientReleaseControlledDirectory.ValidateChain(
            edgeRoot,
            velopackRoot,
            "velopack channel 目录祖先链包含符号链接或越过受控发布目录。");
        return ClientReleaseVelopackPaths.RebuildChannelManifestsFromWhitelist(
            velopackRoot,
            survivingNupkgFileNames,
            logger);
    }

    private static void AssertFileFacts(string fullPath, ClientReleaseComponentDeletionFile file)
    {
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ClientReleaseValidationException("删除操作文件目标包含符号链接或重解析点。");
        }

        if (!string.IsNullOrEmpty(file.Sha256) && file.SizeBytes.HasValue)
        {
            if (!ClientReleaseFileFacts.IsExactRegularFile(fullPath, file.Sha256, file.SizeBytes.Value))
            {
                throw new ClientReleaseValidationException($"删除操作文件事实不匹配: {file.RelativePath}");
            }

            return;
        }

        if (file.SizeBytes.HasValue && new FileInfo(fullPath).Length != file.SizeBytes.Value)
        {
            throw new ClientReleaseValidationException($"删除操作文件大小不匹配: {file.RelativePath}");
        }
    }

    private bool TryDeleteChannelManifests(
        string edgeRoot,
        string velopackRoot,
        ClientReleaseComponentDeletion deletion,
        ICollection<string> deletedPaths)
    {
        ClientReleaseControlledDirectory.ValidateChain(
            edgeRoot,
            velopackRoot,
            "velopack channel 目录祖先链包含符号链接或越过受控发布目录。");
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
    /// plugins/{channel}/{moduleId} 根目录纳入。删除前重校验祖先链；目录树不空或链非法时保留，
    /// 避免误删仍被引用的文件所在目录。失败不阻断删除收敛，目录本身不是可分发文件。
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
                || !IsEmptyDirectoryChain(fullRoot, fullDirectory))
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
    /// 确认目录树里只剩空目录，没有任何文件/链接；进入前先重校验从受控根到该目录的祖先链，
    /// 逐层先拒绝 symlink/reparse 再进入。祖先链非法时返回 false（不删除），不抛出。
    /// </summary>
    private static bool IsEmptyDirectoryChain(string controlledRoot, string root)
    {
        if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        try
        {
            ClientReleaseControlledDirectory.ValidateChain(
                controlledRoot,
                root,
                "空目录清理祖先链包含符号链接或越过受控发布目录。");
        }
        catch (ClientReleaseValidationException)
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
    bool ManifestChanged)
{
    /// <summary>
    /// 成功审计是否已写稳。只有清理成功且成功审计确认落库时才为 true；
    /// 调用方（handler/recovery）不得在未确认时向用户/日志报告永久删除已完成。
    /// </summary>
    public bool AuditConfirmed { get; init; }
}
