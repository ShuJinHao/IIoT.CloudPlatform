using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates;

/// <summary>
/// 聚合根：设备运行日志
/// 边缘端 LogPushTask 定时批量推送设备运行日志到云端
/// </summary>
public class DeviceLog : IAggregateRoot
{
    protected DeviceLog()
    {
    }

    public DeviceLog(Guid deviceId, string level, string message, DateTime logTime)
    {
        Id = Guid.NewGuid();
        DeviceId = deviceId;
        Level = level;
        Message = message;
        LogTime = logTime;
        ReceivedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 日志记录全局唯一标识 (UUID)
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 产生该日志的设备 UUID (关联 devices 表，建索引)
    /// </summary>
    public Guid DeviceId { get; set; }

    /// <summary>
    /// 日志级别 (INFO / WARN / ERROR，建索引)
    /// </summary>
    public string Level { get; set; } = null!;

    /// <summary>
    /// 日志内容
    /// </summary>
    public string Message { get; set; } = null!;

    /// <summary>
    /// 日志在边缘端产生的时间
    /// </summary>
    public DateTime LogTime { get; set; }

    /// <summary>
    /// 云端接收到该日志的时间
    /// </summary>
    public DateTime ReceivedAt { get; set; }
}