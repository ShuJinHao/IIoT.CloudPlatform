using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Queries;

public record PermissionGroupDto(
    string GroupName,
    List<string> Permissions
);

[AuthorizeRequirement("Role.Define")]
public record GetAllDefinedPermissionsQuery() : IQuery<Result<List<PermissionGroupDto>>>;

public class GetAllDefinedPermissionsHandler(
    IRolePolicyService rolePolicyService,
    ICacheService cacheService
) : IQueryHandler<GetAllDefinedPermissionsQuery, Result<List<PermissionGroupDto>>>
{
    private const string CacheKey = "iiot:permissions:v1:all-defined";

    private static readonly List<string> BuiltInPermissions =
    [
        "Employee.Read", "Employee.Onboard", "Employee.Update",
        "Employee.UpdateAccess", "Employee.Deactivate", "Employee.Terminate",
        "Process.Read", "Process.Create", "Process.Update", "Process.Delete",
        "Device.Read", "Device.Create", "Device.Update", "Device.Deactivate",
        "Recipe.Read", "Recipe.Create", "Recipe.Update",
        "Role.Define", "Role.Update",
    ];

    public async Task<Result<List<PermissionGroupDto>>> Handle(GetAllDefinedPermissionsQuery request, CancellationToken cancellationToken)
    {
        var cached = await cacheService.GetAsync<List<PermissionGroupDto>>(CacheKey, cancellationToken);
        if (cached != null) return Result.Success(cached);

        var allRoles = await rolePolicyService.GetAllRolesAsync();
        var discoveredPermissions = new HashSet<string>();

        foreach (var roleName in allRoles)
        {
            var perms = await rolePolicyService.GetRolePermissionsAsync(roleName);
            if (perms != null) foreach (var p in perms) discoveredPermissions.Add(p);
        }

        foreach (var p in BuiltInPermissions) discoveredPermissions.Add(p);

        var grouped = discoveredPermissions
            .OrderBy(p => p)
            .GroupBy(p => p.Contains('.') ? p.Split('.')[0] : "Other")
            .Select(g => new PermissionGroupDto(g.Key, g.ToList()))
            .OrderBy(g => g.GroupName)
            .ToList();

        await cacheService.SetAsync(CacheKey, grouped, TimeSpan.FromHours(4), cancellationToken);

        return Result.Success(grouped);
    }
}
