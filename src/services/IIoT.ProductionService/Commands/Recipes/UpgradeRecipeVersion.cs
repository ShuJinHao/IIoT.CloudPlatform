using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Recipes;

/// <summary>
/// 业务指令：升级配方版本
/// 基于指定的旧版本创建新版本，旧版本自动归档
/// </summary>
[AuthorizeRequirement("Recipe.Update")]
public record UpgradeRecipeVersionCommand(
    Guid SourceRecipeId,
    string NewVersion,
    string ParametersJsonb
) : ICommand<Result<Guid>>;

public class UpgradeRecipeVersionHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IDataQueryService dataQueryService,
    IRepository<Recipe> recipeRepository,
    ICacheService cacheService
) : ICommandHandler<UpgradeRecipeVersionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(UpgradeRecipeVersionCommand request, CancellationToken cancellationToken)
    {
        // 1. 获取源版本
        var source = await recipeRepository.GetByIdAsync(request.SourceRecipeId, cancellationToken);
        if (source == null) return Result.Failure("升级失败：源配方不存在");

        // 2. ABAC 权限校验
        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId)) return Result.Failure("用户凭证异常");

            var employeeSpec = new EmployeeWithAccessesSpec(userId);
            var employee = await employeeRepository.GetSingleOrDefaultAsync(employeeSpec, cancellationToken);
            if (employee == null) return Result.Failure("系统中未找到您的员工档案");

            if (source.DeviceId.HasValue)
            {
                var hasDeviceAccess = employee.DeviceAccesses.Any(d => d.DeviceId == source.DeviceId.Value);
                if (!hasDeviceAccess) return Result.Failure("越权警告：您没有该机台的管辖权！");
            }
            else
            {
                var hasProcessAccess = employee.ProcessAccesses.Any(p => p.ProcessId == source.ProcessId);
                if (!hasProcessAccess) return Result.Failure("越权警告：您没有该工序的管辖权！");
            }
        }

        // 3. 版本号防重
        var duplicateExists = await dataQueryService.AnyAsync(
            dataQueryService.Recipes.Where(r =>
                r.RecipeName == source.RecipeName &&
                r.ProcessId == source.ProcessId &&
                r.DeviceId == source.DeviceId &&
                r.Version == request.NewVersion)
        );
        if (duplicateExists)
            return Result.Failure($"升级失败：版本号 {request.NewVersion} 已存在");

        // 4. 归档当前启用版本（同名同工序同设备下所有 Active 的都归档）
        var activeRecipes = await dataQueryService.ToListAsync(
            dataQueryService.Recipes.Where(r =>
                r.RecipeName == source.RecipeName &&
                r.ProcessId == source.ProcessId &&
                r.DeviceId == source.DeviceId &&
                r.Status == RecipeStatus.Active)
        );

        foreach (var active in activeRecipes)
        {
            var entity = await recipeRepository.GetByIdAsync(active.Id, cancellationToken);
            if (entity != null)
            {
                entity.Archive();
                recipeRepository.Update(entity);
            }
        }

        // 5. 创建新版本
        var newRecipe = new Recipe(
            source.RecipeName,
            source.ProcessId,
            request.ParametersJsonb,
            source.DeviceId);

        // 覆盖默认的 V1.0 为指定版本号
        newRecipe.Version = request.NewVersion;

        recipeRepository.Add(newRecipe);
        await recipeRepository.SaveChangesAsync(cancellationToken);

        // 6. 缓存爆破
        await cacheService.RemoveAsync($"iiot:recipe:v1:{source.Id}", cancellationToken);
        await cacheService.RemoveAsync($"iiot:recipes:process:v1:{source.ProcessId}", cancellationToken);
        if (source.DeviceId.HasValue)
        {
            await cacheService.RemoveAsync($"iiot:recipes:device:v1:{source.DeviceId.Value}", cancellationToken);
        }

        return Result.Success(newRecipe.Id);
    }
}