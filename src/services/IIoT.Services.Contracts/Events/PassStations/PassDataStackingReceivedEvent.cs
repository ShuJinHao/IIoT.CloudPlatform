namespace IIoT.Services.Contracts.Events.PassStations;

public record PassDataStackingReceivedEvent : IPassStationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public int SchemaVersion { get; init; } = 1;

    public Guid DeviceId { get; init; }
    public PassDataStackingItem Item { get; init; } = new();
}

public record PassDataStackingItem
{
    public string Barcode { get; init; } = string.Empty;
    public string TrayCode { get; init; } = string.Empty;
    public int LayerCount { get; init; }
    public int SequenceNo { get; init; }
    public string CellResult { get; init; } = string.Empty;
    public DateTime CompletedTime { get; init; }
}
