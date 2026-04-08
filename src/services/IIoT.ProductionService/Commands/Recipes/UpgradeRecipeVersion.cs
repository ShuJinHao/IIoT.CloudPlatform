using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Recipes;

/// <summary>
/// 业务指令:升级配方版本。
/// 基于指定的旧版本派生新版本,同名同工序同设备下原有的 Active 版本会被自动归档。
/// </summary>
[AuthorizeRequirement("Recipe.Update")]
[DistributedLock("iiot:lock:recipe-upgrade:{SourceRecipeId}", TimeoutSeconds = 5)]
public record UpgradeRecipeVersionCommand(
    Guid SourceRecipeId,
    string NewVersion,
    string ParametersJsonb
) : ICommand<Result<Guid>>;

public class UpgradeRecipeVersionHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IRepository<Recipe> recipeRepository,
    ICacheService cacheService
) : ICommandHandler<UpgradeRecipeVersionCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        UpgradeRecipeVersionCommand request,
        CancellationToken cancellationToken)
    {
        var newVersion = request.NewVersion?.Trim() ?? string.Empty;
        var parametersJsonb = request.ParametersJsonb?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(newVersion))
            return Result.Failure("新版本号不能为空");
        if (string.IsNullOrEmpty(parametersJsonb))
            return Result.Failure("配方参数不能为空");

        // 1. 取源版本
        var source = await recipeRepository.GetSingleOrDefaultAsync(
            new RecipeByIdSpec(request.SourceRecipeId),
            cancellationToken);

        if (source is null)
            return Result.Failure("升级失败:源配方不存在");

        // 2. ABAC 权限校验
        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(userId),
                cancellationToken);

            if (employee is null)
                return Result.Failure("系统中未找到您的员工档案");

            if (source.DeviceId.HasValue)
            {
                var hasDeviceAccess = employee.DeviceAccesses
                    .Any(d => d.DeviceId == source.DeviceId.Value);
                if (!hasDeviceAccess)
                    return Result.Failure("越权:您没有该机台的管辖权");
            }
            else
            {
                var hasProcessAccess = employee.ProcessAccesses
                    .Any(p => p.ProcessId == source.ProcessId);
                if (!hasProcessAccess)
                    return Result.Failure("越权:您没有该工序的管辖权");
            }
        }

        // 3. 版本号防重(同名同工序同设备下不能存在相同版本号)
        var duplicateExists = await recipeRepository.AnyAsync(
            r => r.RecipeName == source.RecipeName
              && r.ProcessId == source.ProcessId
              && r.DeviceId == source.DeviceId
              && r.Version == newVersion,
            cancellationToken);

        if (duplicateExists)
            return Result.Failure($"升级失败:版本号 [{newVersion}] 已存在");

        // 4. 归档同名同工序同设备下所有当前 Active 版本
        var activeVersions = await recipeRepository.GetListAsync(
            new RecipeActiveVersionsSpec(source.RecipeName, source.ProcessId, source.DeviceId),
            cancellationToken);

        foreach (var active in activeVersions)
        {
            active.Archive();
            recipeRepository.Update(active);
        }

        // 5. 基于源版本派生新版本(领域行为方法)
        var newRecipe = source.CreateNextVersion(newVersion, parametersJsonb);
        recipeRepository.Add(newRecipe);

        await recipeRepository.SaveChangesAsync(cancellationToken);

        // 6. 缓存爆破
        await cacheService.RemoveAsync($"iiot:recipe:v1:{source.Id}", cancellationToken);
        await cacheService.RemoveAsync(
            $"iiot:recipes:process:v1:{source.ProcessId}", cancellationToken);
        if (source.DeviceId.HasValue)
        {
            await cacheService.RemoveAsync(
                $"iiot:recipes:device:v1:{source.DeviceId.Value}", cancellationToken);
        }

        return Result.Success(newRecipe.Id);
    }
}