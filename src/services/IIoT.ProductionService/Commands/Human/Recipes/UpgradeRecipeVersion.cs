using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.Authorization;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Recipes;

[AuthorizeRequirement("Recipe.Update")]
[DistributedLock("iiot:lock:recipe-upgrade:{SourceRecipeId}", TimeoutSeconds = 5)]
public record UpgradeRecipeVersionCommand(
    Guid SourceRecipeId,
    string NewVersion,
    string ParametersJsonb
) : IHumanCommand<Result<Guid>>;

public class UpgradeRecipeVersionHandler(
    ICurrentUser currentUser,
    IRepository<Recipe> recipeRepository,
    IRecipeReadQueryService recipeReadQueryService,
    ICacheService cacheService,
    IDevicePermissionService devicePermissionService)
    : ICommandHandler<UpgradeRecipeVersionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        UpgradeRecipeVersionCommand request,
        CancellationToken cancellationToken)
    {
        var newVersion = request.NewVersion?.Trim() ?? string.Empty;
        var parametersJsonb = request.ParametersJsonb?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(newVersion))
            return Result.Failure("版本号不能为空");
        if (string.IsNullOrEmpty(parametersJsonb))
            return Result.Failure("配方参数不能为空");

        var source = await recipeRepository.GetSingleOrDefaultAsync(
            new RecipeByIdSpec(request.SourceRecipeId),
            cancellationToken);

        if (source is null)
            return Result.Failure("升级失败: 源配方不存在");

        if (!string.Equals(
                currentUser.Role,
                IIoT.Services.Common.Contracts.Authorization.SystemRoles.Admin,
                StringComparison.Ordinal))
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
                userId,
                isAdmin: false,
                cancellationToken);
            if (accessibleDeviceIds is null || !accessibleDeviceIds.Contains(source.DeviceId))
                return Result.Failure("越权: 当前账号无权操作该设备");
        }

        var duplicateExists = await recipeReadQueryService.VersionExistsAsync(
            source.ProcessId,
            source.DeviceId,
            source.RecipeName,
            newVersion,
            cancellationToken);

        if (duplicateExists)
            return Result.Failure($"升级失败: 版本号 [{newVersion}] 已存在");

        var activeVersions = await recipeRepository.GetListAsync(
            new RecipeActiveVersionsSpec(source.RecipeName, source.ProcessId, source.DeviceId),
            cancellationToken);

        foreach (var active in activeVersions)
        {
            active.Archive();
            recipeRepository.Update(active);
        }

        var newRecipe = source.CreateNextVersion(newVersion, parametersJsonb);
        recipeRepository.Add(newRecipe);

        await recipeRepository.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveAsync(CacheKeys.Recipe(source.Id), cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.RecipesByProcess(source.ProcessId), cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.RecipesByDevice(source.DeviceId), cancellationToken);

        return Result.Success(newRecipe.Id);
    }
}
