using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.CrossCutting.Caching.Options;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IIoT.EntityFrameworkCore.Identity;

/// <summary>
/// 用户权限提供器。
/// 负责把用户个人权限和角色权限合并成最终权限集合，并按用户维度写入缓存。
/// </summary>
public class PermissionProvider(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    ICacheService cacheService,
    IOptions<PermissionCacheOptions> options) : IPermissionProvider
{
    private readonly PermissionCacheOptions _options = options.Value;

    public async Task<IList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeys.PermissionByUser(userId);

        return await cacheService.GetOrSetAsync(
                   cacheKey,
                   async token =>
                   {
                       var user = await userManager.FindByIdAsync(userId.ToString());
                       if (user == null)
                       {
                           return [];
                       }

                       var allPermissions = new HashSet<string>();

                       var userClaims = await userManager.GetClaimsAsync(user);
                       foreach (var claim in userClaims.Where(c => c.Type == IIoTClaimTypes.Permission))
                       {
                           allPermissions.Add(claim.Value);
                       }

                       var roles = await userManager.GetRolesAsync(user);
                       foreach (var roleName in roles)
                       {
                           token.ThrowIfCancellationRequested();

                           var role = await roleManager.FindByNameAsync(roleName);
                           if (role == null)
                           {
                               continue;
                           }

                           var roleClaims = await roleManager.GetClaimsAsync(role);
                           foreach (var claim in roleClaims.Where(c => c.Type == IIoTClaimTypes.Permission))
                           {
                               allPermissions.Add(claim.Value);
                           }
                       }

                       return allPermissions.ToList();
                   },
                   _options.ResolveExpiration(),
                   cancellationToken)
               ?? [];
    }
}
