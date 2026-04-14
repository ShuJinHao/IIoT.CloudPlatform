using IIoT.SharedKernel.Paging;

namespace IIoT.Services.Common.Contracts.RecordQueries;

public interface IDeviceLogQueryService
{
    Task<(List<DeviceLogListItemDto> Items, int TotalCount)> GetLogsByConditionAsync(
        Pagination pagination,
        Guid deviceId,
        string? level = null,
        string? keyword = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default);
}

public record DeviceLogListItemDto(
    Guid Id,
    Guid DeviceId,
    string DeviceName,
    string Level,
    string Message,
    DateTime LogTime,
    DateTime ReceivedAt);
