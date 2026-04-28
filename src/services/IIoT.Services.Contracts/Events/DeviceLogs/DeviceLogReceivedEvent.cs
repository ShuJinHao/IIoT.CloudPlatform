using IIoT.Services.Contracts;

namespace IIoT.Services.Contracts.Events.DeviceLogs;

/// <summary>
/// 设备日志接收事件。
/// </summary>
public record DeviceLogReceivedEvent : IIntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// 本批日志归属的设备 ID。
    /// </summary>
    public Guid DeviceId { get; init; }

    /// <summary>
    /// 本批次的日志列表。
    /// </summary>
    public List<DeviceLogItem> Logs { get; init; } = [];
}

/// <summary>
/// 单条设备日志条目。
/// </summary>
public record DeviceLogItem
{
    public string Level { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime LogTime { get; init; }
}
