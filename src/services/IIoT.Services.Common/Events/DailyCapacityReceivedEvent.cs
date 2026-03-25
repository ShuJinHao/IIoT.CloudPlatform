namespace IIoT.Services.Common.Events;

public record DailyCapacityReceivedEvent
{
    public Guid DeviceId { get; init; }
    public DateOnly Date { get; init; }
    public string ShiftCode { get; init; } = string.Empty;
    public int TotalCount { get; init; }
    public int OkCount { get; init; }
    public int NgCount { get; init; }
}