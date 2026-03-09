using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands;

// 🌟 【第一维度拦截】：校验是否有“Recipe.Create”权限
[AuthorizeRequirement("Recipe.Create")]
public record CreateRecipeCommand(
    string RecipeName,
    Guid ProcessId,
    string ParametersJsonb,
    Guid? DeviceId = null) : ICommand<Result<Guid>>;

public class CreateRecipeHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IRepository<Recipe> recipeRepository,
    ICacheService cacheService) : ICommandHandler<CreateRecipeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateRecipeCommand request, CancellationToken cancellationToken)
    {
        // 🌟 【第二维度拦截】：校验管辖权
        if (currentUser.Role != "Admin")
        {
            var userId = Guid.Parse(currentUser.Id!);
            var employee = await employeeRepository.GetAsync(e => e.Id == userId, [e => e.ProcessAccesses, e => e.DeviceAccesses], cancellationToken);

            if (employee == null) return Result.Failure("未找到员工档案");

            // 逻辑：如果要建的是机台特调配方，看机台管辖权；否则看工序管辖权
            bool hasAccess = request.DeviceId.HasValue
                ? employee.DeviceAccesses.Any(da => da.DeviceId == request.DeviceId.Value)
                : employee.ProcessAccesses.Any(pa => pa.ProcessId == request.ProcessId);

            if (!hasAccess) return Result.Failure("越权操作：您没有该范围的配方创建权限");
        }

        // 1. 创建领域对象
        var recipe = new Recipe(request.RecipeName, request.ProcessId, request.ParametersJsonb, request.DeviceId);

        // 2. 落库
        recipeRepository.Add(recipe);
        await recipeRepository.SaveChangesAsync(cancellationToken);

        // 3. 🌟 缓存清理：由于新增了配方，该工序下的“列表缓存”已失效，必须踢掉
        await cacheService.RemoveAsync($"iiot:recipes:process:v1:{request.ProcessId}", cancellationToken);

        return Result.Success(recipe.Id);
    }
}