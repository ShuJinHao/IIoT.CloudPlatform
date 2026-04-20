using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

public record RefreshHumanIdentityCommand(string RefreshToken) : IHumanCommand<Result<HumanIdentitySessionResult>>;

public sealed class RefreshHumanIdentityHandler(
    IIdentityAccountStore identityAccountStore,
    IPermissionProvider permissionProvider,
    ICacheService cacheService,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService)
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
            return Result.Unauthorized(rotationResult.Errors?.ToArray() ?? ["Refresh token is invalid or expired."]);
        }

        var account = await identityAccountStore.GetByIdAsync(
            rotationResult.Value!.SubjectId,
            cancellationToken);

        if (account is null || !account.IsEnabled)
        {
            await refreshTokenService.RevokeSubjectTokensAsync(
                IIoTClaimTypes.HumanActor,
                rotationResult.Value.SubjectId,
                "identity-unavailable",
                cancellationToken);

            return Result.Unauthorized("Account is unavailable.");
        }

        var roles = await identityAccountStore.GetRolesAsync(account.Id, cancellationToken);

        await cacheService.RemoveAsync(CacheKeys.PermissionByUser(account.Id), cancellationToken);

        var permissions = await permissionProvider.GetPermissionsAsync(account.Id, cancellationToken);
        var accessToken = jwtTokenGenerator.GenerateHumanToken(
            account.Id,
            account.EmployeeNo,
            roles,
            permissions);

        return Result.Success(new HumanIdentitySessionResult(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            rotationResult.Value.RefreshToken.Token,
            rotationResult.Value.RefreshToken.ExpiresAtUtc));
    }
}
