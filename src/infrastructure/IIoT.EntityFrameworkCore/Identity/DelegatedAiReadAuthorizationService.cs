using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class DelegatedAiReadAuthorizationService(
    ICloudOidcUserProfileService profileService,
    IIdentityAccountStore identityAccountStore,
    IPermissionProvider permissionProvider,
    IDevicePermissionService devicePermissionService)
    : IAiReadDelegatedAuthorizationService
{
    public async Task<AiReadDelegatedAuthorization?> AuthorizeAsync(
        Guid cloudUserId,
        string issuedStatusVersion,
        IReadOnlyCollection<string> requiredPermissions,
        CancellationToken cancellationToken = default)
    {
        if (cloudUserId == Guid.Empty || string.IsNullOrWhiteSpace(issuedStatusVersion))
        {
            return null;
        }

        var profile = await profileService.GetByUserIdAsync(cloudUserId, cancellationToken);
        if (profile is null ||
            !profile.AccountEnabled ||
            !profile.EmployeeActive ||
            string.IsNullOrWhiteSpace(profile.StatusVersion) ||
            !string.Equals(
                profile.StatusVersion,
                issuedStatusVersion,
                StringComparison.Ordinal))
        {
            return null;
        }

        var roles = await identityAccountStore.GetRolesAsync(cloudUserId, cancellationToken);
        var isAdministrator = SystemRoles.ContainsCanonicalAdminRole(roles);
        var permissions = await permissionProvider.GetPermissionsAsync(
            cloudUserId,
            cancellationToken);
        var permissionSet = permissions.ToHashSet(StringComparer.Ordinal);
        if (!requiredPermissions.All(permissionSet.Contains))
        {
            return null;
        }

        var allowedDeviceIds = isAdministrator
            ? null
            : await devicePermissionService.GetAccessibleDeviceIdsAsync(
                cloudUserId,
                cancellationToken);

        return new AiReadDelegatedAuthorization(
            cloudUserId,
            isAdministrator,
            allowedDeviceIds?.Distinct().ToArray());
    }
}
