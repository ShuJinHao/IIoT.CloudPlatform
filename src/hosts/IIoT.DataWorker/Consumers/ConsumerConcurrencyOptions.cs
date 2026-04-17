namespace IIoT.DataWorker.Consumers;

public sealed class ConsumerConcurrencyOptions
{
    public const string SectionName = "Consumers";

    public int PassStationConcurrentMessageLimit { get; set; } = 4;
    public int DeviceLogConcurrentMessageLimit { get; set; } = 3;
    public int HourlyCapacityConcurrentMessageLimit { get; set; } = 1;
}
