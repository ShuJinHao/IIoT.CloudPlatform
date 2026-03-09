using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using System.Linq;

namespace IIoT.ProductionService.Commands;

/// <summary>
/// 更新工艺配方参数指令
/// </summary>
/// <param name="RecipeId">配方唯一标识</param>
/// <param name="NewParametersJsonb">新的 JSONB 参数字符串</param>
/// <param name="NewVersion">新的版本号</param>
// 🌟 【第一维度拦截】：校验是否有“Recipe.Update”系统操作权限
[AuthorizeRequirement("Recipe.Update")]
public record UpdateRecipeParametersCommand(
    Guid RecipeId,
    string NewParametersJsonb,
    string NewVersion) : ICommand<Result<bool>>;

/// <summary>
/// 配方参数更新处理器
/// </summary>
public class UpdateRecipeParametersHandler(
    ICurrentUser currentUser,                // 获取当前用户信息
    IReadRepository<Employee> employeeRepository, // 读取员工及其管辖权
    IRepository<Recipe> recipeRepository,   // 操作配方实体
    ICacheService cacheService)             // 🌟 新增：注入缓存服务
    : ICommandHandler<UpdateRecipeParametersCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateRecipeParametersCommand request, CancellationToken cancellationToken)
    {
        // 1. 获取目标配方实体
        var recipe = await recipeRepository.GetByIdAsync(request.RecipeId, cancellationToken);
        if (recipe == null) return Result.Failure("目标配方不存在");

        // ==========================================
        // 🌟 【第二维度拦截】：业务数据管辖权绝对拦截 (ABAC)
        // ==========================================

        // Admin 超级管理员拥有全厂所有设备/配方的管理权，直接跳过业务校验
        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户身份令牌(UserId)解析异常");

            // 🌟 核心：联级加载该员工的所有【工序管辖权】和【设备管辖权】
            var employee = await employeeRepository.GetAsync(
                expression: e => e.Id == userId,
                includes: [e => e.ProcessAccesses, e => e.DeviceAccesses],
                cancellationToken: cancellationToken);

            if (employee == null) return Result.Failure("系统中未找到该用户的员工档案，请先完善入职信息");

            // 根据配方的颗粒度执行分类拦截
            if (recipe.DeviceId.HasValue)
            {
                // 场景 A：该配方是某台机器的“特调配方”
                // 逻辑：校验员工的【设备管辖列表】里是否有这台机器
                var hasDeviceAccess = employee.DeviceAccesses.Any(da => da.DeviceId == recipe.DeviceId.Value);
                if (!hasDeviceAccess) return Result.Failure("越权警告：您没有该特定机台设备的管理权限");
            }
            else
            {
                // 场景 B：该配方是整个工序的“通用配方”
                // 逻辑：校验员工的【工序管辖列表】里是否有该工序
                var hasProcessAccess = employee.ProcessAccesses.Any(pa => pa.ProcessId == recipe.ProcessId);
                if (!hasProcessAccess) return Result.Failure("越权警告：您没有该工序通用配方的管理权限");
            }
        }
        // ================= 数据拦截结束 =================

        // 2. 执行领域逻辑：更新配方参数与版本
        recipe.UpdateParameters(request.NewParametersJsonb, request.NewVersion);

        // 3. 持久化到 PostgreSQL
        recipeRepository.Update(recipe);
        var dbResult = await recipeRepository.SaveChangesAsync(cancellationToken);

        // 4. 🌟 缓存强一致性保障：写后即删 (Cache-Aside Pattern)
        // 只要数据库保存成功，立刻爆破 Redis 中的旧数据
        if (dbResult > 0)
        {
            // 删除单体配方缓存
            await cacheService.RemoveAsync($"iiot:recipe:v1:{request.RecipeId}", cancellationToken);

            // 💡 进阶建议：通常配方列表也会被缓存，建议也清理相关的列表缓存键
            // 例如按工序缓存的列表：$"iiot:recipes:process:{recipe.ProcessId}"
            await cacheService.RemoveAsync($"iiot:recipes:process:v1:{recipe.ProcessId}", cancellationToken);
        }

        return Result.Success(true);
    }
}