using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
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
    IDataQueryService dataQueryService,
    ICacheService cacheService
) : IQueryHandler<GetRecipesByDeviceIdQuery, Result<List<RecipeForDeviceDto>>>
{
    public async Task<Result<List<RecipeForDeviceDto>>> Handle(
        GetRecipesByDeviceIdQuery request,
        CancellationToken cancellationToken)
    {
        var cacheKey = CacheKeys.RecipesByDevice(request.DeviceId);

        var cached = await cacheService.GetAsync<List<RecipeForDeviceDto>>(cacheKey, cancellationToken);
        if (cached != null) return Result.Success(cached);

        var deviceExists = await dataQueryService.AnyAsync(
            dataQueryService.Devices.Where(d => d.Id == request.DeviceId));

        if (!deviceExists)
            return Result.Failure("查询失败:设备不存在或已停用");

        var spec = new RecipeByDeviceIdSpec(request.DeviceId);
        var recipes = await recipeRepository.GetListAsync(spec, cancellationToken);

        var dtos = recipes.Select(r => new RecipeForDeviceDto(
            r.Id,
            r.RecipeName,
            r.Version,
            r.ProcessId,
            r.DeviceId,
            r.ParametersJsonb,
            r.Status.ToString()
        )).ToList();

        await cacheService.SetAsync(cacheKey, dtos, TimeSpan.FromHours(2), cancellationToken);

        return Result.Success(dtos);
    }
}
