using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.ProductionService.ClientReleases;

/// <summary>
/// 发布组件永久删除的受控文件目标计算。
/// 在数据库删除前把组件的登记文件目标展开为带类型与精确事实（SHA256/大小）的清单并持久化到删除操作，
/// 之后文件清理按操作 ID 驱动，不再依赖组件内存对象或重新扫描磁盘。
/// channel manifest（RELEASES / releases.*.json / assets.*.json）不作为普通文件目标删除，
/// 由删除执行器按存活 Host 版本白名单重建或在没有存活 Host 时统一清理。
/// </summary>
internal static class ClientReleaseComponentRelativePaths
{
    /// <summary>
    /// 收集组件当前应删除的精确受控文件目标。
    /// 登记目录逐层枚举、先拒绝 symlink/reparse point 再进入；已登记但已不存在的文件也保留，
    /// 让删除操作可以幂等收敛（不存在即视为已删）。事实从登记 artifact 拷贝，目录展开文件现场读取。
    /// </summary>
    public static IReadOnlyList<ClientReleaseComponentDeletionFileTarget> Collect(
        string edgeRoot,
        ClientReleaseComponent component)
    {
        var fullRoot = Path.GetFullPath(edgeRoot);
        var targets = new Dictionary<string, ClientReleaseComponentDeletionFileTarget>(StringComparer.Ordinal);
        foreach (var version in component.Versions)
        {
            foreach (var artifact in version.Artifacts)
            {
                var fullPath = Path.GetFullPath(Path.Combine(fullRoot, artifact.RelativePath));
                if (!ClientReleaseFileFacts.IsStrictChildPath(fullRoot, fullPath)
                    || fullPath.Contains("..", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("发布文件路径越过受控发布目录。");
                }

                switch (artifact.ArtifactKind)
                {
                    case ClientReleaseArtifactKind.InstallerDirectory:
                    case ClientReleaseArtifactKind.PluginPackageDirectory:
                        AddDirectoryFiles(fullRoot, artifact.RelativePath, targets);
                        break;
                    case ClientReleaseArtifactKind.ManifestFile:
                    case ClientReleaseArtifactKind.PackageFile:
                        AddFileTarget(targets, artifact.RelativePath, artifact);
                        break;
                    case ClientReleaseArtifactKind.VelopackFile:
                        // channel manifest 不做普通文件目标，由执行器按白名单重建/统一清理。
                        if (!ClientReleaseVelopackPaths.IsChannelManifest(Path.GetFileName(artifact.RelativePath)))
                        {
                            AddFileTarget(targets, artifact.RelativePath, artifact);
                        }

                        break;
                    default:
                        break;
                }
            }
        }

        // 插件永久删除覆盖整个 plugins/{channel}/{moduleId} 目录。
        if (component.ComponentKind == ClientReleaseComponentKind.Plugin)
        {
            var moduleRoot = Path.Combine("plugins", component.Channel, component.ComponentKey).Replace('\\', '/');
            AddDirectoryFiles(fullRoot, moduleRoot, targets);
        }

        return targets.Values
            .OrderBy(target => target.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddFileTarget(
        IDictionary<string, ClientReleaseComponentDeletionFileTarget> targets,
        string relativePath,
        ClientReleaseArtifact artifact)
    {
        targets[relativePath] = new ClientReleaseComponentDeletionFileTarget(
            relativePath,
            artifact.ArtifactKind.ToString(),
            artifact.Sha256,
            artifact.Size);
    }

    private static void AddDirectoryFiles(
        string fullRoot,
        string relativeDirectory,
        IDictionary<string, ClientReleaseComponentDeletionFileTarget> targets)
    {
        var fullDirectory = Path.GetFullPath(Path.Combine(fullRoot, relativeDirectory));
        if (!ClientReleaseControlledDirectory.IsExistingDirectory(fullRoot, fullDirectory))
        {
            return;
        }

        var pending = new Stack<string>();
        pending.Push(fullDirectory);
        while (pending.TryPop(out var current))
        {
            ClientReleaseControlledDirectory.ValidateChain(
                fullRoot,
                current,
                "发布文件目录包含符号链接或重解析点。");
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         current,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("发布文件目录中包含符号链接或重解析点。");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                var relativePath = Path.GetRelativePath(fullRoot, entry).Replace('\\', '/');
                if (targets.ContainsKey(relativePath))
                {
                    continue;
                }

                // 目录展开文件现场读取事实，供重试前穿透/替换校验。
                var fact = ClientReleaseFileFacts.GetFileFact(entry);
                targets[relativePath] = new ClientReleaseComponentDeletionFileTarget(
                    relativePath,
                    "PackageFile",
                    fact.Sha256,
                    fact.Size);
            }
        }
    }
}
