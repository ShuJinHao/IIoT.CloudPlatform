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

public sealed class PassStationTypeDefinitionDto
{
    public string TypeKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<PassStationFieldDefinitionDto> Fields { get; set; } = [];

    public List<string> ListColumns { get; set; } = [];

    public List<PassStationDetailSectionDto> DetailSections { get; set; } = [];

    public List<string> SupportedModes { get; set; } = [];
}

public sealed class PassStationFieldDefinitionDto
{
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public bool Required { get; set; }

    public int? MaxLength { get; set; }

    public decimal? Min { get; set; }

    public decimal? Max { get; set; }

    public string? Unit { get; set; }

    public int? Precision { get; set; }

    public List<string>? Options { get; set; }
}

public sealed class PassStationDetailSectionDto
{
    public string Title { get; set; } = string.Empty;

    public List<string> Fields { get; set; } = [];
}

public sealed record PassStationQueryRequest(
    string TypeKey,
    string Mode,
    Pagination Pagination,
    Guid? ProcessId = null,
    Guid? DeviceId = null,
    string? Barcode = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null);

public interface IPassStationSchemaProvider
{
    IReadOnlyList<PassStationTypeDefinitionDto> GetAll();

    PassStationTypeDefinitionDto? Find(string typeKey);
}

public interface IPassStationRecordQueryService
{
    Task<(List<PassStationListItemDto> Items, int TotalCount)> GetByConditionAsync(
        PassStationQueryRequest request,
        IReadOnlyCollection<Guid>? allowedDeviceIds,
        CancellationToken cancellationToken = default);

    Task<PassStationDetailDto?> GetDetailAsync(
        string typeKey,
        Guid id,
        CancellationToken cancellationToken = default);
}
