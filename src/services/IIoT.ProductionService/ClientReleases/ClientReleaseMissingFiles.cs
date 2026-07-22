using IIoT.Core.Production.Aggregates.ClientReleases;

namespace IIoT.ProductionService.ClientReleases;

/// <summary>
/// 收集当前 catalog 组件中登记文件已不在受控目录上的相对路径，
/// 供 catalog 查询在硬删除文件清理未完成前继续隐藏这些版本。
/// </summary>
internal static class ClientReleaseMissingFiles
{
    public static ISet<string> Collect(
        string edgeRoot,
        IEnumerable<ClientReleaseComponent> components)
    {
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in components)
        {
            foreach (var version in component.Versions)
            {
                foreach (var artifact in version.Artifacts)
                {
                    var fullPath = Path.GetFullPath(Path.Combine(edgeRoot, artifact.RelativePath));
                    if (!ClientReleaseFileFacts.IsStrictChildPath(edgeRoot, fullPath))
                    {
                        missing.Add(artifact.RelativePath);
                        continue;
                    }

                    var exists = artifact.ArtifactKind switch
                    {
                        ClientReleaseArtifactKind.InstallerDirectory
                            or ClientReleaseArtifactKind.PluginPackageDirectory => Directory.Exists(fullPath),
                        ClientReleaseArtifactKind.ManifestFile
                            or ClientReleaseArtifactKind.PackageFile
                            or ClientReleaseArtifactKind.VelopackFile => File.Exists(fullPath),
                        _ => false
                    };
                    if (!exists)
                    {
                        missing.Add(artifact.RelativePath);
                    }
                }
            }
        }

        return missing;
    }
}
