using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.Recipes;

/// <summary>
/// 配方状态枚举
/// </summary>
public enum RecipeStatus
{
    /// <summary>
    /// 当前启用版本(同一配方同一时间只有一个)
    /// </summary>
    Active,

    /// <summary>
    /// 已归档(被新版本替代后自动归档,保留追溯)
    /// </summary>
    Archived
}

/// <summary>
/// 聚合根:工艺配方
/// 支持版本管理:修改配方 = 创建新版本,旧版本由用例显式归档。
/// </summary>
public class Recipe : BaseEntity<Guid>
{
    /// <summary>
    /// 仅供 EF Core 物化使用,业务代码不要调用。
    /// </summary>
    protected Recipe() { }

    /// <summary>
    /// 创建配方的初始版本(V1.0)。
    /// 后续版本必须通过 <see cref="CreateNextVersion"/> 派生。
    /// </summary>
    public Recipe(
        string recipeName,
        Guid processId,
        Guid deviceId,
        string parametersJsonb)
    {
        Id = Guid.NewGuid();
        RecipeName = recipeName.Trim();
        ProcessId = processId;
        DeviceId = deviceId;
        ParametersJsonb = parametersJsonb.Trim();
        Version = "V1.0";
        Status = RecipeStatus.Active;

        ValidateInvariants();
    }

    /// <summary>
    /// 私有构造,用于 <see cref="CreateNextVersion"/> 派生新版本。
    /// </summary>
    private Recipe(
        string recipeName,
        Guid processId,
        Guid deviceId,
        string parametersJsonb,
        string version)
    {
        Id = Guid.NewGuid();
        RecipeName = recipeName.Trim();
        ProcessId = processId;
        DeviceId = deviceId;
        ParametersJsonb = parametersJsonb.Trim();
        Version = version.Trim();
        Status = RecipeStatus.Active;

        ValidateInvariants();
    }

    /// <summary>
    /// 配方名称
    /// </summary>
    public string RecipeName { get; private set; } = null!;

    /// <summary>
    /// 配方版本号 (用于追溯和变更管理)
    /// </summary>
    public string Version { get; private set; } = null!;

    /// <summary>
    /// 归属工序 (关联 MfgProcess 的 UUID)
    /// </summary>
    public Guid ProcessId { get; private set; }

    /// <summary>
    /// 归属设备 UUID(配方强绑定到具体设备)
    /// </summary>
    public Guid DeviceId { get; private set; }

    /// <summary>
    /// 配方参数 (JSONB)
    /// 结构:[{ id, name, unit, min, max }, ...]
    /// </summary>
    public string ParametersJsonb { get; private set; } = null!;

    /// <summary>
    /// 配方状态 (Active = 启用, Archived = 已归档)
    /// </summary>
    public RecipeStatus Status { get; private set; }

    // ── 行为 ──────────────────────────────────────────────

    /// <summary>
    /// 基于当前实例派生一个新版本配方。
    /// 新版本继承 RecipeName / ProcessId / DeviceId,版本号和参数由调用方指定。
    /// 注意:本方法不会副作用地归档当前实例 — 旧版本的归档由用例显式调用 <see cref="Archive"/>。
    /// </summary>
    public Recipe CreateNextVersion(string newVersion, string newParametersJsonb)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(newParametersJsonb);

        return new Recipe(
            RecipeName,
            ProcessId,
            DeviceId,
            newParametersJsonb,
            newVersion.Trim());
    }

    /// <summary>
    /// 归档此版本(被新版本替代时由用例调用)
    /// </summary>
    public void Archive()
    {
        Status = RecipeStatus.Archived;
    }

    private void ValidateInvariants()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(RecipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ParametersJsonb);
        ArgumentException.ThrowIfNullOrWhiteSpace(Version);
        if (ProcessId == Guid.Empty)
            throw new ArgumentException("ProcessId 不能为空。", nameof(ProcessId));
        if (DeviceId == Guid.Empty)
            throw new ArgumentException("DeviceId 不能为空。", nameof(DeviceId));
    }
}
