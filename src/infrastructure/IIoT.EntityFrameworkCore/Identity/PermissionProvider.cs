using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Caching.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IIoT.EntityFrameworkCore.Identity;

public class PermissionProvider(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,  // 必须是 IdentityRole<Guid>
    ICacheService cacheService,
    IOptions<PermissionCacheOptions> options) : IPermissionProvider
{
    private readonly PermissionCacheOptions _options = options.Value;

    public async Task<IList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // 缓存 key 按用户 ID 隔离
        var cacheKey = $"{_options.KeyPrefix}user:{userId}";

        // 1. 读缓存
        var cachedPermissions = await cacheService.GetAsync<List<string>>(cacheKey, cancellationToken);
        if (cachedPermissions != null) return cachedPermissions;

        // 2. DB 兜底：先找人
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user == null) return [];

        // HashSet 去重合并
        var allPermissions = new HashSet<string>();

        // 【维度 A】：获取该用户被“特批”的个人专属权限 (AspNetUserClaims 表)
        var userClaims = await userManager.GetClaimsAsync(user);
        foreach (var claim in userClaims.Where(c => c.Type == "Permission"))
        {
            allPermissions.Add(claim.Value);
        }

        // 【维度 B】：获取该用户所属的所有角色，并将这些角色的权限一并拉取 (AspNetRoleClaims 表)
        var roles = await userManager.GetRolesAsync(user);
        foreach (var roleName in roles)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role != null)
            {
                var roleClaims = await roleManager.GetClaimsAsync(role);
                foreach (var claim in roleClaims.Where(c => c.Type == "Permission"))
                {
                    allPermissions.Add(claim.Value);
                }
            }
        }

        var permissionsList = allPermissions.ToList();

        // 3. 回写缓存
        var expireTime = TimeSpan.FromHours(_options.ExpirationHours);
        await cacheService.SetAsync(cacheKey, permissionsList, expireTime, cancellationToken);

        return permissionsList;
    }
}
