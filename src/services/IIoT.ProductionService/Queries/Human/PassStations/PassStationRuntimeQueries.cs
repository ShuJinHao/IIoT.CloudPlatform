using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.PassStations;

[AuthorizeRequirement("Device.Read")]
public record GetPassStationListByTypeQuery(
    PassStationQueryRequest Request
) : IHumanQuery<Result<PagedList<PassStationListItemDto>>>;

public sealed class GetPassStationListByTypeHandler(
    IEnumerable<IPassStationQueryDescriptor> descriptors,
    ICurrentUser currentUser,
    IDevicePermissionService devicePermissionService,
    IProcessReadQueryService processReadQueryService)
    : IQueryHandler<GetPassStationListByTypeQuery, Result<PagedList<PassStationListItemDto>>>
{
    public async Task<Result<PagedList<PassStationListItemDto>>> Handle(
        GetPassStationListByTypeQuery request,
        CancellationToken cancellationToken)
    {
        var descriptor = PassStationQueryRuntime.ResolveDescriptor(descriptors, request.Request.TypeKey);
        if (descriptor is null)
            return Result.NotFound($"过站类型 [{request.Request.TypeKey}] 不存在。");

        var validation = PassStationQueryRuntime.ValidateListRequest(request.Request, descriptor);
        if (validation is not null)
            return validation;

        var allowedDeviceIds = await PassStationQueryRuntime.ResolveAllowedDeviceIdsAsync(
            request.Request,
            currentUser,
            devicePermissionService,
            processReadQueryService,
            cancellationToken);
        if (!allowedDeviceIds.IsSuccess)
            return Result.Invalid(allowedDeviceIds.ErrorMessage!);
        if (allowedDeviceIds.ShouldReturnEmpty)
            return Result.Success(new PagedList<PassStationListItemDto>([], 0, request.Request.Pagination));

        var (items, totalCount) = await descriptor.QueryAsync(
            request.Request,
            allowedDeviceIds.DeviceIds,
            cancellationToken);

        return Result.Success(new PagedList<PassStationListItemDto>(items, totalCount, request.Request.Pagination));
    }
}

[AuthorizeRequirement("Device.Read")]
public record GetPassStationDetailByTypeQuery(
    string TypeKey,
    Guid Id
) : IHumanQuery<Result<PassStationDetailDto>>;

public sealed class GetPassStationDetailByTypeHandler(
    IEnumerable<IPassStationQueryDescriptor> descriptors,
    ICurrentUser currentUser,
    IDevicePermissionService devicePermissionService)
    : IQueryHandler<GetPassStationDetailByTypeQuery, Result<PassStationDetailDto>>
{
    public async Task<Result<PassStationDetailDto>> Handle(
        GetPassStationDetailByTypeQuery request,
        CancellationToken cancellationToken)
    {
        var descriptor = PassStationQueryRuntime.ResolveDescriptor(descriptors, request.TypeKey);
        if (descriptor is null)
            return Result.NotFound($"过站类型 [{request.TypeKey}] 不存在。");

        var detail = await descriptor.GetDetailAsync(request.Id, cancellationToken);
        if (detail is null)
            return Result.NotFound("未找到该过站记录。");

        if (string.Equals(currentUser.Role, SystemRoles.Admin, StringComparison.Ordinal))
            return Result.Success(detail);

        if (!Guid.TryParse(currentUser.Id, out var userId))
            return Result.Invalid("用户凭证异常。");

        var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
            userId,
            isAdmin: false,
            cancellationToken);
        if (accessibleDeviceIds is null || !accessibleDeviceIds.Contains(detail.DeviceId))
            return Result.Forbidden();

        return Result.Success(detail);
    }
}

public sealed class InjectionPassStationQueryDescriptor(
    IPassStationQueryService<InjectionPassListItemDto> listQueryService,
    IPassStationQueryService<InjectionPassDetailDto> detailQueryService)
    : IPassStationQueryDescriptor
{
    public string TypeKey => "injection";

    public IReadOnlySet<string> SupportedModes => PassStationQueryModes.All;

    public async Task<(List<PassStationListItemDto> Items, int TotalCount)> QueryAsync(
        PassStationQueryRequest request,
        IReadOnlyCollection<Guid>? allowedDeviceIds,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(request.Mode, PassStationQueryModes.DeviceLatest, StringComparison.Ordinal))
        {
            var latest = await listQueryService.GetLatest200ByDeviceAsync(
                request.DeviceId!.Value,
                request.Pagination,
                cancellationToken);

            return (latest.Items.Select(MapListItem).ToList(), latest.TotalCount);
        }

        var response = await listQueryService.GetByConditionAsync(
            request.Pagination,
            deviceIds: allowedDeviceIds?.ToList(),
            deviceId: request.DeviceId,
            barcode: request.Barcode,
            startTime: request.StartTime,
            endTime: request.EndTime,
            cancellationToken: cancellationToken);

        return (response.Items.Select(MapListItem).ToList(), response.TotalCount);
    }

    public async Task<PassStationDetailDto?> GetDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var detail = await detailQueryService.GetDetailAsync(id, cancellationToken);
        return detail is null ? null : MapDetail(detail);
    }

    private static PassStationListItemDto MapListItem(InjectionPassListItemDto item)
    {
        return new PassStationListItemDto(
            item.Id,
            item.DeviceId,
            item.Barcode,
            item.CellResult,
            item.CompletedTime,
            item.ReceivedAt,
            new Dictionary<string, object?>
            {
                ["preInjectionTime"] = item.PreInjectionTime,
                ["preInjectionWeight"] = item.PreInjectionWeight,
                ["postInjectionTime"] = item.PostInjectionTime,
                ["postInjectionWeight"] = item.PostInjectionWeight,
                ["injectionVolume"] = item.InjectionVolume
            });
    }

    private static PassStationDetailDto MapDetail(InjectionPassDetailDto item)
    {
        return new PassStationDetailDto(
            item.Id,
            item.DeviceId,
            item.Barcode,
            item.CellResult,
            item.CompletedTime,
            item.ReceivedAt,
            new Dictionary<string, object?>
            {
                ["preInjectionTime"] = item.PreInjectionTime,
                ["preInjectionWeight"] = item.PreInjectionWeight,
                ["postInjectionTime"] = item.PostInjectionTime,
                ["postInjectionWeight"] = item.PostInjectionWeight,
                ["injectionVolume"] = item.InjectionVolume
            });
    }
}

public sealed class StackingPassStationQueryDescriptor(
    IPassStationQueryService<StackingPassListItemDto> listQueryService,
    IPassStationQueryService<StackingPassDetailDto> detailQueryService)
    : IPassStationQueryDescriptor
{
    public string TypeKey => "stacking";

    public IReadOnlySet<string> SupportedModes => PassStationQueryModes.All;

    public async Task<(List<PassStationListItemDto> Items, int TotalCount)> QueryAsync(
        PassStationQueryRequest request,
        IReadOnlyCollection<Guid>? allowedDeviceIds,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(request.Mode, PassStationQueryModes.DeviceLatest, StringComparison.Ordinal))
        {
            var latest = await listQueryService.GetLatest200ByDeviceAsync(
                request.DeviceId!.Value,
                request.Pagination,
                cancellationToken);

            return (latest.Items.Select(MapListItem).ToList(), latest.TotalCount);
        }

        var response = await listQueryService.GetByConditionAsync(
            request.Pagination,
            deviceIds: allowedDeviceIds?.ToList(),
            deviceId: request.DeviceId,
            barcode: request.Barcode,
            startTime: request.StartTime,
            endTime: request.EndTime,
            cancellationToken: cancellationToken);

        return (response.Items.Select(MapListItem).ToList(), response.TotalCount);
    }

    public async Task<PassStationDetailDto?> GetDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var detail = await detailQueryService.GetDetailAsync(id, cancellationToken);
        return detail is null ? null : MapDetail(detail);
    }

    private static PassStationListItemDto MapListItem(StackingPassListItemDto item)
    {
        return new PassStationListItemDto(
            item.Id,
            item.DeviceId,
            item.Barcode,
            item.CellResult,
            item.CompletedTime,
            item.ReceivedAt,
            new Dictionary<string, object?>
            {
                ["trayCode"] = item.TrayCode,
                ["sequenceNo"] = item.SequenceNo,
                ["layerCount"] = item.LayerCount
            });
    }

    private static PassStationDetailDto MapDetail(StackingPassDetailDto item)
    {
        return new PassStationDetailDto(
            item.Id,
            item.DeviceId,
            item.Barcode,
            item.CellResult,
            item.CompletedTime,
            item.ReceivedAt,
            new Dictionary<string, object?>
            {
                ["trayCode"] = item.TrayCode,
                ["sequenceNo"] = item.SequenceNo,
                ["layerCount"] = item.LayerCount
            });
    }
}

internal sealed record AllowedDeviceResolution(
    IReadOnlyCollection<Guid>? DeviceIds,
    bool ShouldReturnEmpty,
    string? ErrorMessage)
{
    public bool IsSuccess => ErrorMessage is null;
}

internal static class PassStationQueryRuntime
{
    public static IPassStationQueryDescriptor? ResolveDescriptor(
        IEnumerable<IPassStationQueryDescriptor> descriptors,
        string rawTypeKey)
    {
        var typeKey = NormalizeTypeKey(rawTypeKey);
        return descriptors.FirstOrDefault(x => string.Equals(x.TypeKey, typeKey, StringComparison.Ordinal));
    }

    public static Result<PagedList<PassStationListItemDto>>? ValidateListRequest(
        PassStationQueryRequest request,
        IPassStationQueryDescriptor descriptor)
    {
        if (!descriptor.SupportedModes.Contains(request.Mode))
            return Result.Invalid($"过站查询模式 [{request.Mode}] 不支持类型 [{request.TypeKey}]。");

        if (!PassStationQueryModes.All.Contains(request.Mode))
            return Result.Invalid($"过站查询模式 [{request.Mode}] 无效。");

        if (string.Equals(request.Mode, PassStationQueryModes.BarcodeProcess, StringComparison.Ordinal)
            && (request.ProcessId is null || string.IsNullOrWhiteSpace(request.Barcode)))
        {
            return Result.Invalid("查询模式 barcode-process 需要提供工序和条码。");
        }

        if (string.Equals(request.Mode, PassStationQueryModes.TimeProcess, StringComparison.Ordinal)
            && (request.ProcessId is null || request.StartTime is null || request.EndTime is null))
        {
            return Result.Invalid("查询模式 time-process 需要提供工序、开始时间和结束时间。");
        }

        if (string.Equals(request.Mode, PassStationQueryModes.DeviceBarcode, StringComparison.Ordinal)
            && (request.DeviceId is null || string.IsNullOrWhiteSpace(request.Barcode)))
        {
            return Result.Invalid("查询模式 device-barcode 需要提供设备和条码。");
        }

        if (string.Equals(request.Mode, PassStationQueryModes.DeviceTime, StringComparison.Ordinal)
            && (request.DeviceId is null || request.StartTime is null || request.EndTime is null))
        {
            return Result.Invalid("查询模式 device-time 需要提供设备、开始时间和结束时间。");
        }

        if (string.Equals(request.Mode, PassStationQueryModes.DeviceLatest, StringComparison.Ordinal)
            && request.DeviceId is null)
        {
            return Result.Invalid("查询模式 device-latest 需要提供设备。");
        }

        if (request.StartTime is not null && request.EndTime is not null && request.StartTime > request.EndTime)
        {
            return Result.Invalid("开始时间不能晚于结束时间。");
        }

        return null;
    }

    public static async Task<AllowedDeviceResolution> ResolveAllowedDeviceIdsAsync(
        PassStationQueryRequest request,
        ICurrentUser currentUser,
        IDevicePermissionService devicePermissionService,
        IProcessReadQueryService processReadQueryService,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<Guid>? allowedDeviceIds = null;

        if (!string.Equals(currentUser.Role, SystemRoles.Admin, StringComparison.Ordinal))
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return new AllowedDeviceResolution(null, false, "Current user identity is invalid.");

            allowedDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
                userId,
                isAdmin: false,
                cancellationToken);

            if (request.DeviceId.HasValue
                && (allowedDeviceIds is null || !allowedDeviceIds.Contains(request.DeviceId.Value)))
            {
                return new AllowedDeviceResolution(null, false, "Unauthorized device access.");
            }
        }

        if (request.ProcessId.HasValue)
        {
            var processDeviceIds = (await processReadQueryService.GetDeviceIdsAsync(
                request.ProcessId.Value,
                cancellationToken)).ToList();

            if (processDeviceIds.Count == 0)
                return new AllowedDeviceResolution(null, false, "The selected process does not have any devices.");

            if (allowedDeviceIds is not null)
            {
                processDeviceIds = processDeviceIds.Intersect(allowedDeviceIds).ToList();
                if (processDeviceIds.Count == 0)
                    return new AllowedDeviceResolution([], true, null);
            }

            return new AllowedDeviceResolution(processDeviceIds, false, null);
        }

        return new AllowedDeviceResolution(null, false, null);
    }

    public static string NormalizeTypeKey(string rawTypeKey)
    {
        return rawTypeKey?.Trim().ToLowerInvariant() ?? string.Empty;
    }
}
