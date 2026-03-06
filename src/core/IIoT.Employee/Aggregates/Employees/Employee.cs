using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Employee.Aggregates.Employees;

/// <summary>
/// 聚合根：员工/操作员
/// </summary>
public class Employee : IAggregateRoot
{
    // 员工关联的工序权限私有集合
    private readonly List<EmployeeProcessAccess> _processAccesses = [];

    protected Employee()
    {
    }

    public Employee(string employeeNo, string realName, string passwordHash)
    {
        Id = Guid.NewGuid(); // 实例化时直接生成 Guid
        EmployeeNo = employeeNo;
        RealName = realName;
        PasswordHash = passwordHash;
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
    /// 登录密码的哈希值 (禁止明文存储)
    /// </summary>
    public string PasswordHash { get; set; } = null!;

    /// <summary>
    /// 账号状态标识 (true: 正常启用; false: 已离职或冻结，禁止登录设备)
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 该员工允许操作的工序权限列表 (对外暴露为只读，保证聚合根数据的一致性)
    /// </summary>
    public IReadOnlyCollection<EmployeeProcessAccess> ProcessAccesses => _processAccesses.AsReadOnly();

    /// <summary>
    /// 为该员工分配新的工序操作权限
    /// </summary>
    /// <param name="processId">工序的 UUID</param>
    public void AddProcessAccess(Guid processId)
    {
        // 避免重复添加权限 (幂等性校验)
        if (!_processAccesses.Any(x => x.ProcessId == processId))
        {
            var access = new EmployeeProcessAccess(this, processId);
            _processAccesses.Add(access);
        }
    }

    /// <summary>
    /// 移除该员工的某个工序操作权限
    /// </summary>
    /// <param name="processId">工序的 UUID</param>
    public void RemoveProcessAccess(Guid processId)
    {
        var access = _processAccesses.FirstOrDefault(x => x.ProcessId == processId);
        if (access != null)
        {
            _processAccesses.Remove(access);
        }
    }
}