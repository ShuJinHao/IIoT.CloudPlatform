using IIoT.SharedKernel.Paging;

namespace IIoT.Services.Contracts.RecordQueries;

public sealed record AiProductionRecordQueryRequest(
    Pagination Pagination,
    DateTime StartTime,
    DateTime EndTime,
    string? TypeKey = null,
    Guid? ProcessId = null,
    Guid? DeviceId = null,
    string? Barcode = null,
    string? Result = null);

public sealed record AiProductionRecordQueryItem(
    Guid Id,
    string TypeKey,
    Guid DeviceId,
    string DeviceName,
    string? Barcode,
    string? Result,
    DateTime? CompletedTime,
    DateTime? ReceivedAt,
    Dictionary<string, object?> Fields);

public interface IAiProductionRecordQueryService
{
    Task<(List<AiProductionRecordQueryItem> Items, int TotalCount)> GetAsync(
        AiProductionRecordQueryRequest request,
        IReadOnlyCollection<Guid>? allowedDeviceIds,
        CancellationToken cancellationToken = default);
}
