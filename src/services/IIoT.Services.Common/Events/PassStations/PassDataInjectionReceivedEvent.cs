namespace IIoT.Services.Common.Events.PassStations;

/// <summary>
/// 注液过站数据接收事件。
/// </summary>
public record PassDataInjectionReceivedEvent : IPassStationEvent
{
    public Guid DeviceId { get; init; }
    public List<PassDataInjectionItem> Items { get; init; } = [];
}

/// <summary>
/// 单条注液过站数据条目。
/// </summary>
public record PassDataInjectionItem
{
    public string Barcode { get; init; } = string.Empty;
    public string CellResult { get; init; } = string.Empty;
    public DateTime CompletedTime { get; init; }
    public DateTime PreInjectionTime { get; init; }
    public decimal PreInjectionWeight { get; init; }
    public DateTime PostInjectionTime { get; init; }
    public decimal PostInjectionWeight { get; init; }
    public decimal InjectionVolume { get; init; }
}
