using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.Recipes;

/// <summary>
/// 聚合根：工艺配方 (极其灵活，百毒不侵)
/// </summary>
public class Recipe : IAggregateRoot
{
    protected Recipe()
    {
    }

    public Recipe(string recipeName, Guid processId, string parametersJsonb, Guid? deviceId = null)
    {
        Id = Guid.NewGuid();
        RecipeName = recipeName;
        ProcessId = processId;
        ParametersJsonb = parametersJsonb;
        DeviceId = deviceId;
        Version = "V1.0"; // 默认初始版本
        IsActive = true;
    }

    /// <summary>
    /// 配方全局唯一标识 (UUID)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 配方名称 (如：A型号冬季特调配方)
    /// </summary>
    public string RecipeName { get; set; } = null!;

    /// <summary>
    /// 配方版本号 (用于后期追溯和变更管理)
    /// </summary>
    public string Version { get; set; } = null!;

    /// <summary>
    /// 1. 基础归属：它肯定属于某个工序 (关联 MfgProcess 的 UUID)
    /// </summary>
    public Guid ProcessId { get; set; }

    /// <summary>
    /// 2. 🌟 降伏“刺头设备”的杀招：可空专属设备 UUID
    /// 若为 null，代表该工序下的所有机器通用此配方。
    /// 若为具体 UUID，代表这是特定机器的专属特调配方！
    /// </summary>
    public Guid? DeviceId { get; set; }

    /// <summary>
    /// 3. 🌟 降伏“非标参数”的杀招：万能 JSONB 字典
    /// EF Core 将其映射为 PostgreSQL 的 jsonb 字段，WPF 拿去自己解析
    /// </summary>
    public string ParametersJsonb { get; set; } = null!;

    /// <summary>
    /// 是否为激活状态 (停用的配方前端拉取不到)
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 领域行为：更新配方参数（同时可以升级版本号）
    /// </summary>
    public void UpdateParameters(string newParametersJsonb, string newVersion)
    {
        ParametersJsonb = newParametersJsonb;
        Version = newVersion;
    }
}