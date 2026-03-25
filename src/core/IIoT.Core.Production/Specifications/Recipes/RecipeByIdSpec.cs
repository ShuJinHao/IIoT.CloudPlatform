using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.SharedKernel.Specification;
using System;

namespace IIoT.Core.Production.Specifications.Recipes;

/// <summary>
/// 专用查询规约：根据 ID 获取配方单体档案
/// </summary>
public class RecipeByIdSpec : Specification<Recipe>
{
    public RecipeByIdSpec(Guid recipeId)
    {
        FilterCondition = r => r.Id == recipeId && r.IsActive; // 默认只查未被软删除的有效配方
    }
}