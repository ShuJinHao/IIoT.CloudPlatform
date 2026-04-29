using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
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
    IRepository<Recipe> recipeRepository,
    IDevicePermissionService devicePermissionService)
    : ICommandHandler<DeleteRecipeCommand, Result<bool>>
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

        if (!string.Equals(
                currentUser.Role,
                IIoT.Services.Contracts.Authorization.SystemRoles.Admin,
                StringComparison.Ordinal))
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
                userId,
                isAdmin: false,
                cancellationToken);
            if (accessibleDeviceIds is null || !accessibleDeviceIds.Contains(recipe.DeviceId))
                return Result.Failure("越权:您没有该机台的管辖权,禁止删除此配方");
        }

        recipe.MarkDeleted();
        recipeRepository.Delete(recipe);
        await recipeRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
