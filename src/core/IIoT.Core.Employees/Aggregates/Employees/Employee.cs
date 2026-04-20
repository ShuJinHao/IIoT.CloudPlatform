using IIoT.Core.Employees.Aggregates.Employees.Events;
using IIoT.Core.Employees.Aggregates.Employees.ValueObjects;
using IIoT.SharedKernel.Domain;
using EmployeeNoValueObject = IIoT.Core.Employees.Aggregates.Employees.ValueObjects.EmployeeNo;

namespace IIoT.Core.Employees.Aggregates.Employees;

/// <summary>
/// 聚合根:员工/操作员
/// 包含机台级管辖权(精细颗粒)。
/// </summary>
public class Employee : BaseEntity<Guid>
{
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
        : this(id, EmployeeNoValueObject.From(employeeNo), realName)
    {
    }

    public Employee(Guid id, EmployeeNoValueObject employeeNo, string realName)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Employee Id 不能为空。", nameof(id));

        Id = id;
        EmployeeNo = employeeNo.Value;
        RealName = NormalizeRequired(realName, nameof(realName));
        IsActive = true;

        AddDomainEvent(new EmployeeOnboardedDomainEvent(Id, EmployeeNo, RealName));
    }

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

    public uint RowVersion { get; private set; }

    // ── 基础档案行为 ──────────────────────────────────────

    /// <summary>
    /// 修改员工的工号和姓名。
    /// </summary>
    public void Rename(string newEmployeeNo, string newRealName)
    {
        var employeeNo = EmployeeNoValueObject.From(newEmployeeNo);
        var realName = NormalizeRequired(newRealName, nameof(newRealName));

        if (EmployeeNo == employeeNo.Value && RealName == realName)
        {
            return;
        }

        EmployeeNo = employeeNo.Value;
        RealName = realName;

        AddDomainEvent(new EmployeeRenamedDomainEvent(Id, EmployeeNo, RealName));
    }

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        AddDomainEvent(new EmployeeActivatedDomainEvent(Id));
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        AddDomainEvent(new EmployeeDeactivatedDomainEvent(Id));
    }

    public void Terminate()
    {
        IsActive = false;
        AddDomainEvent(new EmployeeTerminatedDomainEvent(Id, EmployeeNo));
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

    private static string NormalizeRequired(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }
}
