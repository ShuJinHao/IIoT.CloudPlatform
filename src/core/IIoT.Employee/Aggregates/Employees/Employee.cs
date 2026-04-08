using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Employee.Aggregates.Employees;

/// <summary>
/// 聚合根:员工/操作员
/// 包含两级管辖权:工序级(粗颗粒)+ 机台级(精细颗粒)。
/// </summary>
public class Employee : IAggregateRoot
{
    private readonly List<EmployeeProcessAccess> _processAccesses = [];
    private readonly List<EmployeeDeviceAccess> _deviceAccesses = [];

    /// <summary>
    /// 仅供 EF Core 物化使用,业务代码不要调用。
    /// </summary>
    protected Employee() { }

    /// <summary>
    /// 创建新员工。
    /// Id 由调用方传入(因为 Employee 与 IdentityService 的 User 共用 Id)。
    /// </summary>
    public Employee(Guid id, string employeeNo, string realName)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Employee Id 不能为空。", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(employeeNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(realName);

        Id = id;
        EmployeeNo = employeeNo.Trim();
        RealName = realName.Trim();
        IsActive = true;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// 员工工号 (车间设备的登录账号)
    /// </summary>
    public string EmployeeNo { get; private set; } = null!;

    /// <summary>
    /// 员工真实姓名
    /// </summary>
    public string RealName { get; private set; } = null!;

    /// <summary>
    /// 账号状态 (true: 正常启用; false: 已离职或冻结,禁止登录设备)
    /// </summary>
    public bool IsActive { get; private set; }

    // ── 基础档案行为 ──────────────────────────────────────

    /// <summary>
    /// 修改员工的工号和姓名。
    /// </summary>
    public void Rename(string newEmployeeNo, string newRealName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newEmployeeNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(newRealName);

        EmployeeNo = newEmployeeNo.Trim();
        RealName = newRealName.Trim();
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    // ── 工序级管辖权 ──────────────────────────────────────

    public IReadOnlyCollection<EmployeeProcessAccess> ProcessAccesses => _processAccesses.AsReadOnly();

    public void AddProcessAccess(Guid processId)
    {
        if (!_processAccesses.Any(x => x.ProcessId == processId))
        {
            _processAccesses.Add(new EmployeeProcessAccess(this, processId));
        }
    }

    public void RemoveProcessAccess(Guid processId)
    {
        var access = _processAccesses.FirstOrDefault(x => x.ProcessId == processId);
        if (access != null)
        {
            _processAccesses.Remove(access);
        }
    }

    // ── 机台级管辖权 ──────────────────────────────────────

    /// <summary>
    /// 该员工允许操作的具体设备列表(对外暴露为只读,保证聚合根数据一致性)
    /// </summary>
    public IReadOnlyCollection<EmployeeDeviceAccess> DeviceAccesses => _deviceAccesses.AsReadOnly();

    public void AddDeviceAccess(Guid deviceId)
    {
        if (!_deviceAccesses.Any(x => x.DeviceId == deviceId))
        {
            _deviceAccesses.Add(new EmployeeDeviceAccess(this, deviceId));
        }
    }

    public void RemoveDeviceAccess(Guid deviceId)
    {
        var access = _deviceAccesses.FirstOrDefault(x => x.DeviceId == deviceId);
        if (access != null)
        {
            _deviceAccesses.Remove(access);
        }
    }
}