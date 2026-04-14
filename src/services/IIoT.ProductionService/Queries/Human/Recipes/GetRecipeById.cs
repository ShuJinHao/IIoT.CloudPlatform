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
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IReadRepository<Recipe> recipeRepository,
    ICacheService cacheService
) : IQueryHandler<GetRecipeByIdQuery, Result<RecipeDetailDto>>
{
    public async Task<Result<RecipeDetailDto>> Handle(GetRecipeByIdQuery request, CancellationToken cancellationToken)
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

        // ABAC 校验:无论数据来自 DB 还是缓存,都要在这里过一遍管辖权
        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId)) return Result.Failure("用户凭证异常");

            var employeeSpec = new EmployeeWithAccessesSpec(userId);
            var employee = await employeeRepository.GetSingleOrDefaultAsync(employeeSpec, cancellationToken);

            if (employee == null) return Result.Failure("系统中未找到您的员工档案");

            var hasDeviceAccess = employee.DeviceAccesses.Any(d => d.DeviceId == dto.DeviceId);
            if (!hasDeviceAccess) return Result.Failure("无权查看该配方");
        }

        return Result.Success(dto);
    }
}
