using IIoT.Core.Production.ValueObjects;

namespace IIoT.Core.Production.Contracts.RecordRepositories;

/// <summary>
/// 半小时产能写入模型。
/// 唯一键粒度: Instance + Date + ShiftCode + Hour + Minute + PlcName。
/// PlcName 为空时由 Dapper 实现统一写入 "" (空字符串),避免 PostgreSQL 唯一索引对 NULL 的特殊行为。
/// </summary>
public sealed record HourlyCapacityWriteModel(
    Guid Id,
    Guid DeviceId,
    ClientInstanceId Instance,
    DateOnly Date,
    string ShiftCode,
    int Hour,
    int Minute,
    string TimeLabel,
    int TotalCount,
    int OkCount,
    int NgCount,
    string PlcName,
    DateTime ReportedAt);

public interface IHourlyCapacityRecordRepository
{
    Task UpsertAsync(
        HourlyCapacityWriteModel item,
        CancellationToken cancellationToken = default);
}