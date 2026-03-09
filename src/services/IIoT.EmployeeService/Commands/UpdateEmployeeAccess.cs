using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.EmployeeService.Commands;

/// <summary>
/// 业务层指令：全量同步/更新员工的管辖权
/// </summary>
[AuthorizeRequirement("Employee.UpdateAccess")] // 需要车间主任或管理员权限
public record UpdateEmployeeAccessCommand(
    Guid EmployeeId,
    List<Guid> ProcessIds, // 该员工最新的工序管辖列表
    List<Guid> DeviceIds   // 该员工最新的机台管辖列表
) : ICommand<Result<bool>>;

public class UpdateEmployeeAccessHandler(
    IRepository<Employee> employeeRepository
) : ICommandHandler<UpdateEmployeeAccessCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateEmployeeAccessCommand request, CancellationToken cancellationToken)
    {
        // 🌟 1. 见证奇迹的时刻：使用规约模式，极致清爽地查出实体及导航属性
        var spec = new EmployeeWithAccessesSpec(request.EmployeeId);
        var employee = await employeeRepository.GetSingleOrDefaultAsync(spec, cancellationToken);

        if (employee == null) return Result.Failure("未找到目标员工档案");

        // ==========================================
        // 🌟 2. 工序管辖权差集更新 (Diff Update)
        // ==========================================
        var existingProcessIds = employee.ProcessAccesses.Select(p => p.ProcessId).ToList();

        // 算出现在该删掉哪些 (原有的里面，新传入列表没有的)
        var processesToRemove = existingProcessIds.Except(request.ProcessIds).ToList();
        // 算出现在该新增哪些 (新传入的里面，原有列表里没有的)
        var processesToAdd = request.ProcessIds.Except(existingProcessIds).ToList();

        foreach (var id in processesToRemove) employee.RemoveProcessAccess(id);
        foreach (var id in processesToAdd) employee.AddProcessAccess(id);

        // ==========================================
        // 🌟 3. 机台管辖权差集更新 (Diff Update)
        // ==========================================
        var existingDeviceIds = employee.DeviceAccesses.Select(d => d.DeviceId).ToList();

        var devicesToRemove = existingDeviceIds.Except(request.DeviceIds).ToList();
        var devicesToAdd = request.DeviceIds.Except(existingDeviceIds).ToList();

        foreach (var id in devicesToRemove) employee.RemoveDeviceAccess(id);
        foreach (var id in devicesToAdd) employee.AddDeviceAccess(id);

        // 4. 落地保存
        employeeRepository.Update(employee);
        await employeeRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(true);
    }
}