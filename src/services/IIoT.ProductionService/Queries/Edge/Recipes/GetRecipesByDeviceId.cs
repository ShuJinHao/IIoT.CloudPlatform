using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Recipes;

/// <summary>
/// 设备侧使用的配方详情。
/// </summary>
public record RecipeForDeviceDto(
    Guid Id,
    string RecipeName,
    string Version,
    Guid ProcessId,
    Guid DeviceId,
    string ParametersJsonb,
    string Status
);

/// <summary>
/// 查询设备可用的配方列表。
/// </summary>
public record GetRecipesByDeviceIdQuery(Guid DeviceId) : IDeviceQuery<Result<List<RecipeForDeviceDto>>>;

public class GetRecipesByDeviceIdHandler(
    IReadRepository<Recipe> recipeRepository,
    IDeviceReadQueryService deviceReadQueryService,
    ICacheService cacheService
) : IQueryHandler<GetRecipesByDeviceIdQuery, Result<List<RecipeForDeviceDto>>>
{
    public async Task<Result<List<RecipeForDeviceDto>>> Handle(
        GetRecipesByDeviceIdQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeys.RecipesByDevice(request.DeviceId);

        var dtos = await cacheService.GetOrSetAsync<List<RecipeForDeviceDto>>(
            cacheKey,
            async factoryCancellationToken =>
            {
                var deviceExists = await deviceReadQueryService.ExistsAsync(
                    request.DeviceId,
                    factoryCancellationToken);
                if (!deviceExists)
                    return null;

                var spec = new RecipeByDeviceIdSpec(request.DeviceId);
                var recipes = await recipeRepository.GetListAsync(spec, factoryCancellationToken);
                return recipes.Select(recipe => new RecipeForDeviceDto(
                    recipe.Id,
                    recipe.RecipeName,
                    recipe.Version,
                    recipe.ProcessId,
                    recipe.DeviceId,
                    recipe.ParametersJsonb,
                    recipe.Status.ToString()
                )).ToList();
            },
            static value => value is not null,
            TimeSpan.FromHours(2),
            cancellationToken);

        if (dtos is null)
            return Result.Failure("查询失败: 设备不存在或已停用");

        return Result.Success(dtos);
    }
}
