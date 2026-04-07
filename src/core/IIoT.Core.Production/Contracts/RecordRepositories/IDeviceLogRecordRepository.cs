using IIoT.Core.Production.ValueObjects;

namespace IIoT.Core.Production.Contracts.RecordRepositories;

/// <summary>
/// 设备日志写入模型。身份通过 ClientInstanceId 值对象表达,
/// 由 Dapper 实现在 SQL 绑参的最后一刻拆成 mac_address + client_code 两列。
/// </summary>
public sealed record DeviceLogWriteModel(
    Guid Id,
    Guid DeviceId,
    ClientInstanceId Instance,
    string Level,
    string Message,
    DateTime LogTime,
    DateTime ReceivedAt);

public interface IDeviceLogRecordRepository
{
    Task InsertBatchAsync(
        IReadOnlyCollection<DeviceLogWriteModel> items,
        CancellationToken cancellationToken = default);
}