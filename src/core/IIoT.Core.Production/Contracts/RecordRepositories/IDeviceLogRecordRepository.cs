namespace IIoT.Core.Production.Contracts.RecordRepositories;

/// <summary>
/// 设备日志写模型约定，身份仅由 device_id 表示。
/// </summary>
public sealed record DeviceLogWriteModel(
    Guid Id,
    Guid DeviceId,
    string Level,
    string Message,
    DateTime LogTime,
    DateTime ReceivedAt,
    string IdempotencyKey);

public interface IDeviceLogRecordRepository
{
    Task InsertBatchAsync(
        IReadOnlyCollection<DeviceLogWriteModel> items,
        CancellationToken cancellationToken = default);
}
