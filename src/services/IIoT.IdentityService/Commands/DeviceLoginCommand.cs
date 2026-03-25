// 路径：src/services/IIoT.IdentityService/Commands/DeviceLoginCommand.cs
using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.IdentityService.Commands;

/// <summary>
/// WPF 边缘端专用登录命令。
/// 在普通登录基础上增加设备绑定校验：
///   Admin 角色 → 跳过设备校验，直接签发
///   普通员工   → 必须绑定了该设备才能登录
/// </summary>
public record DeviceLoginCommand(
    string EmployeeNo,
    string Password,
    Guid DeviceId
) : ICommand<Result<string>>;

public class DeviceLoginHandler(
    IAccountService accountService,
    IPermissionProvider permissionProvider,
    ICacheService cacheService,
    IJwtTokenGenerator jwtTokenGenerator,
    IReadRepository<Employee> employeeRepository)
    : ICommandHandler<DeviceLoginCommand, Result<string>>
{
    public async Task<Result<string>> Handle(
        DeviceLoginCommand request,
        CancellationToken cancellationToken)
    {
        // 1. 验证密码
        var checkResult = await accountService.CheckPasswordAsync(
            request.EmployeeNo, request.Password);

        if (!checkResult.IsSuccess || !checkResult.Value)
            return Result.Failure("工号不存在或密码错误");

        // 2. 获取 userId 和角色
        var userId = await accountService.GetUserIdByEmployeeNoAsync(request.EmployeeNo);
        if (userId == null) return Result.Failure("身份信息异常");

        var roles = await accountService.GetRolesAsync(request.EmployeeNo);
        var isAdmin = roles.Contains("Admin");

        // 3. 设备权限校验（Admin 跳过）
        if (!isAdmin)
        {
            var spec = new EmployeeWithAccessesSpec(userId.Value);
            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                spec, cancellationToken);

            if (employee == null)
                return Result.Failure("员工档案不存在");

            var hasDeviceAccess = employee.DeviceAccesses
                .Any(d => d.DeviceId == request.DeviceId);

            if (!hasDeviceAccess)
                return Result.Failure("您无权操作此设备，请联系管理员绑定设备权限");
        }

        // 4. 清除权限缓存
        await cacheService.RemoveAsync(
            $"iiot:permissions:v1:user:{userId.Value}", cancellationToken);

        // 5. 获取权限并签发 JWT
        var permissions = await permissionProvider.GetPermissionsAsync(
            userId.Value, cancellationToken);

        var token = jwtTokenGenerator.GenerateToken(
            userId.Value, request.EmployeeNo, roles, permissions);

        return Result.Success(token);
    }
}