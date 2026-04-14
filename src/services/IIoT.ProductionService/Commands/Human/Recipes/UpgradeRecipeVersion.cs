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

namespace IIoT.ProductionService.Commands.Recipes;

[AuthorizeRequirement("Recipe.Update")]
[DistributedLock("iiot:lock:recipe-upgrade:{SourceRecipeId}", TimeoutSeconds = 5)]
public record UpgradeRecipeVersionCommand(
    Guid SourceRecipeId,
    string NewVersion,
    string ParametersJsonb
) : IHumanCommand<Result<Guid>>;

public class UpgradeRecipeVersionHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IRepository<Recipe> recipeRepository,
    IDataQueryService dataQueryService,
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

        var source = await recipeRepository.GetSingleOrDefaultAsync(
            new RecipeByIdSpec(request.SourceRecipeId),
            cancellationToken);

        if (source is null)
            return Result.Failure("升级失败:源配方不存在");

        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(userId),
                cancellationToken);

            if (employee is null)
                return Result.Failure("系统中未找到您的员工档案");

            var hasDeviceAccess = employee.DeviceAccesses
                .Any(d => d.DeviceId == source.DeviceId);
            if (!hasDeviceAccess)
                return Result.Failure("越权:您没有该机台的管辖权");
        }

        var duplicateExists = await dataQueryService.AnyAsync(
            dataQueryService.Recipes.Where(r =>
                r.RecipeName == source.RecipeName
             && r.ProcessId == source.ProcessId
             && r.DeviceId == source.DeviceId
             && r.Version == newVersion));

        if (duplicateExists)
            return Result.Failure($"升级失败:版本号 [{newVersion}] 已存在");

        var activeVersions = await recipeRepository.GetListAsync(
            new RecipeActiveVersionsSpec(source.RecipeName, source.ProcessId, source.DeviceId),
            cancellationToken);

        foreach (var active in activeVersions)
        {
            active.Archive();
            recipeRepository.Update(active);
        }

        var newRecipe = source.CreateNextVersion(newVersion, parametersJsonb);
        recipeRepository.Add(newRecipe);

        await recipeRepository.SaveChangesAsync(cancellationToken);

        await cacheService.RemoveAsync(CacheKeys.Recipe(source.Id), cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.RecipesByProcess(source.ProcessId), cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.RecipesByDevice(source.DeviceId), cancellationToken);

        return Result.Success(newRecipe.Id);
    }
}
