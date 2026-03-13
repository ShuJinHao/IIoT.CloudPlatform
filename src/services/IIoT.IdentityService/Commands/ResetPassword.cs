using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

/// <summary>
/// 业务指令：管理员强制重置指定员工的登录密码 (不需要旧密码)
/// </summary>
[AuthorizeRequirement("Employee.Update")]
public record ResetPasswordCommand(
    Guid UserId,
    string NewPassword
) : ICommand<Result<bool>>;

public class ResetPasswordHandler(
    IAccountService accountService
) : ICommandHandler<ResetPasswordCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        return await accountService.ResetPasswordAsync(request.UserId, request.NewPassword);
    }
}
