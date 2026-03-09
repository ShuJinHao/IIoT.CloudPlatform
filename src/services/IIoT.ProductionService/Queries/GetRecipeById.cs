using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries;

[AuthorizeRequirement("Recipe.Read")]
public record GetRecipeByIdQuery(Guid RecipeId) : IQuery<Result<Recipe>>;

public class GetRecipeByIdHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IReadRepository<Recipe> recipeRepository,
    ICacheService cacheService) : IQueryHandler<GetRecipeByIdQuery, Result<Recipe>>
{
    public async Task<Result<Recipe>> Handle(GetRecipeByIdQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = $"iiot:recipe:v1:{request.RecipeId}";

        // 1. 🌟 读链路第一步：尝试从缓存极速读取
        var cachedRecipe = await cacheService.GetAsync<Recipe>(cacheKey, cancellationToken);

        Recipe? recipe = cachedRecipe;

        // 2. 🌟 读链路第二步：缓存未命中，查数据库
        if (recipe == null)
        {
            recipe = await recipeRepository.GetByIdAsync(request.RecipeId, cancellationToken);
            if (recipe == null) return Result.NotFound();

            // 回写缓存 (设置 2 小时过期)
            await cacheService.SetAsync(cacheKey, recipe, TimeSpan.FromHours(2), cancellationToken);
        }

        // 3. 🌟 【第二维度拦截】：即使是查询，也要看管辖权！
        if (currentUser.Role != "Admin")
        {
            var userId = Guid.Parse(currentUser.Id!);
            var employee = await employeeRepository.GetAsync(e => e.Id == userId, [e => e.DeviceAccesses, e => e.ProcessAccesses], cancellationToken);

            bool hasAccess = recipe.DeviceId.HasValue
                ? employee!.DeviceAccesses.Any(da => da.DeviceId == recipe.DeviceId.Value)
                : employee!.ProcessAccesses.Any(pa => pa.ProcessId == recipe.ProcessId);

            if (!hasAccess) return Result.Failure("拒绝访问：该配方不在您的管辖范围内");
        }

        return Result.Success(recipe);
    }
}