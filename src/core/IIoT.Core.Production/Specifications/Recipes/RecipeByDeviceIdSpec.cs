using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.Recipes;

/// <summary>
/// 专用查询规约：根据设备ID获取该设备可用的配方列表
/// 查询逻辑：该设备的专属特调配方 + 该设备所属工序的通用配方（DeviceId 为 null 的）
/// 边缘端 RecipeSyncTask 定时拉取时使用
/// </summary>
public class RecipeByDeviceIdSpec : Specification<Recipe>
{
    public RecipeByDeviceIdSpec(Guid deviceId, Guid processId)
    {
        // 只查激活状态的配方
        // 1. 该设备的专属特调配方（DeviceId == deviceId）
        // 2. 该设备所属工序的通用配方（DeviceId == null && ProcessId == processId）
        FilterCondition = r => r.IsActive
            && ((r.DeviceId.HasValue && r.DeviceId.Value == deviceId)
                || (!r.DeviceId.HasValue && r.ProcessId == processId));

        SetOrderBy(r => r.RecipeName);
    }
}