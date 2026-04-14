using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Recipes;

/// <summary>
/// 业务指令:物理删除配方
/// </summary>
[AuthorizeRequirement("Recipe.Delete")]
public record DeleteRecipeCommand(Guid RecipeId) : IHumanCommand<Result<bool>>;

public class DeleteRecipeHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IRepository<Recipe> recipeRepository,
    ICacheService cacheService
) : ICommandHandler<DeleteRecipeCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteRecipeCommand request,
        CancellationToken cancellationToken)
    {
        var recipe = await recipeRepository.GetSingleOrDefaultAsync(
            new RecipeByIdSpec(request.RecipeId),
            cancellationToken);

        if (recipe is null)
            return Result.Failure("操作失败:目标配方不存在");

        // ABAC 权限校验
        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(userId),
                cancellationToken);

            if (employee is null)
                return Result.Failure("系统中未找到您的员工档案");

            var hasDeviceAccess = employee.DeviceAccesses
                .Any(d => d.DeviceId == recipe.DeviceId);
            if (!hasDeviceAccess)
                return Result.Failure("越权:您没有该机台的管辖权,禁止删除此配方");
        }

        recipeRepository.Delete(recipe);
        var affected = await recipeRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync(CacheKeys.Recipe(recipe.Id), cancellationToken);
            await cacheService.RemoveAsync(
                CacheKeys.RecipesByProcess(recipe.ProcessId), cancellationToken);
            await cacheService.RemoveAsync(
                CacheKeys.RecipesByDevice(recipe.DeviceId), cancellationToken);
        }

        return Result.Success(true);
    }
}
