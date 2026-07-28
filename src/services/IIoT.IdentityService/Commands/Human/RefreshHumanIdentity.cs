using IIoT.Core.Identity.Aggregates.IdentityAccounts;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

public record RefreshHumanIdentityCommand(string RefreshToken) : IHumanCommand<Result<HumanIdentitySessionResult>>;

public sealed class RefreshHumanIdentityHandler(
    IIdentityAccountStore identityAccountStore,
    IPermissionProvider permissionProvider,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService,
    IHumanSessionRevocationService sessionRevocationService,
    ICloudOidcUserProfileService profileService)
    : ICommandHandler<RefreshHumanIdentityCommand, Result<HumanIdentitySessionResult>>
{
    public async Task<Result<HumanIdentitySessionResult>> Handle(
        RefreshHumanIdentityCommand request,
        CancellationToken cancellationToken)
    {
        var rotationResult = await refreshTokenService.RotateAsync(
            IIoTClaimTypes.HumanActor,
            request.RefreshToken,
            cancellationToken);

        if (!rotationResult.IsSuccess)
        {
            return Result.Unauthorized(rotationResult.Errors?.ToArray() ?? ["刷新令牌无效或已过期。"]);
        }

        IdentityAccount? account;
        CloudOidcUserProfile? profile;
        try
        {
            account = await identityAccountStore.GetByIdAsync(
                rotationResult.Value!.SubjectId,
                cancellationToken);
            profile = await profileService.GetByUserIdAsync(
                rotationResult.Value.SubjectId,
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await sessionRevocationService.RevokeAllAsync(
                rotationResult.Value!.SubjectId,
                "identity-status-unavailable",
                cancellationToken);
            return Result.Unauthorized("账号不可用。");
        }

        if (account is null ||
            !account.IsEnabled ||
            profile is null ||
            !profile.AccountEnabled ||
            !profile.EmployeeActive ||
            string.IsNullOrWhiteSpace(profile.StatusVersion) ||
            !string.Equals(
                rotationResult.Value.IdentityStatusVersion,
                profile.StatusVersion,
                StringComparison.Ordinal))
        {
            await sessionRevocationService.RevokeAllAsync(
                rotationResult.Value.SubjectId,
                "identity-unavailable",
                cancellationToken);

            return Result.Unauthorized("账号不可用。");
        }

        var roles = await identityAccountStore.GetRolesAsync(account.Id, cancellationToken);

        var permissions = await permissionProvider.GetPermissionsAsync(account.Id, cancellationToken);
        var accessToken = jwtTokenGenerator.GenerateHumanToken(
            account.Id,
            account.EmployeeNo,
            roles,
            permissions,
            profile.StatusVersion);

        return Result.Success(new HumanIdentitySessionResult(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            rotationResult.Value.RefreshToken.Token,
            rotationResult.Value.RefreshToken.ExpiresAtUtc));
    }
}
