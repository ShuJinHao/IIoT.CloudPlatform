using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
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
    IRepository<Recipe> recipeRepository,
    IRecipeReadQueryService recipeReadQueryService,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService)
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

        var deviceAccess = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            source.DeviceId,
            cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return Result.Failure(deviceAccess.Errors?.ToArray() ?? ["越权: 当前账号无权操作该设备"]);
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

        return Result.Success(newRecipe.Id);
    }
}
