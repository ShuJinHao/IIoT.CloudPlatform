using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Queries;

public sealed record HumanIdentitySessionDto(
    Guid UserId,
    string EmployeeNo,
    string DisplayName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record GetHumanIdentitySessionQuery
    : IHumanQuery<Result<HumanIdentitySessionDto>>;

public sealed class GetHumanIdentitySessionHandler(
    ICurrentUser currentUser,
    IIdentityAccountStore identityAccountStore,
    ICloudOidcUserProfileService profileService,
    IPermissionProvider permissionProvider)
    : IQueryHandler<GetHumanIdentitySessionQuery, Result<HumanIdentitySessionDto>>
{
    public async Task<Result<HumanIdentitySessionDto>> Handle(
        GetHumanIdentitySessionQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated ||
            !string.Equals(
                currentUser.ActorType,
                IIoTClaimTypes.HumanActor,
                StringComparison.Ordinal) ||
            !Guid.TryParse(currentUser.Id, out var userId) ||
            userId == Guid.Empty)
        {
            return Result.Unauthorized("账号不可用。");
        }

        try
        {
            var account = await identityAccountStore.GetByIdAsync(userId, cancellationToken);
            var profile = await profileService.GetByUserIdAsync(userId, cancellationToken);
            if (account is null ||
                !account.IsEnabled ||
                profile is null ||
                profile.UserId != userId ||
                !profile.AccountEnabled ||
                !profile.EmployeeActive ||
                string.IsNullOrWhiteSpace(profile.StatusVersion) ||
                string.IsNullOrWhiteSpace(profile.EmployeeNo) ||
                string.IsNullOrWhiteSpace(profile.RealName) ||
                !string.Equals(
                    account.EmployeeNo,
                    profile.EmployeeNo,
                    StringComparison.Ordinal))
            {
                return Result.Unauthorized("账号不可用。");
            }

            var roles = Normalize(await identityAccountStore.GetRolesAsync(userId, cancellationToken));
            var permissions = Normalize(await permissionProvider.GetPermissionsAsync(userId, cancellationToken));

            return Result.Success(new HumanIdentitySessionDto(
                userId,
                profile.EmployeeNo.Trim(),
                profile.RealName.Trim(),
                roles,
                permissions));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Unauthorized("账号不可用。");
        }
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> values)
        => values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
