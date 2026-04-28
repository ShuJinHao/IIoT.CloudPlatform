namespace IIoT.Services.Contracts.RecordQueries;

public record StackingPassListItemDto(
    Guid Id,
    Guid DeviceId,
    string Barcode,
    string TrayCode,
    int SequenceNo,
    int LayerCount,
    string CellResult,
    DateTime CompletedTime,
    DateTime ReceivedAt);

public record StackingPassDetailDto(
    Guid Id,
    Guid DeviceId,
    string Barcode,
    string TrayCode,
    int SequenceNo,
    int LayerCount,
    string CellResult,
    DateTime CompletedTime,
    DateTime ReceivedAt);
