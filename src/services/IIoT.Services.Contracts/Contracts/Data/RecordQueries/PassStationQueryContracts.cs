using IIoT.SharedKernel.Paging;

namespace IIoT.Services.Contracts.RecordQueries;

public static class PassStationQueryModes
{
    public const string BarcodeProcess = "barcode-process";
    public const string TimeProcess = "time-process";
    public const string DeviceBarcode = "device-barcode";
    public const string DeviceTime = "device-time";
    public const string DeviceLatest = "device-latest";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        BarcodeProcess,
        TimeProcess,
        DeviceBarcode,
        DeviceTime,
        DeviceLatest
    };
}

public sealed record PassStationListItemDto(
    Guid Id,
    Guid DeviceId,
    string? Barcode,
    string? CellResult,
    DateTime? CompletedTime,
    DateTime? ReceivedAt,
    Dictionary<string, object?> Fields);

public sealed record PassStationDetailDto(
    Guid Id,
    Guid DeviceId,
    string? Barcode,
    string? CellResult,
    DateTime? CompletedTime,
    DateTime? ReceivedAt,
    Dictionary<string, object?> Fields);

public sealed record PassStationQueryRequest(
    string TypeKey,
    string Mode,
    Pagination Pagination,
    Guid? ProcessId = null,
    Guid? DeviceId = null,
    string? Barcode = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null);

public interface IPassStationQueryDescriptor
{
    string TypeKey { get; }

    IReadOnlySet<string> SupportedModes { get; }

    Task<(List<PassStationListItemDto> Items, int TotalCount)> QueryAsync(
        PassStationQueryRequest request,
        IReadOnlyCollection<Guid>? allowedDeviceIds,
        CancellationToken cancellationToken = default);

    Task<PassStationDetailDto?> GetDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
