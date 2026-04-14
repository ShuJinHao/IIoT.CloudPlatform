using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

public record LoginUserCommand(string EmployeeNo, string Password) : IHumanCommand<Result<string>>;

public class LoginUserHandler(
    IIdentityAccountStore identityAccountStore,
    IIdentityPasswordService identityPasswordService,
    IPermissionProvider permissionProvider,
    ICacheService cacheService,
    IJwtTokenGenerator jwtTokenGenerator)
    : ICommandHandler<LoginUserCommand, Result<string>>
{
    public async Task<Result<string>> Handle(
        LoginUserCommand request,
        CancellationToken cancellationToken)
    {
        var account = await identityAccountStore.GetByEmployeeNoAsync(
            request.EmployeeNo,
            cancellationToken);

        if (account is null)
        {
            return Result.Failure("工号不存在或密码错误");
        }

        if (!account.IsEnabled)
        {
            return Result.Failure("账号已停用，请联系管理员");
        }

        var checkResult = await identityPasswordService.CheckPasswordAsync(
            account.Id,
            request.Password,
            cancellationToken);

        if (!checkResult.IsSuccess || !checkResult.Value)
        {
            return Result.Failure("工号不存在或密码错误");
        }

        var roles = await identityAccountStore.GetRolesAsync(account.Id, cancellationToken);

        await cacheService.RemoveAsync(CacheKeys.PermissionByUser(account.Id), cancellationToken);

        var permissions = await permissionProvider.GetPermissionsAsync(account.Id, cancellationToken);
        var token = jwtTokenGenerator.GenerateToken(account.Id, request.EmployeeNo, roles, permissions);

        return Result.Success(token);
    }
}
