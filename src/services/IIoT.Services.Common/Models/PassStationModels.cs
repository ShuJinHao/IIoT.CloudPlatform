namespace IIoT.Services.Common.Models;

/// <summary>
/// 注液工序过站数据请求体
/// </summary>
public record InjectionPassRequest(
    Guid DeviceId,
    string Barcode,
    string CellResult,
    DateTime CompletedTime,
    DateTime PreInjectionTime,
    decimal PreInjectionWeight,
    DateTime PostInjectionTime,
    decimal PostInjectionWeight,
    decimal InjectionVolume
);