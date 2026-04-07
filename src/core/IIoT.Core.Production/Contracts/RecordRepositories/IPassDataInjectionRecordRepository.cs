using IIoT.Core.Production.ValueObjects;

namespace IIoT.Core.Production.Contracts.RecordRepositories;

/// <summary>
/// 注液过站记录写入模型。
/// 幂等性由表层唯一索引 (device_id, barcode, completed_time) + ON CONFLICT DO NOTHING 保证,
/// 调用方无需先查后写。
/// </summary>
public sealed record PassDataInjectionWriteModel(
    Guid Id,
    Guid DeviceId,
    ClientInstanceId Instance,
    string CellResult,
    DateTime CompletedTime,
    DateTime ReceivedAt,
    string Barcode,
    DateTime PreInjectionTime,
    decimal PreInjectionWeight,
    DateTime PostInjectionTime,
    decimal PostInjectionWeight,
    decimal InjectionVolume);

public interface IPassDataInjectionRecordRepository
{
    Task InsertAsync(
        PassDataInjectionWriteModel item,
        CancellationToken cancellationToken = default);
}