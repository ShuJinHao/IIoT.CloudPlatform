using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IIoT.ProductionService.Queries.Recipes;

/// <summary>
/// 纯净的配方详情 DTO (包含完整的 ParametersJsonb)
/// </summary>
public record RecipeDetailDto(
    Guid Id,
    string RecipeName,
    string Version,
    Guid ProcessId,
    Guid? DeviceId,
    string ParametersJsonb,
    string Status
);

/// <summary>
/// 交互查询：根据 ID 极速获取配方详情
/// </summary>
[AuthorizeRequirement("Recipe.Read")] // 🌟 第一道门：基础行为权限拦截
public record GetRecipeByIdQuery(Guid RecipeId) : IQuery<Result<RecipeDetailDto>>;

public class GetRecipeByIdHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository, // 拉取权限
    IReadRepository<Recipe> recipeRepository,     // 读仓储 (配合规约)
    ICacheService cacheService                    // 缓存抗压
) : IQueryHandler<GetRecipeByIdQuery, Result<RecipeDetailDto>>
{
    public async Task<Result<RecipeDetailDto>> Handle(GetRecipeByIdQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = $"iiot:recipe:v1:{request.RecipeId}";
        RecipeDetailDto? dto = null;

        // ==========================================
        // 🌟 1. 读链路：缓存绝对优先 (Cache-Aside 模式)
        // ==========================================
        dto = await cacheService.GetAsync<RecipeDetailDto>(cacheKey, cancellationToken);

        if (dto == null)
        {
            // 缓存未命中，使用第一阶段定义的专属规约图纸查库 (彻底告别 LINQ)
            var spec = new RecipeByIdSpec(request.RecipeId);
            var recipe = await recipeRepository.GetSingleOrDefaultAsync(spec, cancellationToken);

            if (recipe == null) return Result.Failure("获取失败：未找到该配方档案或已被停用");

            // 领域实体转 DTO
            dto = new RecipeDetailDto(
                recipe.Id,
                recipe.RecipeName,
                recipe.Version,
                recipe.ProcessId,
                recipe.DeviceId,
                recipe.ParametersJsonb,
                recipe.Status.ToString()
            );

            // 写入缓存，防击穿 (设置 2 小时过期，或结合业务调整)
            await cacheService.SetAsync(cacheKey, dto, TimeSpan.FromHours(2), cancellationToken);
        }

        // ==========================================
        // 🌟 2. 第二道门：内存级双维数据管辖权绝对拦截 (ABAC)
        // ==========================================
        // 🚨 警告：无论数据是从 DB 来的还是 Redis 来的，都必须在这里接受审判！
        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId)) return Result.Failure("用户凭证异常");

            var employeeSpec = new EmployeeWithAccessesSpec(userId);
            var employee = await employeeRepository.GetSingleOrDefaultAsync(employeeSpec, cancellationToken);

            if (employee == null) return Result.Failure("系统中未找到您的员工档案");

            // 🌟 利用 DTO 中的物理归属标识，动态判定校验规则
            if (dto.DeviceId.HasValue)
            {
                // 特调配方：校验具体机台管辖权
                var hasDeviceAccess = employee.DeviceAccesses.Any(d => d.DeviceId == dto.DeviceId.Value);
                if (!hasDeviceAccess) return Result.Failure("越权警告：您无权查看其他车间/机台的专属特调配方机密！");
            }
            else
            {
                // 通用配方：校验工序管辖权
                var hasProcessAccess = employee.ProcessAccesses.Any(p => p.ProcessId == dto.ProcessId);
                if (!hasProcessAccess) return Result.Failure("越权警告：您无权查看其他工序的通用配方机密！");
            }
        }

        return Result.Success(dto);
    }
}