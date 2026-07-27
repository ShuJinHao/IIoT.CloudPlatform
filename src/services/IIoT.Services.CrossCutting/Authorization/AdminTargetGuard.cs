using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;

namespace IIoT.Services.CrossCutting.Authorization;

/// <summary>
/// 以身份库中的当前账号与角色为准，统一保护 Admin 目标。
/// </summary>
public sealed class AdminTargetGuard(
    IIdentityAccountStore identityAccountStore) : IAdminTargetGuard
{
    public async Task<Result> EnsureMutableNonAdminTargetAsync(
        Guid targetUserId,
        CancellationToken cancellationToken = default)
    {
        var account = await identityAccountStore.GetByIdAsync(
            targetUserId,
            cancellationToken);
        if (account is null)
        {
            return Result.Failure(AdminTargetProtectionErrors.TargetNotFound);
        }

        var roles = await identityAccountStore.GetRolesAsync(
            targetUserId,
            cancellationToken);
        return SystemRoles.ContainsAdmin(roles)
            ? Result.Failure(AdminTargetProtectionErrors.AdminTargetProtected)
            : Result.Success();
    }
}
