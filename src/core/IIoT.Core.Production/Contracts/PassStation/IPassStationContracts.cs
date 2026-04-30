namespace IIoT.Core.Production.Contracts.PassStation;

public interface IPassStationWriteModel;

public interface IPassStationRecordRepository
{
    Task InsertBatchAsync(
        IReadOnlyCollection<PassStationRecordWriteModel> items,
        CancellationToken cancellationToken = default);
}

public sealed record PassStationRecordWriteModel(
    Guid Id,
    Guid DeviceId,
    string TypeKey,
    string Barcode,
    string CellResult,
    DateTime CompletedTime,
    DateTime ReceivedAt,
    string DeduplicationKey,
    string PayloadJson) : IPassStationWriteModel;
