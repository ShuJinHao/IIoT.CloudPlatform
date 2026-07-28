using System.Security.Claims;
using IIoT.Services.Contracts.Identity;

namespace IIoT.HttpApi.Infrastructure.Authentication;

internal sealed class HumanJwtStatusValidator(ICloudOidcUserProfileService profileService)
{
    public async Task<bool> IsCurrentAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                principal.FindFirstValue(IIoTClaimTypes.ActorType),
                IIoTClaimTypes.HumanActor,
                StringComparison.Ordinal))
        {
            return true;
        }

        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return false;
        }

        var tokenStatusVersion = principal.FindFirstValue(IIoTClaimTypes.IdentityStatusVersion);
        if (string.IsNullOrWhiteSpace(tokenStatusVersion))
        {
            return false;
        }

        try
        {
            var profile = await profileService.GetByUserIdAsync(userId, cancellationToken);
            return profile is not null &&
                   profile.AccountEnabled &&
                   profile.EmployeeActive &&
                   !string.IsNullOrWhiteSpace(profile.StatusVersion) &&
                   string.Equals(
                       tokenStatusVersion,
                       profile.StatusVersion,
                       StringComparison.Ordinal);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
