namespace IIoT.Services.Common.Events;

public record PassDataInjectionReceivedEvent
{
    public Guid DeviceId { get; init; }
    public string Barcode { get; init; } = string.Empty;
    public string CellResult { get; init; } = string.Empty;
    public DateTime CompletedTime { get; init; }
    public DateTime PreInjectionTime { get; init; }
    public decimal PreInjectionWeight { get; init; }
    public DateTime PostInjectionTime { get; init; }
    public decimal PostInjectionWeight { get; init; }
    public decimal InjectionVolume { get; init; }
}