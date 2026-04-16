namespace IIoT.Core.Production.Contracts.PassStation;

public sealed record StackingWriteModel(
    Guid Id,
    Guid DeviceId,
    string Barcode,
    string TrayCode,
    int SequenceNo,
    int LayerCount,
    string CellResult,
    DateTime CompletedTime,
    DateTime ReceivedAt
) : IPassStationWriteModel;
