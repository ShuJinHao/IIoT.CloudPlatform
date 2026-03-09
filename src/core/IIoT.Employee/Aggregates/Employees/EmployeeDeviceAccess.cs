using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Employee.Aggregates.Employees;

/// <summary>
/// 映射关联实体：员工与具体物理设备的权限绑定关系表
/// (解决精细化管辖问题：精确到具体机台级别，如张三只能管 A1 注液机)
/// </summary>
public class EmployeeDeviceAccess : IEntity
{
    // 给 EF Core 预留的无参构造函数
    protected EmployeeDeviceAccess()
    {
    }

    /// <summary>
    /// 领域构造函数
    /// </summary>
    /// <param name="employee">员工聚合根实体</param>
    /// <param name="deviceId">具体设备/机台的 UUID</param>
    public EmployeeDeviceAccess(Employee employee, Guid deviceId)
    {
        Employee = employee;
        EmployeeId = employee.Id; // 自动提取员工的 Guid
        DeviceId = deviceId;
    }

    /// <summary>
    /// 关联的员工 UUID (联合主键之一)
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// 关联的具体设备/机台 UUID (联合主键之一)
    /// </summary>
    public Guid DeviceId { get; set; }

    /// <summary>
    /// 导航属性：关联的员工聚合根引用
    /// </summary>
    public Employee Employee { get; set; } = null!;
}