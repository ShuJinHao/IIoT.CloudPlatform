using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

[AuthorizeRequirement("Role.Update")]
public record UpdateRolePermissionsCommand(string RoleName, List<string> Permissions) : ICommand<Result<bool>>;

public class UpdateRolePermissionsHandler(
    IRolePolicyService rolePolicyService,
    ICacheService cacheService
) : ICommandHandler<UpdateRolePermissionsCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        if (request.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("系统保护：内置 Admin 角色的权限由系统硬编码，禁止修改！");
        }

        var result = await rolePolicyService.UpdateRolePermissionsAsync(request.RoleName, request.Permissions);

        if (result.IsSuccess && result.Value)
        {
            await cacheService.RemoveAsync("iiot:permissions:v1:all-defined", cancellationToken);
        }

        return result;
    }
}
