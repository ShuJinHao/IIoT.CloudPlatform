using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands;

/// <summary>
/// 业务层入职指令：协调身份中心与人事档案
/// </summary>
[AuthorizeRequirement("Employee.Onboard")]
public record OnboardEmployeeCommand(
    string EmployeeNo,
    string RealName,
    string Password,
    string RoleName, // 初始分配的角色
    List<Guid> ProcessIds, // 初始管辖工序
    List<Guid> DeviceIds   // 初始管辖机台
) : ICommand<Result<Guid>>;

public class OnboardEmployeeHandler(
    IIdentityService identityService,   // 🌟 注入身份中心接口 (保安)
    IRepository<Employee> employeeRepository // 🌟 注入员工仓储 (人事)
) : ICommandHandler<OnboardEmployeeCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(OnboardEmployeeCommand request, CancellationToken cancellationToken)
    {
        // 1. 生成全局唯一 ID (作为两个领域的灵魂纽带)
        var sharedId = Guid.NewGuid();

        // ==========================================
        // 🌟 第一步：在【身份中心】办理通行证
        // ==========================================

        // 仅仅创建底层账号，Identity 不知道 Employee 的存在
        var identityResult = await identityService.CreateUserAsync(sharedId, request.EmployeeNo, request.Password);
        if (!identityResult.IsSuccess)
            return Result.Failure(identityResult.Errors?.ToArray() ?? ["身份账号创建失败"]);

        // 绑定系统角色 (保安科盖章)
        var roleResult = await identityService.AssignRoleToUserAsync(request.EmployeeNo, request.RoleName);
        if (!roleResult.IsSuccess)
            return Result.Failure(roleResult.Errors?.ToArray() ?? ["系统角色绑定失败"]);

        // ==========================================
        // 🌟 第二步：在【业务中心】建立档案
        // ==========================================

        // 创建 Employee 聚合根
        var employee = new Employee(sharedId, request.EmployeeNo, request.RealName);

        // 分配管辖工序权限
        foreach (var pId in request.ProcessIds)
        {
            employee.AddProcessAccess(pId);
        }

        // 🌟 分配具体机台管辖权 (咱们刚才新加的业务血肉)
        foreach (var dId in request.DeviceIds)
        {
            employee.AddDeviceAccess(dId);
        }

        // 3. 业务数据落库
        employeeRepository.Add(employee);
        await employeeRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(sharedId);
    }
}