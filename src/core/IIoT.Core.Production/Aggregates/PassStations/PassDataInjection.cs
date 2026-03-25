namespace IIoT.Core.Production.Aggregates.PassStations;

/// <summary>
/// 注液工序过站数据 → 映射表 pass_data_injection
/// 注液工序专属字段：条码、注液前后的称重时间和注液量
/// </summary>
public class PassDataInjection : PassDataBase
{
    protected PassDataInjection()
    {
    }

    public PassDataInjection(
        Guid deviceId,
        string cellResult,
        DateTime completedTime,
        string barcode,
        DateTime preInjectionTime,
        decimal preInjectionWeight,
        DateTime postInjectionTime,
        decimal postInjectionWeight,
        decimal injectionVolume)
        : base(deviceId, cellResult, completedTime)
    {
        Barcode = barcode;
        PreInjectionTime = preInjectionTime;
        PreInjectionWeight = preInjectionWeight;
        PostInjectionTime = postInjectionTime;
        PostInjectionWeight = postInjectionWeight;
        InjectionVolume = injectionVolume;
    }

    /// <summary>
    /// 电芯条码 (追溯标识，建索引)
    /// </summary>
    public string Barcode { get; set; } = null!;

    /// <summary>
    /// 注液前时间
    /// </summary>
    public DateTime PreInjectionTime { get; set; }

    /// <summary>
    /// 注液前称重 (单位: g)
    /// </summary>
    public decimal PreInjectionWeight { get; set; }

    /// <summary>
    /// 注液后时间
    /// </summary>
    public DateTime PostInjectionTime { get; set; }

    /// <summary>
    /// 注液后称重 (单位: g)
    /// </summary>
    public decimal PostInjectionWeight { get; set; }

    /// <summary>
    /// 注液量 (单位: ml)
    /// </summary>
    public decimal InjectionVolume { get; set; }
}