using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Employee.Aggregates.MfgProcesses;

/// <summary>
/// 聚合根：制造工序 (权限挂载的核心锚点)
/// </summary>
public class MfgProcess : IAggregateRoot
{
    protected MfgProcess()
    {
    }

    public MfgProcess(string processCode, string processName)
    {
        Id = Guid.NewGuid(); // 实例化时直接生成 Guid
        ProcessCode = processCode;
        ProcessName = processName;
    }

    /// <summary>
    /// 工序全局唯一标识 (UUID)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 工序系统编码 (如：Stacking, Injection 等，通常用于程序内部枚举或校验)
    /// </summary>
    public string ProcessCode { get; set; } = null!;

    /// <summary>
    /// 工序显示名称 (如：叠片工序, 注液工序，主要用于前端 UI 展示)
    /// </summary>
    public string ProcessName { get; set; } = null!;
}