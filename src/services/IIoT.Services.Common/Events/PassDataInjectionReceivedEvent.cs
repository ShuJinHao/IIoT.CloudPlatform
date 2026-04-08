namespace IIoT.Services.Common.Events;

/// <summary>
/// 注液工序过站数据接收事件(单条)。
/// HttpApi 接收上位机上报后发布,DataWorker 消费后落库。
/// 身份信息(MacAddress + ClientCode)随事件透传,
/// 由 Persist 用例在消费侧重新组装为 ClientInstanceId 值对象。
/// </summary>
public record PassDataInjectionReceivedEvent
{
    public Guid DeviceId { get; init; }

    /// <summary>
    /// 上位机宿主机 MAC 地址。
    /// </summary>
    public string MacAddress { get; init; } = string.Empty;

    /// <summary>
    /// 上位机实例编号。
    /// </summary>
    public string ClientCode { get; init; } = string.Empty;

    public string Barcode { get; init; } = string.Empty;
    public string CellResult { get; init; } = string.Empty;
    public DateTime CompletedTime { get; init; }
    public DateTime PreInjectionTime { get; init; }
    public decimal PreInjectionWeight { get; init; }
    public DateTime PostInjectionTime { get; init; }
    public decimal PostInjectionWeight { get; init; }
    public decimal InjectionVolume { get; init; }
}