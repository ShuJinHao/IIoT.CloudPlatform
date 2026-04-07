namespace IIoT.Core.Production.Records.Capacities;

/// <summary>
/// 每日产能汇总记录
/// </summary>
public class DailyCapacity
{
    protected DailyCapacity()
    {
    }

    public DailyCapacity(
        Guid deviceId,
        string macAddress,
        string clientCode,
        DateOnly date,
        string shiftCode,
        int totalCount,
        int okCount,
        int ngCount)
    {
        Id = Guid.NewGuid();
        DeviceId = deviceId;
        MacAddress = macAddress;
        ClientCode = clientCode;
        Date = date;
        ShiftCode = shiftCode;
        TotalCount = totalCount;
        OkCount = okCount;
        NgCount = ngCount;
        ReportedAt = DateTime.UtcNow;
    }

    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }

    public string MacAddress { get; set; } = null!;

    public string ClientCode { get; set; } = null!;

    public DateOnly Date { get; set; }

    public string ShiftCode { get; set; } = null!;

    public int TotalCount { get; set; }

    public int OkCount { get; set; }

    public int NgCount { get; set; }

    public DateTime ReportedAt { get; set; }

    public void UpdateCapacity(int totalCount, int okCount, int ngCount)
    {
        TotalCount = totalCount;
        OkCount = okCount;
        NgCount = ngCount;
        ReportedAt = DateTime.UtcNow;
    }
}