using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Employee.Aggregates.Employees;

/// <summary>
/// 映射关联实体：员工与工序的权限绑定关系表 (由 EF Core 映射为关联表)
/// </summary>
public class EmployeeProcessAccess : IEntity
{
    protected EmployeeProcessAccess()
    {
    }

    public EmployeeProcessAccess(Employee employee, Guid processId)
    {
        Employee = employee;
        EmployeeId = employee.Id; // 这里自动提取的就是 Guid
        ProcessId = processId;
    }

    /// <summary>
    /// 关联的员工 UUID (联合主键之一)
    /// </summary>
    public Guid EmployeeId { get; set; }

    /// <summary>
    /// 关联的工序 UUID (联合主键之一)
    /// </summary>
    public Guid ProcessId { get; set; }

    /// <summary>
    /// 导航属性：关联的员工聚合根引用
    /// </summary>
    public Employee Employee { get; set; } = null!;
}