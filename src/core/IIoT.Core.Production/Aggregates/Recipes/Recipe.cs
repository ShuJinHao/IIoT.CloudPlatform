using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.Recipes;

/// <summary>
/// 配方状态枚举
/// </summary>
public enum RecipeStatus
{
    /// <summary>
    /// 当前启用版本（同一配方同一时间只有一个）
    /// </summary>
    Active,

    /// <summary>
    /// 已归档（被新版本替代后自动归档，保留追溯）
    /// </summary>
    Archived
}

/// <summary>
/// 聚合根：工艺配方
/// 支持版本管理：修改配方 = 创建新版本，旧版本自动归档
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
        Version = "V1.0";
        Status = RecipeStatus.Active;
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
    /// 配方版本号 (用于追溯和变更管理)
    /// </summary>
    public string Version { get; set; } = null!;

    /// <summary>
    /// 归属工序 (关联 MfgProcess 的 UUID)
    /// </summary>
    public Guid ProcessId { get; set; }

    /// <summary>
    /// 可空专属设备 UUID
    /// null = 该工序下通用配方，有值 = 特定机器专属配方
    /// </summary>
    public Guid? DeviceId { get; set; }

    /// <summary>
    /// 配方参数 (JSONB)
    /// 结构：[{ id, name, unit, min, max }, ...]
    /// </summary>
    public string ParametersJsonb { get; set; } = null!;

    /// <summary>
    /// 配方状态 (Active = 启用, Archived = 已归档)
    /// </summary>
    public RecipeStatus Status { get; set; }

    /// <summary>
    /// 领域行为：归档此版本（被新版本替代时调用）
    /// </summary>
    public void Archive()
    {
        Status = RecipeStatus.Archived;
    }
}