using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

// 登录请求，返回 JWT Token 字符串
public record LoginUserCommand(string EmployeeNo, string Password) : ICommand<Result<string>>;

public class LoginUserHandler(
    IIdentityService identityService,
    IJwtTokenGenerator jwtTokenGenerator) : ICommandHandler<LoginUserCommand, Result<string>>
{
    public async Task<Result<string>> Handle(LoginUserCommand request, CancellationToken cancellationToken)
    {
        // 1. 校验密码
        var checkResult = await identityService.CheckPasswordAsync(request.EmployeeNo, request.Password);
        if (!checkResult.IsSuccess || !checkResult.Value)
        {
            return Result.Failure("工号不存在或密码错误");
        }

        // 2. 获取灵魂绑定 ID 和角色列表
        var userId = await identityService.GetUserIdByEmployeeNoAsync(request.EmployeeNo);
        var roles = await identityService.GetRolesAsync(request.EmployeeNo);

        if (userId == null) return Result.Failure("身份信息异常");

        // 3. 颁发令牌
        var token = jwtTokenGenerator.GenerateToken(userId.Value, request.EmployeeNo, roles);

        return Result.Success(token);
    }
}