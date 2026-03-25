namespace IIoT.Services.Common.Events;

public record DeviceLogReceivedEvent
{
    public List<DeviceLogItem> Logs { get; init; } = [];
}

public record DeviceLogItem
{
    public Guid DeviceId { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTime LogTime { get; init; }
}