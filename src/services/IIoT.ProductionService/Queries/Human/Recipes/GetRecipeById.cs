using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Recipes;

/// <summary>
/// 配方详情 DTO(包含完整 ParametersJsonb)
/// </summary>
public record RecipeDetailDto(
    Guid Id,
    string RecipeName,
    string Version,
    Guid ProcessId,
    Guid DeviceId,
    string ParametersJsonb,
    string Status
);

/// <summary>
/// 根据 ID 获取配方详情。
/// </summary>
[AuthorizeRequirement("Recipe.Read")]
public record GetRecipeByIdQuery(Guid RecipeId) : IHumanQuery<Result<RecipeDetailDto>>;

public class GetRecipeByIdHandler(
    IReadRepository<Recipe> recipeRepository,
    ICacheService cacheService,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService
) : IQueryHandler<GetRecipeByIdQuery, Result<RecipeDetailDto>>
{
    public async Task<Result<RecipeDetailDto>> Handle(
        GetRecipeByIdQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeys.Recipe(request.RecipeId);

        // Cache-Aside:先查缓存,未命中再走 Spec 查库并回填
        var dto = await cacheService.GetAsync<RecipeDetailDto>(cacheKey, cancellationToken);

        if (dto == null)
        {
            var spec = new RecipeByIdSpec(request.RecipeId);
            var recipe = await recipeRepository.GetSingleOrDefaultAsync(spec, cancellationToken);

            if (recipe == null) return Result.Failure("获取失败:未找到该配方档案或已被停用");

            dto = new RecipeDetailDto(
                recipe.Id,
                recipe.RecipeName,
                recipe.Version,
                recipe.ProcessId,
                recipe.DeviceId,
                recipe.ParametersJsonb,
                recipe.Status.ToString()
            );

            await cacheService.SetAsync(cacheKey, dto, TimeSpan.FromHours(2), cancellationToken);
        }

        var deviceAccess = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            dto.DeviceId,
            cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return Result.Failure(deviceAccess.Errors?.ToArray() ?? ["无权查看该配方"]);
        }

        return Result.Success(dto);
    }
}
