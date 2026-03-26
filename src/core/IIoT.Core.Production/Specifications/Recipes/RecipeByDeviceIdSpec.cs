using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.Recipes;

public class RecipeByDeviceIdSpec : Specification<Recipe>
{
    public RecipeByDeviceIdSpec(Guid deviceId, Guid processId)
    {
        // 边缘端只拉启用状态的配方
        FilterCondition = r => r.Status == RecipeStatus.Active
            && ((r.DeviceId.HasValue && r.DeviceId.Value == deviceId)
                || (!r.DeviceId.HasValue && r.ProcessId == processId));

        SetOrderBy(r => r.RecipeName);
    }
}