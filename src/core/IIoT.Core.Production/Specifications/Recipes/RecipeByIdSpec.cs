using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.SharedKernel.Specification;

namespace IIoT.Core.Production.Specifications.Recipes;

public class RecipeByIdSpec : Specification<Recipe>
{
    public RecipeByIdSpec(Guid recipeId)
    {
        FilterCondition = r => r.Id == recipeId;
    }
}