using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.ProductionService.ClientReleases;

/// <summary>
/// 发布组件永久删除的受控相对路径计算。
/// 在数据库删除前把组件的登记文件目标展开为精确清单并持久化到删除操作，
/// 之后文件清理按操作 ID 驱动，不再依赖组件内存对象或重新扫描磁盘。
/// </summary>
internal static class ClientReleaseComponentRelativePaths
{
    /// <summary>
    /// 收集组件当前应删除的精确受控相对文件路径。
    /// 登记目录逐层枚举、先拒绝 symlink/reparse point 再进入；已登记但已不存在的文件也保留，
    /// 让删除操作可以幂等收敛（不存在即视为已删）。
    /// </summary>
    public static IReadOnlyList<string> Collect(string edgeRoot, ClientReleaseComponent component)
    {
        var fullRoot = Path.GetFullPath(edgeRoot);
        var paths = new HashSet<string>(StringComparer.Ordinal);
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
                        AddDirectoryFiles(fullRoot, artifact.RelativePath, paths);
                        break;
                    case ClientReleaseArtifactKind.ManifestFile:
                    case ClientReleaseArtifactKind.PackageFile:
                    case ClientReleaseArtifactKind.VelopackFile:
                        paths.Add(artifact.RelativePath);
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
            AddDirectoryFiles(fullRoot, moduleRoot, paths);
        }

        return paths.Order(StringComparer.Ordinal).ToArray();
    }

    private static void AddDirectoryFiles(
        string fullRoot,
        string relativeDirectory,
        ISet<string> paths)
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

                paths.Add(Path.GetRelativePath(fullRoot, entry).Replace('\\', '/'));
            }
        }
    }
}
