using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
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
        DevicePermissions.Read, DevicePermissions.Create, DevicePermissions.Update,
        DevicePermissions.Delete, DevicePermissions.CascadeDelete,

        // 上位机 PLC 状态
        EdgeHostPermissions.Read,

        // 配方管理
        "Recipe.Read", "Recipe.Create", "Recipe.Update", "Recipe.Delete",

        // 角色管理
        "Role.Read", "Role.Define", "Role.Update",

        // 客户端发布
        ClientReleasePermissions.Read,
        ClientReleasePermissions.GenerateInstaller,
        ClientReleasePermissions.Publish,
        ClientReleasePermissions.Manage,

        // AI 只读接口
        AiReadPermissions.Device, AiReadPermissions.Process, AiReadPermissions.ClientRelease, AiReadPermissions.DeviceClientState,
        AiReadPermissions.Capacity, AiReadPermissions.DeviceLog, AiReadPermissions.ProductionRecord,
        AiReadPermissions.IdentityStatus,

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
        var grouped = await cacheService.GetOrSetAsync<List<PermissionGroupDto>>(
            CacheKeys.AllDefinedPermissions(),
            async factoryCancellationToken =>
            {
                factoryCancellationToken.ThrowIfCancellationRequested();
                var allRoles = await rolePolicyService.GetAllRolesAsync();
                var discoveredPermissions = new HashSet<string>();

                foreach (var roleName in allRoles)
                {
                    factoryCancellationToken.ThrowIfCancellationRequested();
                    var permissions = await rolePolicyService.GetRolePermissionsAsync(roleName);
                    if (permissions is not null)
                    {
                        foreach (var permission in permissions)
                            discoveredPermissions.Add(permission);
                    }
                }

                foreach (var permission in BuiltInPermissions)
                    discoveredPermissions.Add(permission);

                return discoveredPermissions
                    .OrderBy(permission => permission)
                    .GroupBy(permission => permission.Contains('.') ? permission.Split('.')[0] : "Other")
                    .Select(group => new PermissionGroupDto(group.Key, group.ToList()))
                    .OrderBy(group => group.GroupName)
                    .ToList();
            },
            static value => value is not null,
            TimeSpan.FromHours(4),
            cancellationToken);

        return Result.Success(grouped
            ?? throw new InvalidOperationException("Permission cache factory returned null."));
    }
}
