using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.Recipes;

/// <summary>
/// 查询同名同工序同设备下当前所有处于 Active 状态的配方版本。
/// 用于配方升级时批量归档旧版本。
/// </summary>
public sealed class RecipeActiveVersionsSpec : Specification<Recipe>
{
    public RecipeActiveVersionsSpec(string recipeName, Guid processId, Guid? deviceId)
    {
        FilterCondition = r =>
            r.RecipeName == recipeName
            && r.ProcessId == processId
            && r.DeviceId == deviceId
            && r.Status == RecipeStatus.Active;
    }
}