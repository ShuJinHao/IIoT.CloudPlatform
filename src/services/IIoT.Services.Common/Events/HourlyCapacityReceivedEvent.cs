namespace IIoT.Services.Common.Events;

/// <summary>
/// 半小时产能数据接收事件。
/// HttpApi 接收上位机上报后发布,DataWorker 消费后落库。
/// 身份信息(MacAddress + ClientCode)随事件透传,
/// 由 Persist 用例在消费侧重新组装为 ClientInstanceId 值对象。
/// </summary>
public record HourlyCapacityReceivedEvent
{
    public Guid DeviceId { get; init; }

    /// <summary>
    /// 上位机宿主机 MAC 地址。
    /// </summary>
    public string MacAddress { get; init; } = string.Empty;

    /// <summary>
    /// 上位机实例编号。同一台宿主机可承载多个实例,
    /// MacAddress + ClientCode 联合构成云端唯一身份。
    /// </summary>
    public string ClientCode { get; init; } = string.Empty;

    public DateOnly Date { get; init; }
    public string ShiftCode { get; init; } = string.Empty;
    public int Hour { get; init; }
    public int Minute { get; init; }
    public string TimeLabel { get; init; } = string.Empty;
    public int TotalCount { get; init; }
    public int OkCount { get; init; }
    public int NgCount { get; init; }

    /// <summary>
    /// 产生该产能数据的 PLC 名称(可空,Edge 端不传时为 null)
    /// </summary>
    public string? PlcName { get; init; }
}