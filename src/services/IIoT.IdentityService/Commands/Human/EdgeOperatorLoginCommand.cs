using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.Authorization;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

public record EdgeOperatorLoginCommand(
    string EmployeeNo,
    string Password,
    Guid DeviceId
) : IHumanCommand<Result<string>>;

public class EdgeOperatorLoginHandler(
    IIdentityAccountStore identityAccountStore,
    IIdentityPasswordService identityPasswordService,
    IPermissionProvider permissionProvider,
    ICacheService cacheService,
    IJwtTokenGenerator jwtTokenGenerator,
    IEmployeeLookupService employeeLookupService)
    : ICommandHandler<EdgeOperatorLoginCommand, Result<string>>
{
    public async Task<Result<string>> Handle(
        EdgeOperatorLoginCommand request,
        CancellationToken cancellationToken)
    {
        var account = await identityAccountStore.GetByEmployeeNoAsync(
            request.EmployeeNo,
            cancellationToken);

        if (account is null)
        {
            return Result.Failure("账号不存在或密码错误");
        }

        if (!account.IsEnabled)
        {
            return Result.Failure("账号已冻结，请联系管理员");
        }

        var checkResult = await identityPasswordService.CheckPasswordAsync(
            account.Id,
            request.Password,
            cancellationToken);

        if (!checkResult.IsSuccess || !checkResult.Value)
        {
            return Result.Failure("账号不存在或密码错误");
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

        await cacheService.RemoveAsync(CacheKeys.PermissionByUser(account.Id), cancellationToken);

        var permissions = await permissionProvider.GetPermissionsAsync(account.Id, cancellationToken);
        var token = jwtTokenGenerator.GenerateHumanToken(
            account.Id,
            request.EmployeeNo,
            roles,
            permissions);

        return Result.Success(token.Token);
    }
}
