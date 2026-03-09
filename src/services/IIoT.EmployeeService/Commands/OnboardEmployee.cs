using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands;

[AuthorizeRequirement("Employee.Onboard")]
public record OnboardEmployeeCommand(
    string EmployeeNo,
    string RealName,
    string Password,
    string? RoleName = null, // 🌟 核心修改 1：改成可空字符串，并赋予默认值 null
    List<Guid>? ProcessIds = null,
    List<Guid>? DeviceIds = null
) : ICommand<Result<Guid>>;

public class OnboardEmployeeHandler(
    IIdentityService identityService,
    IRepository<Employee> employeeRepository
) : ICommandHandler<OnboardEmployeeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(OnboardEmployeeCommand request, CancellationToken cancellationToken)
    {
        // 防线：如果传了角色，坚决禁止传 Admin
        if (!string.IsNullOrWhiteSpace(request.RoleName) && request.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("系统保护：禁止通过员工入职接口分配最高管理员权限！");
        }

        var sharedId = Guid.NewGuid();

        // ==========================================
        // 第一步：在【身份中心】办理通行证
        // ==========================================

        var identityResult = await identityService.CreateUserAsync(sharedId, request.EmployeeNo, request.Password);
        if (!identityResult.IsSuccess)
            return Result.Failure(identityResult.Errors?.ToArray() ?? ["身份账号创建失败"]);

        // 🌟 核心修改 2：只有前端明确传了角色名，才去保安科挂载角色
        if (!string.IsNullOrWhiteSpace(request.RoleName))
        {
            var roleResult = await identityService.AssignRoleToUserAsync(request.EmployeeNo, request.RoleName);
            if (!roleResult.IsSuccess)
                return Result.Failure(roleResult.Errors?.ToArray() ?? ["系统角色绑定失败"]);
        }

        // ==========================================
        // 第二步：在【业务中心】建立档案
        // ==========================================

        var employee = new Employee(sharedId, request.EmployeeNo, request.RealName);

        if (request.ProcessIds != null && request.ProcessIds.Any())
        {
            foreach (var pId in request.ProcessIds) employee.AddProcessAccess(pId);
        }

        if (request.DeviceIds != null && request.DeviceIds.Any())
        {
            foreach (var dId in request.DeviceIds) employee.AddDeviceAccess(dId);
        }

        employeeRepository.Add(employee);
        await employeeRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(sharedId);
    }
}