using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using System.Collections.Generic;

namespace IIoT.ProductionService.Queries;

[AuthorizeRequirement("Recipe.Read")]
public record GetUserRecipesQuery() : IQuery<Result<List<Recipe>>>;

public class GetUserRecipesHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IReadRepository<Recipe> recipeRepository,
    ICacheService cacheService) : IQueryHandler<GetUserRecipesQuery, Result<List<Recipe>>>
{
    public async Task<Result<List<Recipe>>> Handle(GetUserRecipesQuery request, CancellationToken cancellationToken)
    {
        var userIdStr = currentUser.Id!;
        var cacheKey = $"iiot:recipes:user:v1:{userIdStr}";

        // 1. 🌟 读链路第一步：尝试从缓存读取
        var cachedRecipes = await cacheService.GetAsync<List<Recipe>>(cacheKey, cancellationToken);
        if (cachedRecipes != null) return Result.Success(cachedRecipes);

        // 2. 🌟 读链路第二步：缓存未命中，进行带权限过滤的数据库查询
        List<Recipe> recipes;

        if (currentUser.Role == "Admin")
        {
            // 超级管理员：查询全厂所有配方
            recipes = await recipeRepository.GetListAsync(r => r.IsActive, cancellationToken);
        }
        else
        {
            var userId = Guid.Parse(userIdStr);
            var employee = await employeeRepository.GetAsync(e => e.Id == userId, [e => e.ProcessAccesses, e => e.DeviceAccesses], cancellationToken);
            if (employee == null) return Result.Failure("未找到员工档案");

            // 提取用户的管辖 ID 集合
            var processIds = employee.ProcessAccesses.Select(p => p.ProcessId).ToList();
            var deviceIds = employee.DeviceAccesses.Select(d => d.DeviceId).ToList();

            // 🌟 核心过滤逻辑：
            // A. 如果是通用配方(DeviceId为null)，必须在员工的工序管辖范围内。
            // B. 如果是特调配方(DeviceId有值)，必须在员工的设备管辖范围内。
            recipes = await recipeRepository.GetListAsync(r => r.IsActive && (
                (r.DeviceId == null && processIds.Contains(r.ProcessId)) ||
                (r.DeviceId != null && deviceIds.Contains(r.DeviceId.Value))
            ), cancellationToken);
        }

        // 3. 🌟 写入缓存：下次查询直接命中
        await cacheService.SetAsync(cacheKey, recipes, TimeSpan.FromHours(2), cancellationToken);

        return Result.Success(recipes);
    }
}