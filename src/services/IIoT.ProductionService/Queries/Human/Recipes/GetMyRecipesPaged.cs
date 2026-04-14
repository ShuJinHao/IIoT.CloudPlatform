using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Recipes;

public record RecipeListItemDto(
    Guid Id,
    string RecipeName,
    string Version,
    Guid ProcessId,
    Guid DeviceId,
    string Status
);

[AuthorizeRequirement("Recipe.Read")]
public record GetMyRecipesPagedQuery(
    Pagination PaginationParams,
    string? Keyword = null
) : IHumanQuery<Result<PagedList<RecipeListItemDto>>>;

public class GetMyRecipesPagedHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IReadRepository<Recipe> recipeRepository
) : IQueryHandler<GetMyRecipesPagedQuery, Result<PagedList<RecipeListItemDto>>>
{
    public async Task<Result<PagedList<RecipeListItemDto>>> Handle(GetMyRecipesPagedQuery request, CancellationToken cancellationToken)
    {
        List<Guid>? allowedDeviceIds = null;

        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId)) return Result.Failure("用户凭证异常");

            var employeeSpec = new EmployeeWithAccessesSpec(userId);
            var employee = await employeeRepository.GetSingleOrDefaultAsync(employeeSpec, cancellationToken);

            if (employee == null) return Result.Failure("系统中未找到您的员工档案");

            allowedDeviceIds = employee.DeviceAccesses.Select(d => d.DeviceId).ToList();

            if (allowedDeviceIds.Count == 0)
            {
                var emptyList = new PagedList<RecipeListItemDto>([], 0, request.PaginationParams);
                return Result.Success(emptyList);
            }
        }

        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var take = request.PaginationParams.PageSize;

        var pagedSpec = new RecipePagedSpec(skip, take, allowedDeviceIds, request.Keyword, isPaging: true);
        var countSpec = new RecipePagedSpec(0, 0, allowedDeviceIds, request.Keyword, isPaging: false);

        var totalCount = await recipeRepository.CountAsync(countSpec, cancellationToken);

        List<Recipe> list = [];
        if (totalCount > 0)
        {
            list = await recipeRepository.GetListAsync(pagedSpec, cancellationToken);
        }

        var dtos = list.Select(r => new RecipeListItemDto(
            r.Id,
            r.RecipeName,
            r.Version,
            r.ProcessId,
            r.DeviceId,
            r.Status.ToString()
        )).ToList();

        var pagedList = new PagedList<RecipeListItemDto>(dtos, totalCount, request.PaginationParams);

        return Result.Success(pagedList);
    }
}
