namespace IIoT.Core.Production.Records.PassStations;

public class PassDataInjection : PassDataBase
{
    protected PassDataInjection()
    {
    }

    public PassDataInjection(
        Guid deviceId,
        string macAddress,
        string clientCode,
        string cellResult,
        DateTime completedTime,
        string barcode,
        DateTime preInjectionTime,
        decimal preInjectionWeight,
        DateTime postInjectionTime,
        decimal postInjectionWeight,
        decimal injectionVolume)
        : base(deviceId, macAddress, clientCode, cellResult, completedTime)
    {
        Barcode = barcode;
        PreInjectionTime = preInjectionTime;
        PreInjectionWeight = preInjectionWeight;
        PostInjectionTime = postInjectionTime;
        PostInjectionWeight = postInjectionWeight;
        InjectionVolume = injectionVolume;
    }

    public string Barcode { get; set; } = null!;

    public DateTime PreInjectionTime { get; set; }

    public decimal PreInjectionWeight { get; set; }

    public DateTime PostInjectionTime { get; set; }

    public decimal PostInjectionWeight { get; set; }

    public decimal InjectionVolume { get; set; }
}