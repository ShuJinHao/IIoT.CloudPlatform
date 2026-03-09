using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

// 只有超级管理员或人事具备销毁账号权限
[AuthorizeRequirement("Account.Delete")]
public record DeleteAccountCommand(Guid UserId) : ICommand<Result>;

public class DeleteAccountHandler(IIdentityService identityService) : ICommandHandler<DeleteAccountCommand, Result>
{
    public async Task<Result> Handle(DeleteAccountCommand request, CancellationToken cancellationToken)
    {
        // 🌟 防越权：坚决禁止通过接口直接删除系统初始的超级管理员(比如前面 Seed 的 101650)
        // 实际开发中可以通过校验 UserId 是否等于配置中的特权 ID 来拦截
        return await identityService.DeleteUserAsync(request.UserId);
    }
}