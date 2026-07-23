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
/// channel manifest 不作为普通文件目标：有存活 Host 时按真实 Velopack schema 与白名单过滤重建，
/// 任一原始 manifest 缺失或不可验证都 fail closed；没有任何存活 Host 时才统一清理。
/// </summary>
internal sealed class ClientReleaseComponentDeletionExecutor(
    IOptions<EdgeInstallerArtifactOptions> artifactOptions,
    ILogger logger)
{
    public const string FailureFileDeletion = "FileDeletionFailed";
    public const string FailureManifestRebuild = "ManifestRebuildFailed";
    public const string FailureFileFactsMismatch = "FileFactsMismatch";

    /// <summary>
    /// survivingVelopackArtifacts 是同 channel 全部 runtime 存活 Host 版本仍引用的真实 Velopack 文件事实，
    /// 用于按真实 schema 过滤 channel manifests 并保留共享文件；hasSurvivingHost 为 false 时才清理 channel manifest。
    /// </summary>
    public ClientReleaseComponentDeletionOutcome Execute(
        ClientReleaseComponentDeletion deletion,
        IReadOnlyCollection<ClientReleaseVelopackArtifactFact> survivingVelopackArtifacts,
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
            // Host 的全部 channel Velopack 文件都不在第一阶段删除：Setup/Portable/.nupkg 可能被
            // 其它 runtime 的存活 Host 共享，必须在存活事实与 manifest 白名单判定后再决定保留或删除。
            if (isHost
                && relativePath.StartsWith(velopackChannelPrefix, StringComparison.Ordinal)
                && string.Equals(
                    file.ArtifactKind,
                    ClientReleaseArtifactKind.VelopackFile.ToString(),
                    StringComparison.Ordinal))
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
            var operationVelopackFiles = targets
                .Where(file =>
                {
                    var path = file.RelativePath;
                    return path.StartsWith(velopackChannelPrefix, StringComparison.Ordinal)
                           && string.Equals(
                               file.ArtifactKind,
                               ClientReleaseArtifactKind.VelopackFile.ToString(),
                               StringComparison.Ordinal)
                           && !ClientReleaseVelopackPaths.IsChannelManifest(Path.GetFileName(path));
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
                // 仍有存活 Host：channel 目录、真实 manifest 和至少一个存活 .nupkg 都必须存在。
                // 元数据不足时不得从文件名猜造发布清单，统一返回可重试 ManifestRebuildFailed。
                if (!velopackRootExists
                    || !survivingVelopackArtifacts.Any(
                        artifact => artifact.FileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    || !TryRebuildChannelManifests(
                        edgeRoot,
                        velopackRoot,
                        survivingVelopackArtifacts))
                {
                    return new ClientReleaseComponentDeletionOutcome(
                        false,
                        deletedPaths,
                        skippedPaths,
                        FailureManifestRebuild,
                        false);
                }

                manifestChanged = true;
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

            // 操作目标里仍被存活白名单引用的 Setup/Portable/.nupkg 等 channel 文件都属于共享资产，
            // 按存活版本登记事实验证后保留；其余在 manifest 收敛后按删除操作事实删除。
            var shared = survivingVelopackArtifacts.ToDictionary(
                artifact => artifact.FileName,
                StringComparer.OrdinalIgnoreCase);
            foreach (var file in operationVelopackFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file.RelativePath);
                var path = Path.Combine(velopackRoot, name);
                if (!File.Exists(path))
                {
                    continue;
                }

                ClientReleaseControlledDirectory.ValidateChain(
                    edgeRoot,
                    Path.GetDirectoryName(path)!,
                    "删除操作 Velopack 文件祖先链包含符号链接或越过受控发布目录。");
                if (shared.TryGetValue(name, out var survivingFact))
                {
                    AssertFileFacts(
                        path,
                        file.RelativePath,
                        survivingFact.Sha256,
                        survivingFact.SizeBytes);
                    skippedPaths.Add($"{velopackChannelPrefix}{name}");
                    continue;
                }

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
    /// 白名单重建 channel manifest。Cloud 不从文件名猜造 Velopack 元数据；任一原始 manifest
    /// 缺失、结构漂移或无法验证都返回 false。
    /// 读取/覆盖前都重校验 velopack 目录祖先链。
    /// </summary>
    private bool TryRebuildChannelManifests(
        string edgeRoot,
        string velopackRoot,
        IReadOnlyCollection<ClientReleaseVelopackArtifactFact> survivingVelopackArtifacts)
    {
        ClientReleaseControlledDirectory.ValidateChain(
            edgeRoot,
            velopackRoot,
            "velopack channel 目录祖先链包含符号链接或越过受控发布目录。");
        return ClientReleaseVelopackPaths.RebuildChannelManifestsFromWhitelist(
            edgeRoot,
            velopackRoot,
            survivingVelopackArtifacts,
            logger);
    }

    private static void AssertFileFacts(string fullPath, ClientReleaseComponentDeletionFile file)
        => AssertFileFacts(fullPath, file.RelativePath, file.Sha256, file.SizeBytes);

    private static void AssertFileFacts(
        string fullPath,
        string relativePath,
        string? sha256,
        long? sizeBytes)
    {
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ClientReleaseValidationException("删除操作文件目标包含符号链接或重解析点。");
        }

        if (!string.IsNullOrEmpty(sha256) && sizeBytes.HasValue)
        {
            if (!ClientReleaseFileFacts.IsExactRegularFile(fullPath, sha256, sizeBytes.Value))
            {
                throw new ClientReleaseValidationException($"删除操作文件事实不匹配: {relativePath}");
            }

            return;
        }

        if (sizeBytes.HasValue && new FileInfo(fullPath).Length != sizeBytes.Value)
        {
            throw new ClientReleaseValidationException($"删除操作文件大小不匹配: {relativePath}");
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

            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0
                || (attributes & FileAttributes.Directory) != 0)
            {
                throw new ClientReleaseValidationException(
                    "删除操作 Velopack manifest 不是受控普通文件。");
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
