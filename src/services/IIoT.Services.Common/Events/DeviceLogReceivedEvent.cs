namespace IIoT.Services.Common.Events;

/// <summary>
/// 设备日志接收事件(批量)。
/// HttpApi 接收上位机上报后发布,DataWorker 消费后落库。
/// 一次事件来自同一台上位机,因此身份信息(MacAddress + ClientCode)
/// 在事件顶层共享,Logs 集合内每条 Item 不再重复。
/// </summary>
public record DeviceLogReceivedEvent
{
    /// <summary>
    /// 上位机宿主机 MAC 地址(整批日志共享)。
    /// </summary>
    public string MacAddress { get; init; } = string.Empty;

    /// <summary>
    /// 上位机实例编号(整批日志共享)。
    /// </summary>
    public string ClientCode { get; init; } = string.Empty;

    /// <summary>
    /// 本批次的日志列表。
    /// </summary>
    public List<DeviceLogItem> Logs { get; init; } = [];
}

/// <summary>
/// 单条设备日志条目。
/// 不带身份信息 — 整批共享 <see cref="DeviceLogReceivedEvent.MacAddress"/> +
/// <see cref="DeviceLogReceivedEvent.ClientCode"/>。
/// </summary>
public record DeviceLogItem
{
    public Guid DeviceId { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime LogTime { get; init; }
}