using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

public record EdgeOperatorLoginCommand(
    string EmployeeNo,
    string Password,
    Guid DeviceId
) : IHumanCommand<Result<HumanIdentitySessionResult>>;

public class EdgeOperatorLoginHandler(
    IIdentityAccountStore identityAccountStore,
    IIdentityPasswordService identityPasswordService,
    IPermissionProvider permissionProvider,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService,
    IEmployeeLookupService employeeLookupService,
    ICloudOidcUserProfileService profileService)
    : ICommandHandler<EdgeOperatorLoginCommand, Result<HumanIdentitySessionResult>>
{
    private const string InvalidLoginMessage = "账号不存在或密码错误";

    public async Task<Result<HumanIdentitySessionResult>> Handle(
        EdgeOperatorLoginCommand request,
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

        CloudOidcUserProfile? profile;
        try
        {
            profile = await profileService.GetByUserIdAsync(account.Id, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure(InvalidLoginMessage);
        }

        if (profile is null ||
            !profile.AccountEnabled ||
            !profile.EmployeeActive ||
            string.IsNullOrWhiteSpace(profile.StatusVersion))
        {
            return Result.Failure(InvalidLoginMessage);
        }

        var roles = await identityAccountStore.GetRolesAsync(account.Id, cancellationToken);
        var isAdmin = roles.Contains(SystemRoles.Admin, StringComparer.Ordinal);

        if (!isAdmin)
        {
            var employee = await employeeLookupService.GetByIdAsync(account.Id, cancellationToken);
            if (employee is null)
            {
                return Result.Failure("员工档案不存在");
            }

            if (!employee.IsActive)
            {
                return Result.Failure("账号已冻结，无法登录");
            }

            var hasDeviceAccess = employee.DeviceIds.Contains(request.DeviceId);
            if (!hasDeviceAccess)
            {
                return Result.Failure("无设备权限，请联系管理员授权");
            }
        }

        var permissions = await permissionProvider.GetPermissionsAsync(account.Id, cancellationToken);
        var accessToken = jwtTokenGenerator.GenerateHumanToken(
            account.Id,
            request.EmployeeNo,
            roles,
            permissions,
            profile.StatusVersion);
        var refreshToken = await refreshTokenService.IssueHumanAsync(
            account.Id,
            profile.StatusVersion,
            cancellationToken);

        return Result.Success(new HumanIdentitySessionResult(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            refreshToken.Token,
            refreshToken.ExpiresAtUtc));
    }
}
