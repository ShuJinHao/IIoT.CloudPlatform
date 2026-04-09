using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Recipes;

/// <summary>
/// 配方详情 DTO (包含完整的 ParametersJsonb,边缘端需要拿到完整参数)
/// </summary>
public record RecipeForDeviceDto(
    Guid Id,
    string RecipeName,
    string Version,
    Guid ProcessId,
    Guid? DeviceId,
    string ParametersJsonb,
    string Status
);

/// <summary>
/// 查询:根据设备ID获取该设备可用的配方列表
/// 边缘端 RecipeSyncTask 定时拉取时使用,不走员工 ABAC 权限校验
/// </summary>
public record GetRecipesByDeviceIdQuery(Guid DeviceId) : IQuery<Result<List<RecipeForDeviceDto>>>;

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
        var cacheKey = $"iiot:recipes:device:v1:{request.DeviceId}";

        // 1. 缓存优先
        var cached = await cacheService.GetAsync<List<RecipeForDeviceDto>>(cacheKey, cancellationToken);
        if (cached != null) return Result.Success(cached);

        // 2. 校验设备存在且激活,顺带拿到 ProcessId 喂给下一步的 Spec
        var device = await dataQueryService.FirstOrDefaultAsync(
            dataQueryService.Devices.Where(d => d.Id == request.DeviceId && d.IsActive));

        if (device is null)
            return Result.Failure("查询失败:设备不存在或已停用");

        // 3. 使用规约查询:专属特调配方 + 该工序通用配方
        var spec = new RecipeByDeviceIdSpec(request.DeviceId, device.ProcessId);
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

        // 4. 写入缓存 (2小时过期,与现有配方缓存策略一致)
        await cacheService.SetAsync(cacheKey, dtos, TimeSpan.FromHours(2), cancellationToken);

        return Result.Success(dtos);
    }
}