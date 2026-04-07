using IIoT.SharedKernel.Domain;

namespace IIoT.Core.Production.Aggregates.Capacities;

public class HourlyCapacity : IAggregateRoot
{
    protected HourlyCapacity()
    {
    }

    public HourlyCapacity(
        Guid deviceId,
        string macAddress,
        string clientCode,
        DateOnly date,
        string shiftCode,
        int hour,
        int minute,
        string timeLabel,
        int totalCount,
        int okCount,
        int ngCount,
        string? plcName = null)
    {
        Id = Guid.NewGuid();
        DeviceId = deviceId;
        MacAddress = macAddress;
        ClientCode = clientCode;
        Date = date;
        ShiftCode = shiftCode;
        Hour = hour;
        Minute = minute;
        TimeLabel = timeLabel;
        TotalCount = totalCount;
        OkCount = okCount;
        NgCount = ngCount;
        PlcName = plcName;
        ReportedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }

    public string MacAddress { get; set; } = null!;

    public string ClientCode { get; set; } = null!;

    public DateOnly Date { get; set; }
    public string ShiftCode { get; set; } = null!;
    public int Hour { get; set; }
    public int Minute { get; set; }
    public string TimeLabel { get; set; } = null!;
    public int TotalCount { get; set; }
    public int OkCount { get; set; }
    public int NgCount { get; set; }
    public string? PlcName { get; set; }
    public DateTime ReportedAt { get; set; }

    public void UpdateCapacity(int totalCount, int okCount, int ngCount)
    {
        TotalCount = totalCount;
        OkCount = okCount;
        NgCount = ngCount;
        ReportedAt = DateTime.UtcNow;
    }
}