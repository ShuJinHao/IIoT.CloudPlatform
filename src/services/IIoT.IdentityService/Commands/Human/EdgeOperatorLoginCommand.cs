using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
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
    IReadRepository<Employee> employeeRepository)
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
        var isAdmin = roles.Contains("Admin");

        if (!isAdmin)
        {
            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(account.Id),
                cancellationToken);

            if (employee is null)
            {
                return Result.Failure("员工档案不存在");
            }

            if (!employee.IsActive)
            {
                return Result.Failure("员工已停用，无法登录");
            }

            var hasDeviceAccess = employee.DeviceAccesses.Any(d => d.DeviceId == request.DeviceId);
            if (!hasDeviceAccess)
            {
                return Result.Failure("您无权操作此设备，请联系管理员绑定设备权限");
            }
        }

        await cacheService.RemoveAsync(CacheKeys.PermissionByUser(account.Id), cancellationToken);

        var permissions = await permissionProvider.GetPermissionsAsync(account.Id, cancellationToken);
        var token = jwtTokenGenerator.GenerateToken(account.Id, request.EmployeeNo, roles, permissions);

        return Result.Success(token);
    }
}
