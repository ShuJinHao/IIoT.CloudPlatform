using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Identity;

namespace IIoT.EntityFrameworkCore.Identity;

/// <summary>
/// 用户权限提供器。
/// 每次从 Identity store 读取用户个人权限和角色权限并合并；授权判定不复用值缓存。
/// </summary>
public class PermissionProvider(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager) : IPermissionProvider
{
    public async Task<IList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            cancellationToken.ThrowIfCancellationRequested();

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
    }
}
