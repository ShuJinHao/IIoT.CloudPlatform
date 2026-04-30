using IIoT.SharedKernel.Domain;

namespace IIoT.Core.MasterData.Aggregates.MfgProcesses;

/// <summary>
/// 聚合根:制造工序(权限挂载的核心锚点)
/// </summary>
public class MfgProcess : BaseEntity<Guid>
{
    /// <summary>
    /// 仅供 EF Core 物化使用,业务代码不要调用。
    /// </summary>
    protected MfgProcess() { }

    public MfgProcess(string processCode, string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        Id = Guid.NewGuid();
        ProcessCode = processCode.Trim();
        ProcessName = processName.Trim();
    }

    /// <summary>
    /// 工序系统编码 (如:Stacking, Injection 等)
    /// </summary>
    public string ProcessCode { get; private set; } = null!;

    /// <summary>
    /// 工序显示名称 (如:叠片工序, 注液工序)
    /// </summary>
    public string ProcessName { get; private set; } = null!;

    public uint RowVersion { get; private set; }

    // ── 行为 ──────────────────────────────────────────────

    /// <summary>
    /// 修改工序的编码和显示名称。
    /// 业务上工序的"重命名"是 Code + Name 同时变更的原子操作。
    /// </summary>
    public void Rename(string newCode, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var normalizedCode = newCode.Trim();
        var normalizedName = newName.Trim();
        if (ProcessCode == normalizedCode && ProcessName == normalizedName)
        {
            return;
        }

        var oldCode = ProcessCode;
        var oldName = ProcessName;

        ProcessCode = normalizedCode;
        ProcessName = normalizedName;
        AddDomainEvent(new Events.MfgProcessRenamedDomainEvent(
            Id,
            oldCode,
            ProcessCode,
            oldName,
            ProcessName));
    }
}
