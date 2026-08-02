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
                    if (!IsArtifactPresent(
                            edgeRoot,
                            artifact.ArtifactKind,
                            artifact.RelativePath))
                    {
                        missing.Add(artifact.RelativePath);
                    }
                }
            }
        }

        return missing;
    }

    public static bool IsArtifactPresent(
        string edgeRoot,
        ClientReleaseArtifactKind artifactKind,
        string relativePath)
    {
        try
        {
            var fullPath = Path.GetFullPath(Path.Combine(edgeRoot, relativePath));
            if (!ClientReleaseFileFacts.IsStrictChildPath(edgeRoot, fullPath))
            {
                return false;
            }

            return artifactKind switch
            {
                ClientReleaseArtifactKind.InstallerDirectory
                    or ClientReleaseArtifactKind.PluginPackageDirectory =>
                    ClientReleaseControlledDirectory.IsExistingDirectory(
                        edgeRoot,
                        fullPath),
                ClientReleaseArtifactKind.ManifestFile
                    or ClientReleaseArtifactKind.PackageFile
                    or ClientReleaseArtifactKind.VelopackFile =>
                    File.Exists(fullPath)
                    && Path.GetDirectoryName(fullPath) is { } parent
                    && ClientReleaseControlledDirectory.IsExistingDirectory(
                        edgeRoot,
                        parent)
                    && (File.GetAttributes(fullPath) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0,
                _ => false
            };
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }
}
