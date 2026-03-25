// 路径：src/services/IIoT.IdentityService/Commands/LoginUser.cs
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

public record LoginUserCommand(string EmployeeNo, string Password) : ICommand<Result<string>>;

public class LoginUserHandler(
    IAccountService accountService,
    IPermissionProvider permissionProvider,
    ICacheService cacheService,
    IJwtTokenGenerator jwtTokenGenerator)
    : ICommandHandler<LoginUserCommand, Result<string>>
{
    public async Task<Result<string>> Handle(
        LoginUserCommand request,
        CancellationToken cancellationToken)
    {
        var checkResult = await accountService.CheckPasswordAsync(
            request.EmployeeNo, request.Password);

        if (!checkResult.IsSuccess || !checkResult.Value)
            return Result.Failure("工号不存在或密码错误");

        var userId = await accountService.GetUserIdByEmployeeNoAsync(request.EmployeeNo);
        var roles = await accountService.GetRolesAsync(request.EmployeeNo);

        if (userId == null) return Result.Failure("身份信息异常");

        await cacheService.RemoveAsync(
            $"iiot:permissions:v1:user:{userId.Value}", cancellationToken);

        var permissions = await permissionProvider.GetPermissionsAsync(
            userId.Value, cancellationToken);

        var token = jwtTokenGenerator.GenerateToken(
            userId.Value, request.EmployeeNo, roles, permissions);

        return Result.Success(token);
    }
}