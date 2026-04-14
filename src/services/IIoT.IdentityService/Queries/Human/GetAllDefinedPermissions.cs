using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Queries;

public record PermissionGroupDto(
    string GroupName,
    List<string> Permissions
);

[AuthorizeRequirement("Role.Define")]
public record GetAllDefinedPermissionsQuery() : IHumanQuery<Result<List<PermissionGroupDto>>>;

public class GetAllDefinedPermissionsHandler(
    IRolePolicyService rolePolicyService,
    ICacheService cacheService
) : IQueryHandler<GetAllDefinedPermissionsQuery, Result<List<PermissionGroupDto>>>
{
    private static readonly List<string> BuiltInPermissions =
    [
        // 员工管理
        "Employee.Read", "Employee.Onboard", "Employee.Update",
        "Employee.UpdateAccess", "Employee.Deactivate", "Employee.Terminate",

        // 工序管理
        "Process.Read", "Process.Create", "Process.Update", "Process.Delete",

        // 设备管理
        "Device.Read", "Device.Create", "Device.Update", "Device.Delete",

        // 配方管理
        "Recipe.Read", "Recipe.Create", "Recipe.Update", "Recipe.Delete",

        // 角色管理
        "Role.Define", "Role.Update",

        // ── WPF 边缘端菜单权限 ──────────────────────────────────────
        // 硬件配置页（对应 WPF Permissions.HardwareConfig）
        "Hardware.Config",

        // 参数配置页（对应 WPF Permissions.ParamConfig）
        "Param.Config",
    ];

    public async Task<Result<List<PermissionGroupDto>>> Handle(
        GetAllDefinedPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        var cached = await cacheService.GetAsync<List<PermissionGroupDto>>(
            CacheKeys.AllDefinedPermissions(), cancellationToken);
        if (cached != null) return Result.Success(cached);

        var allRoles = await rolePolicyService.GetAllRolesAsync();
        var discoveredPermissions = new HashSet<string>();

        foreach (var roleName in allRoles)
        {
            var perms = await rolePolicyService.GetRolePermissionsAsync(roleName);
            if (perms != null)
                foreach (var p in perms)
                    discoveredPermissions.Add(p);
        }

        foreach (var p in BuiltInPermissions)
            discoveredPermissions.Add(p);

        var grouped = discoveredPermissions
            .OrderBy(p => p)
            .GroupBy(p => p.Contains('.') ? p.Split('.')[0] : "Other")
            .Select(g => new PermissionGroupDto(g.Key, g.ToList()))
            .OrderBy(g => g.GroupName)
            .ToList();

        await cacheService.SetAsync(
            CacheKeys.AllDefinedPermissions(),
            grouped,
            TimeSpan.FromHours(4),
            cancellationToken);

        return Result.Success(grouped);
    }
}
