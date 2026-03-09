using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Employee.Aggregates.Employees;

/// <summary>
/// 聚合根：员工/操作员
/// (包含第一维度：工序级管辖权；第二维度：精确机台级管辖权)
/// </summary>
public class Employee : IAggregateRoot
{
    // 员工关联的【工序】权限私有集合 (粗颗粒度)
    private readonly List<EmployeeProcessAccess> _processAccesses = [];

    // 🌟 新增：员工关联的【具体设备】权限私有集合 (精细颗粒度)
    private readonly List<EmployeeDeviceAccess> _deviceAccesses = [];

    // 给 EF Core 预留的无参构造函数
    protected Employee()
    {
    }

    /// <summary>
    /// 领域工厂：创建新员工
    /// </summary>
    public Employee(Guid id, string employeeNo, string realName)
    {
        Id = id;
        EmployeeNo = employeeNo;
        RealName = realName;
        IsActive = true;
    }

    /// <summary>
    /// 员工全局唯一标识 (UUID)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 员工工号 (车间设备的登录账号)
    /// </summary>
    public string EmployeeNo { get; set; } = null!;

    /// <summary>
    /// 员工真实姓名 (如：张三，用于报表和系统日志显示)
    /// </summary>
    public string RealName { get; set; } = null!;

    /// <summary>
    /// 账号状态标识 (true: 正常启用; false: 已离职或冻结，禁止登录设备)
    /// </summary>
    public bool IsActive { get; set; }

    // ==========================================
    // 工序管辖权逻辑 (保持原样)
    // ==========================================
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

    // ==========================================
    // 🌟 新增：具体机台设备管辖权逻辑
    // ==========================================

    /// <summary>
    /// 该员工允许操作的具体设备列表 (对外暴露为只读，保证聚合根数据的一致性)
    /// </summary>
    public IReadOnlyCollection<EmployeeDeviceAccess> DeviceAccesses => _deviceAccesses.AsReadOnly();

    /// <summary>
    /// 为该员工分配特定机台设备的管辖权
    /// </summary>
    /// <param name="deviceId">具体设备/机台的 UUID</param>
    public void AddDeviceAccess(Guid deviceId)
    {
        // 避免重复添加权限 (幂等性校验)
        if (!_deviceAccesses.Any(x => x.DeviceId == deviceId))
        {
            _deviceAccesses.Add(new EmployeeDeviceAccess(this, deviceId));
        }
    }

    /// <summary>
    /// 移除该员工对某个具体设备的管辖权
    /// </summary>
    /// <param name="deviceId">具体设备/机台的 UUID</param>
    public void RemoveDeviceAccess(Guid deviceId)
    {
        var access = _deviceAccesses.FirstOrDefault(x => x.DeviceId == deviceId);
        if (access != null)
        {
            _deviceAccesses.Remove(access);
        }
    }
}