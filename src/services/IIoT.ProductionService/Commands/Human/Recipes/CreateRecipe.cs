using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Recipes;

[AuthorizeRequirement("Recipe.Create")]
[DistributedLock("iiot:lock:recipe-create:{ProcessId}:{DeviceId}:{RecipeName}", TimeoutSeconds = 5)]
public record CreateRecipeCommand(
    string RecipeName,
    Guid ProcessId,
    Guid DeviceId,
    string ParametersJsonb
) : IHumanCommand<Result<Guid>>;

public class CreateRecipeHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IRepository<Recipe> recipeRepository,
    IDataQueryService dataQueryService,
    ICacheService cacheService
) : ICommandHandler<CreateRecipeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateRecipeCommand request,
        CancellationToken cancellationToken)
    {
        var recipeName = request.RecipeName?.Trim() ?? string.Empty;
        var parametersJsonb = request.ParametersJsonb?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(recipeName))
            return Result.Failure("配方名称不能为空");
        if (string.IsNullOrEmpty(parametersJsonb))
            return Result.Failure("配方参数不能为空");
        if (request.ProcessId == Guid.Empty)
            return Result.Failure("归属工序不能为空");
        if (request.DeviceId == Guid.Empty)
            return Result.Failure("归属设备不能为空");

        var processExists = await dataQueryService.AnyAsync(
            dataQueryService.MfgProcesses.Where(p => p.Id == request.ProcessId));

        if (!processExists)
            return Result.Failure("配方创建失败:指定的归属工序不存在");

        var deviceValid = await dataQueryService.AnyAsync(
            dataQueryService.Devices.Where(d =>
                d.Id == request.DeviceId && d.ProcessId == request.ProcessId));

        if (!deviceValid)
            return Result.Failure("配方创建失败:指定的机台不存在或不属于当前工序");

        var duplicateExists = await dataQueryService.AnyAsync(
            dataQueryService.Recipes.Where(r =>
                r.ProcessId == request.ProcessId
             && r.DeviceId == request.DeviceId
             && r.RecipeName == recipeName
             && r.Version == "V1.0"));

        if (duplicateExists)
            return Result.Failure($"配方创建失败:已存在同名的 V1.0 初始版本配方 [{recipeName}]");

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
                .Any(d => d.DeviceId == request.DeviceId);
            if (!hasDeviceAccess)
                return Result.Failure("越权:您没有该设备的管辖权");
        }

        var recipe = new Recipe(recipeName, request.ProcessId, parametersJsonb, request.DeviceId);

        recipeRepository.Add(recipe);
        var affected = await recipeRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync(
                CacheKeys.RecipesByProcess(request.ProcessId), cancellationToken);
            await cacheService.RemoveAsync(
                CacheKeys.RecipesByDevice(request.DeviceId), cancellationToken);
        }

        return Result.Success(recipe.Id);
    }
}
