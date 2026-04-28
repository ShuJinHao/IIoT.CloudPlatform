namespace IIoT.Services.Contracts.Events.Capacities;

/// <summary>
/// 半小时产能接收事件。
/// </summary>
public record HourlyCapacityReceivedEvent
{
    public int SchemaVersion { get; init; } = 1;

    public Guid DeviceId { get; init; }

    public DateOnly Date { get; init; }
    public string ShiftCode { get; init; } = string.Empty;
    public int Hour { get; init; }
    public int Minute { get; init; }
    public string TimeLabel { get; init; } = string.Empty;
    public int TotalCount { get; init; }
    public int OkCount { get; init; }
    public int NgCount { get; init; }

    /// <summary>
    /// 产生该产能数据的 PLC 名称。
    /// </summary>
    public string? PlcName { get; init; }

    public DateTime ReceivedAtUtc { get; init; }
}
