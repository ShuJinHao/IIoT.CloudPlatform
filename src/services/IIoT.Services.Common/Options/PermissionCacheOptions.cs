namespace IIoT.Services.Common.Options;

/// <summary>
/// 权限模块专属缓存配置 (业务级配置契约)
/// </summary>
/// <remarks>
/// 放在 Common 层是为了让基础设施层能读取配置，同时让宿主层能进行统一注入。
/// </remarks>
public class PermissionCacheOptions
{
    /// <summary>
    /// 配置文件中的节点名称
    /// </summary>
    public const string SectionName = "PermissionCache";

    /// <summary>
    /// Redis 缓存 Key 的统一定义前缀 (例如: "iiot:permissions:")
    /// </summary>
    public string KeyPrefix { get; set; } = "iiot:permissions:v1:";

    /// <summary>
    /// 缓存的绝对过期时间 (单位：小时)
    /// </summary>
    public int ExpirationHours { get; set; } = 2;
}