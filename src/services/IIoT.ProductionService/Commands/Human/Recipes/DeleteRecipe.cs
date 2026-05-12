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
    IRepository<Recipe> recipeRepository,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService)
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

        var deviceAccess = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            recipe.DeviceId,
            cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return Result.Failure(deviceAccess.Errors?.ToArray() ?? ["越权:您没有该机台的管辖权,禁止删除此配方"]);
        }

        if (recipe.Status != RecipeStatus.Archived)
            return Result.Failure("操作失败:只有已归档配方可以删除");

        recipe.MarkDeleted();
        recipeRepository.Delete(recipe);
        await recipeRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}
