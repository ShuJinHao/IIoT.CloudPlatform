using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IIoT.ProductionService.Commands.Recipes;

/// <summary>
/// 业务指令：停用/软删除配方 (保留追溯记录，但在前端列表和生产下发中将不可见)
/// </summary>
[AuthorizeRequirement("Recipe.Create")] // 🌟 第一道门：基础行为权限拦截 (假设前端叫删除，后端实为停用)
public record DeactivateRecipeCommand(Guid RecipeId) : ICommand<Result<bool>>;

public class DeactivateRecipeHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository, // 拉取管辖权
    IRepository<Recipe> recipeRepository,         // 实体变更仓储
    ICacheService cacheService                    // 缓存双杀爆破
) : ICommandHandler<DeactivateRecipeCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(DeactivateRecipeCommand request, CancellationToken cancellationToken)
    {
        // ==========================================
        // 🌟 1. 获取目标聚合根
        // ==========================================
        var recipe = await recipeRepository.GetByIdAsync(request.RecipeId, cancellationToken);
        if (recipe == null) return Result.Failure("操作失败：目标配方不存在");

        // 幂等性设计：如果已经停用，直接返回成功，不消耗数据库资源
        if (!recipe.IsActive) return Result.Success(true);

        // ==========================================
        // 🌟 2. 第二道门：动态双维数据管辖权绝对拦截 (ABAC)
        // ==========================================
        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId)) return Result.Failure("用户凭证异常");

            var employeeSpec = new EmployeeWithAccessesSpec(userId);
            var employee = await employeeRepository.GetSingleOrDefaultAsync(employeeSpec, cancellationToken);
            if (employee == null) return Result.Failure("系统中未找到您的员工档案");

            // 🌟 依然是坚不可摧的逻辑：根据配方自身属性判定校验目标
            if (recipe.DeviceId.HasValue)
            {
                // 特调配方 -> 校验具体机台管辖权
                var hasDeviceAccess = employee.DeviceAccesses.Any(d => d.DeviceId == recipe.DeviceId.Value);
                if (!hasDeviceAccess) return Result.Failure("越权警告：您没有该具体机台的管辖权，严禁停用此特调配方！");
            }
            else
            {
                // 通用配方 -> 校验所属工序管辖权
                var hasProcessAccess = employee.ProcessAccesses.Any(p => p.ProcessId == recipe.ProcessId);
                if (!hasProcessAccess) return Result.Failure("越权警告：您没有该工序的管辖权，严禁停用此通用配方！");
            }
        }

        // ==========================================
        // 🌟 3. 领域状态变更与持久化
        // ==========================================
        // 软删除：标记为不可用
        recipe.IsActive = false;

        recipeRepository.Update(recipe);
        var affected = await recipeRepository.SaveChangesAsync(cancellationToken);

        // ==========================================
        // 🌟 4. 缓存强一致性保障：三杀爆破！
        // ==========================================
        if (affected > 0)
        {
            // 停用的配方绝不能再被终端或列表命中
            await cacheService.RemoveAsync($"iiot:recipe:v1:{recipe.Id}", cancellationToken);
            await cacheService.RemoveAsync($"iiot:recipes:process:v1:{recipe.ProcessId}", cancellationToken);

            if (recipe.DeviceId.HasValue)
            {
                await cacheService.RemoveAsync($"iiot:recipes:device:v1:{recipe.DeviceId.Value}", cancellationToken);
            }
        }

        return Result.Success(true);
    }
}