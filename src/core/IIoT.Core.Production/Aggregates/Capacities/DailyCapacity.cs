using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.Capacities;

/// <summary>
/// 聚合根：每日产能汇总记录
/// 边缘端 DailyReportTask 每天定时上报当日汇总数据
/// 唯一约束：DeviceId + Date + ShiftCode (同一设备同一天同一班次不允许重复，支持 Upsert 覆盖)
/// </summary>
public class DailyCapacity : IAggregateRoot
{
    protected DailyCapacity()
    {
    }

    public DailyCapacity(Guid deviceId, DateOnly date, string shiftCode, int totalCount, int okCount, int ngCount)
    {
        Id = Guid.NewGuid();
        DeviceId = deviceId;
        Date = date;
        ShiftCode = shiftCode;
        TotalCount = totalCount;
        OkCount = okCount;
        NgCount = ngCount;
        ReportedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 产能记录全局唯一标识 (UUID)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 上报设备 UUID (关联 devices 表)
    /// </summary>
    public Guid DeviceId { get; set; }

    /// <summary>
    /// 统计日期 (如: 2026-03-25)
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// 班次编码 (如: DAY / NIGHT / A / B，由工厂自行定义)
    /// </summary>
    public string ShiftCode { get; set; } = null!;

    /// <summary>
    /// 当日该班次总产出数量
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 良品数量
    /// </summary>
    public int OkCount { get; set; }

    /// <summary>
    /// 不良品数量
    /// </summary>
    public int NgCount { get; set; }

    /// <summary>
    /// 云端接收到上报数据的时间
    /// </summary>
    public DateTime ReportedAt { get; set; }

    /// <summary>
    /// 领域行为：边缘端重发时覆盖更新产能数据
    /// </summary>
    public void UpdateCapacity(int totalCount, int okCount, int ngCount)
    {
        TotalCount = totalCount;
        OkCount = okCount;
        NgCount = ngCount;
        ReportedAt = DateTime.UtcNow;
    }
}