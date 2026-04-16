using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.Authorization;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Recipes;

[AuthorizeRequirement("Recipe.Create")]
[DistributedLock("iiot:lock:recipe-create:{ProcessId}:{DeviceId}:{RecipeName}", TimeoutSeconds = 5)]
public record CreateRecipeCommand(
    string RecipeName,
    Guid ProcessId,
    Guid DeviceId,
    string ParametersJsonb
) : IHumanCommand<Result<Guid>>;

public class CreateRecipeHandler(
    ICurrentUser currentUser,
    IRepository<Recipe> recipeRepository,
    IProcessReadQueryService processReadQueryService,
    IDeviceReadQueryService deviceReadQueryService,
    IRecipeReadQueryService recipeReadQueryService,
    ICacheService cacheService,
    IDevicePermissionService devicePermissionService)
    : ICommandHandler<CreateRecipeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateRecipeCommand request,
        CancellationToken cancellationToken)
    {
        var recipeName = request.RecipeName?.Trim() ?? string.Empty;
        var parametersJsonb = request.ParametersJsonb?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(recipeName))
            return Result.Failure("配方名称不能为空");
        if (string.IsNullOrEmpty(parametersJsonb))
            return Result.Failure("配方参数不能为空");
        if (request.ProcessId == Guid.Empty)
            return Result.Failure("工序不能为空");
        if (request.DeviceId == Guid.Empty)
            return Result.Failure("设备不能为空");

        var processExists = await processReadQueryService.ExistsAsync(
            request.ProcessId,
            cancellationToken);
        if (!processExists)
            return Result.Failure("配方创建失败: 指定工序不存在");

        var deviceValid = await deviceReadQueryService.ExistsInProcessAsync(
            request.DeviceId,
            request.ProcessId,
            cancellationToken);
        if (!deviceValid)
            return Result.Failure("配方创建失败: 指定设备不存在或不属于该工序");

        var duplicateExists = await recipeReadQueryService.VersionExistsAsync(
            request.ProcessId,
            request.DeviceId,
            recipeName,
            "V1.0",
            cancellationToken);
        if (duplicateExists)
            return Result.Failure($"配方创建失败: 已存在同名初始版本配方 [{recipeName}]");

        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
                userId,
                isAdmin: false,
                cancellationToken);
            if (accessibleDeviceIds is null || !accessibleDeviceIds.Contains(request.DeviceId))
                return Result.Failure("越权: 未授权该设备");
        }

        var recipe = new Recipe(recipeName, request.ProcessId, request.DeviceId, parametersJsonb);
        recipeRepository.Add(recipe);
        var affected = await recipeRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync(
                CacheKeys.RecipesByProcess(request.ProcessId), cancellationToken);
            await cacheService.RemoveAsync(
                CacheKeys.RecipesByDevice(request.DeviceId), cancellationToken);
        }

        return Result.Success(recipe.Id);
    }
}
