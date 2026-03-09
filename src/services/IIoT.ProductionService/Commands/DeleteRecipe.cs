using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands;

[AuthorizeRequirement("Recipe.Delete")]
public record DeleteRecipeCommand(Guid RecipeId) : ICommand<Result<bool>>;

public class DeleteRecipeHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IRepository<Recipe> recipeRepository,
    ICacheService cacheService) : ICommandHandler<DeleteRecipeCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeleteRecipeCommand request, CancellationToken cancellationToken)
    {
        var recipe = await recipeRepository.GetByIdAsync(request.RecipeId, cancellationToken);
        if (recipe == null) return Result.Failure("配方不存在");

        // 🌟 【第二维度拦截】：校验管辖权
        if (currentUser.Role != "Admin")
        {
            var userId = Guid.Parse(currentUser.Id!);
            var employee = await employeeRepository.GetAsync(e => e.Id == userId, [e => e.DeviceAccesses, e => e.ProcessAccesses], cancellationToken);

            bool hasAccess = recipe.DeviceId.HasValue
                ? employee!.DeviceAccesses.Any(da => da.DeviceId == recipe.DeviceId.Value)
                : employee!.ProcessAccesses.Any(pa => pa.ProcessId == recipe.ProcessId);

            if (!hasAccess) return Result.Failure("越权操作：您没有该配方的删除权限");
        }

        // 1. 物理删除 (或你可以改为逻辑删除)
        recipeRepository.Delete(recipe);
        await recipeRepository.SaveChangesAsync(cancellationToken);

        // 2. 🌟 缓存爆破：删掉单体，删掉关联列表
        await cacheService.RemoveAsync($"iiot:recipe:v1:{request.RecipeId}", cancellationToken);
        await cacheService.RemoveAsync($"iiot:recipes:process:v1:{recipe.ProcessId}", cancellationToken);

        return Result.Success(true);
    }
}