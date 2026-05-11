using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.Recipes;

public class RecipeVersionsByDeviceSpec : Specification<Recipe>
{
    public RecipeVersionsByDeviceSpec(
        Guid deviceId,
        Guid? processId = null,
        int skip = 0,
        int take = 0,
        bool isPaging = true)
    {
        FilterCondition = recipe =>
            recipe.DeviceId == deviceId
            && (!processId.HasValue || recipe.ProcessId == processId.Value);

        SetOrderBy(recipe => recipe.RecipeName);

        if (isPaging)
        {
            SetPaging(skip, take);
        }
    }
}
