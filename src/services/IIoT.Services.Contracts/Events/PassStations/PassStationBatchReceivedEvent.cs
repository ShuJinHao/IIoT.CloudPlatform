namespace IIoT.Services.Contracts.Events.PassStations;

/// <summary>
/// 通用过站批次接收事件。
/// 公共检索字段保持显式属性，工序专属字段保存在 PayloadJson 中。
/// </summary>
public sealed record PassStationBatchReceivedEvent : IPassStationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public int SchemaVersion { get; init; } = 1;

    public Guid DeviceId { get; init; }

    public string TypeKey { get; init; } = string.Empty;

    public string ProcessType { get; init; } = string.Empty;

    public IReadOnlyList<PassStationBatchItem> Items { get; init; } = [];
}

public sealed record PassStationBatchItem
{
    public string Barcode { get; init; } = string.Empty;

    public string CellResult { get; init; } = string.Empty;

    public DateTime CompletedTime { get; init; }

    public string PayloadJson { get; init; } = "{}";

    public string DeduplicationKey { get; init; } = string.Empty;
}
