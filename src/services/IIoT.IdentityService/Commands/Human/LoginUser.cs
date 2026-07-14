using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

public record LoginUserCommand(string EmployeeNo, string Password) : IHumanCommand<Result<HumanIdentitySessionResult>>;

public class LoginUserHandler(
    IIdentityAccountStore identityAccountStore,
    IIdentityPasswordService identityPasswordService,
    IPermissionProvider permissionProvider,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService)
    : ICommandHandler<LoginUserCommand, Result<HumanIdentitySessionResult>>
{
    private const string InvalidLoginMessage = "账号不存在或密码错误";

    public async Task<Result<HumanIdentitySessionResult>> Handle(
        LoginUserCommand request,
        CancellationToken cancellationToken)
    {
        var account = await identityAccountStore.GetByEmployeeNoAsync(
            request.EmployeeNo,
            cancellationToken);

        if (account is null)
        {
            return Result.Failure(InvalidLoginMessage);
        }

        if (!account.IsEnabled)
        {
            return Result.Failure(InvalidLoginMessage);
        }

        var checkResult = await identityPasswordService.CheckPasswordAsync(
            account.Id,
            request.Password,
            cancellationToken);

        if (!checkResult.IsSuccess || !checkResult.Value)
        {
            return Result.Failure(InvalidLoginMessage);
        }

        var roles = await identityAccountStore.GetRolesAsync(account.Id, cancellationToken);

        var permissions = await permissionProvider.GetPermissionsAsync(account.Id, cancellationToken);
        var accessToken = jwtTokenGenerator.GenerateHumanToken(account.Id, request.EmployeeNo, roles, permissions);
        var refreshToken = await refreshTokenService.IssueAsync(
            IIoTClaimTypes.HumanActor,
            account.Id,
            cancellationToken);

        return Result.Success(new HumanIdentitySessionResult(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            refreshToken.Token,
            refreshToken.ExpiresAtUtc));
    }
}
