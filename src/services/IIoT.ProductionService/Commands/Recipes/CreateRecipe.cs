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
/// 业务指令:创建新的生产配方(初始版本默认 V1.0)
/// </summary>
[AuthorizeRequirement("Recipe.Create")]
[DistributedLock("iiot:lock:recipe-create:{ProcessId}:{DeviceId}:{RecipeName}", TimeoutSeconds = 5)]
public record CreateRecipeCommand(
    string RecipeName,
    Guid ProcessId,
    Guid? DeviceId,
    string ParametersJsonb
) : ICommand<Result<Guid>>;

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

        // 校验 A:归属工序必须存在
        var processExists = await dataQueryService.AnyAsync(
            dataQueryService.MfgProcesses.Where(p => p.Id == request.ProcessId));

        if (!processExists)
            return Result.Failure("配方创建失败:指定的归属工序不存在");

        // 校验 B:如指定设备,该设备必须存在且属于当前工序
        if (request.DeviceId.HasValue)
        {
            var deviceValid = await dataQueryService.AnyAsync(
                dataQueryService.Devices.Where(d =>
                    d.Id == request.DeviceId.Value && d.ProcessId == request.ProcessId));

            if (!deviceValid)
                return Result.Failure("配方创建失败:指定的机台不存在或不属于当前工序");
        }

        // 校验 C:防重 — 同工序、同设备、同名的 V1.0 初始版本不能重复
        var duplicateExists = await dataQueryService.AnyAsync(
            dataQueryService.Recipes.Where(r =>
                r.ProcessId == request.ProcessId
             && r.DeviceId == request.DeviceId
             && r.RecipeName == recipeName
             && r.Version == "V1.0"));

        if (duplicateExists)
            return Result.Failure($"配方创建失败:已存在同名的 V1.0 初始版本配方 [{recipeName}]");

        // ABAC:非 Admin 的双维管辖权校验
        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(userId),
                cancellationToken);

            if (employee is null)
                return Result.Failure("系统中未找到您的员工档案");

            if (request.DeviceId.HasValue)
            {
                var hasDeviceAccess = employee.DeviceAccesses
                    .Any(d => d.DeviceId == request.DeviceId.Value);
                if (!hasDeviceAccess)
                    return Result.Failure("越权:您没有该具体机台的管辖权,无法创建专属配方");
            }
            else
            {
                var hasProcessAccess = employee.ProcessAccesses
                    .Any(p => p.ProcessId == request.ProcessId);
                if (!hasProcessAccess)
                    return Result.Failure("越权:您没有该工序的管辖权,无法创建通用配方");
            }
        }

        var recipe = new Recipe(recipeName, request.ProcessId, parametersJsonb, request.DeviceId);

        recipeRepository.Add(recipe);
        var affected = await recipeRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync(
                $"iiot:recipes:process:v1:{request.ProcessId}", cancellationToken);
            if (request.DeviceId.HasValue)
            {
                await cacheService.RemoveAsync(
                    $"iiot:recipes:device:v1:{request.DeviceId.Value}", cancellationToken);
            }
        }

        return Result.Success(recipe.Id);
    }
}