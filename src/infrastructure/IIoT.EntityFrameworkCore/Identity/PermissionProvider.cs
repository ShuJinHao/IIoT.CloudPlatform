using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Identity;

namespace IIoT.EntityFrameworkCore.Identity;

/// <summary>
/// 用户权限提供器。
/// 每次从 Identity store 读取用户个人权限和角色权限并合并；授权判定不复用值缓存。
/// </summary>
public class PermissionProvider(
    IUserStore<ApplicationUser> userStore,
    IRoleStore<IdentityRole<Guid>> roleStore,
    ILookupNormalizer keyNormalizer) : IPermissionProvider
{
    public async Task<IList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userStore.FindByIdAsync(userId.ToString(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (user == null)
        {
            return [];
        }

        var allPermissions = new HashSet<string>();

        var userClaimStore = userStore as IUserClaimStore<ApplicationUser>
                             ?? throw new NotSupportedException("The configured user store does not support claims.");
        var userClaims = await userClaimStore.GetClaimsAsync(user, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var claim in userClaims.Where(c => c.Type == IIoTClaimTypes.Permission))
        {
            allPermissions.Add(claim.Value);
        }

        var userRoleStore = userStore as IUserRoleStore<ApplicationUser>
                            ?? throw new NotSupportedException("The configured user store does not support roles.");
        var roles = await userRoleStore.GetRolesAsync(user, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var roleClaimStore = roleStore as IRoleClaimStore<IdentityRole<Guid>>
                             ?? throw new NotSupportedException("The configured role store does not support claims.");
        foreach (var roleName in roles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var role = await roleStore.FindByNameAsync(
                keyNormalizer.NormalizeName(roleName),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (role == null)
            {
                continue;
            }

            var roleClaims = await roleClaimStore.GetClaimsAsync(role, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var claim in roleClaims.Where(c => c.Type == IIoTClaimTypes.Permission))
            {
                allPermissions.Add(claim.Value);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return allPermissions.ToList();
    }
}
