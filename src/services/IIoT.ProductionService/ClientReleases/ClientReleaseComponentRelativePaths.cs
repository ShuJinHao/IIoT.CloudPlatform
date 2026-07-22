using IIoT.Core.Production.Aggregates.ClientReleases;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.ClientReleases;

/// <summary>
/// 组件全部登记 artifact 的受控相对路径集合（以 edge-updates 根为基准），
/// 删除执行器和 catalog 收窄过滤共用同一份计算。
/// </summary>
internal static class ClientReleaseComponentRelativePaths
{
    public static IReadOnlyList<string> Collect(string edgeRoot, ClientReleaseComponent component)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        if (component.ComponentKind == ClientReleaseComponentKind.Plugin)
        {
            var moduleRoot = Path.Combine(
                edgeRoot,
                "plugins",
                component.Channel,
                component.ComponentKey);
            if (Directory.Exists(moduleRoot))
            {
                foreach (var file in Directory.EnumerateFiles(
                             moduleRoot,
                             "*",
                             SearchOption.AllDirectories))
                {
                    paths.Add(ToRelative(edgeRoot, file));
                }
            }
        }

        foreach (var version in component.Versions)
        {
            foreach (var artifact in version.Artifacts)
            {
                paths.Add(artifact.RelativePath);
            }
        }

        return paths.Order(StringComparer.Ordinal).ToArray();
    }

    private static string ToRelative(string edgeRoot, string path)
        => Path.GetRelativePath(edgeRoot, path).Replace('\\', '/');
}
