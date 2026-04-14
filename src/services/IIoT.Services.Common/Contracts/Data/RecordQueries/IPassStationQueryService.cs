using IIoT.SharedKernel.Paging;

namespace IIoT.Services.Common.Contracts.RecordQueries;

public interface IPassStationQueryService<TDto>
{
    Task<(List<TDto> Items, int TotalCount)> GetByConditionAsync(
        Pagination pagination,
        List<Guid>? deviceIds = null,
        Guid? deviceId = null,
        string? barcode = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default);

    Task<TDto?> GetDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<(List<TDto> Items, int TotalCount)> GetLatest200ByDeviceAsync(
        Guid deviceId,
        Pagination pagination,
        CancellationToken cancellationToken = default);
}

public record InjectionPassListItemDto(
    Guid Id,
    Guid DeviceId,
    string Barcode,
    string CellResult,
    DateTime PreInjectionTime,
    decimal PreInjectionWeight,
    DateTime PostInjectionTime,
    decimal PostInjectionWeight,
    decimal InjectionVolume,
    DateTime CompletedTime,
    DateTime ReceivedAt);

public record InjectionPassDetailDto(
    Guid Id,
    Guid DeviceId,
    string CellResult,
    DateTime CompletedTime,
    DateTime ReceivedAt,
    string Barcode,
    DateTime PreInjectionTime,
    decimal PreInjectionWeight,
    DateTime PostInjectionTime,
    decimal PostInjectionWeight,
    decimal InjectionVolume);
