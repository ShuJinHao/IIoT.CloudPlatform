using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Aggregates.Recipes;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Core.Production.Specifications.Recipes;
using IIoT.ProductionService.AiRead;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.AiRead;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;
using Microsoft.Extensions.Options;

namespace IIoT.ProductionService.Queries.AiRead;

public sealed record AiReadDeviceDto(
    Guid Id,
    string DeviceName,
    Guid ProcessId);

public sealed record AiReadCapacitySummaryDto(
    DateOnly Date,
    int TotalCount,
    int OkCount,
    int NgCount,
    int DayShiftTotal,
    int NightShiftTotal);

public sealed record AiReadDeviceLogDto(
    Guid Id,
    Guid DeviceId,
    string DeviceName,
    string Level,
    string Message,
    DateTime LogTime,
    DateTime ReceivedAt);

public sealed record AiReadPassStationDto(
    Guid Id,
    Guid DeviceId,
    string? Barcode,
    string? CellResult,
    DateTime? CompletedTime,
    DateTime? ReceivedAt,
    IReadOnlyDictionary<string, object?> Fields);

public sealed record AiReadRecipeVersionDto(
    Guid Id,
    Guid DeviceId,
    Guid ProcessId,
    string RecipeName,
    string Version,
    string Status);

[AuthorizeAiRead(AiReadPermissions.Device)]
public sealed record GetAiReadDevicesQuery(
    string? Keyword = null,
    int? MaxRows = null) : IAiReadQuery<Result<AiReadListResponse<AiReadDeviceDto>>>;

public sealed class GetAiReadDevicesHandler(
    IReadRepository<Device> deviceRepository,
    IAiReadScopeAccessor scopeAccessor,
    IOptions<AiReadOptions> options)
    : IQueryHandler<GetAiReadDevicesQuery, Result<AiReadListResponse<AiReadDeviceDto>>>
{
    public async Task<Result<AiReadListResponse<AiReadDeviceDto>>> Handle(
        GetAiReadDevicesQuery request,
        CancellationToken cancellationToken)
    {
        var maxRows = AiReadQueryGuard.NormalizeMaxRows(request.MaxRows, options.Value);
        var allowedDeviceIds = scopeAccessor.DelegatedDeviceIds?.ToList();
        var countSpec = new DevicePagedSpec(0, 0, allowedDeviceIds, request.Keyword, isPaging: false);
        var totalCount = await deviceRepository.CountAsync(countSpec, cancellationToken);

        List<Device> devices = [];
        if (totalCount > 0)
        {
            devices = await deviceRepository.GetListAsync(
                new DevicePagedSpec(0, maxRows, allowedDeviceIds, request.Keyword, isPaging: true),
                cancellationToken);
        }

        var items = devices
            .Take(maxRows)
            .Select(device => new AiReadDeviceDto(device.Id, device.DeviceName, device.ProcessId))
            .ToList();

        return Result.Success(new AiReadListResponse<AiReadDeviceDto>(
            items,
            DateTimeOffset.UtcNow,
            "devices",
            AiReadQueryGuard.BuildScope(
                ("keyword", request.Keyword),
                ("delegatedUserId", scopeAccessor.DelegatedUserId?.ToString()),
                ("delegatedDeviceCount", allowedDeviceIds?.Count.ToString())),
            items.Count,
            totalCount > items.Count));
    }
}

[AuthorizeAiRead(AiReadPermissions.Recipe)]
public sealed record GetAiReadRecipeVersionsQuery(
    Guid DeviceId,
    Guid? ProcessId = null,
    int? MaxRows = null) : IAiReadQuery<Result<AiReadListResponse<AiReadRecipeVersionDto>>>;

public sealed class GetAiReadRecipeVersionsHandler(
    IReadRepository<Recipe> recipeRepository,
    IAiReadScopeAccessor scopeAccessor,
    IOptions<AiReadOptions> options)
    : IQueryHandler<GetAiReadRecipeVersionsQuery, Result<AiReadListResponse<AiReadRecipeVersionDto>>>
{
    public async Task<Result<AiReadListResponse<AiReadRecipeVersionDto>>> Handle(
        GetAiReadRecipeVersionsQuery request,
        CancellationToken cancellationToken)
    {
        var deviceValidation = AiReadQueryGuard.ValidateDeviceAllowed(
            request.DeviceId,
            scopeAccessor.DelegatedDeviceIds);
        if (deviceValidation is not null)
            return deviceValidation;

        var maxRows = AiReadQueryGuard.NormalizeMaxRows(request.MaxRows, options.Value);
        var countSpec = new RecipeVersionsByDeviceSpec(
            request.DeviceId,
            request.ProcessId,
            isPaging: false);
        var totalCount = await recipeRepository.CountAsync(countSpec, cancellationToken);

        List<Recipe> recipes = [];
        if (totalCount > 0)
        {
            recipes = await recipeRepository.GetListAsync(
                new RecipeVersionsByDeviceSpec(
                    request.DeviceId,
                    request.ProcessId,
                    take: maxRows),
                cancellationToken);
        }

        var items = recipes
            .Take(maxRows)
            .Select(recipe => new AiReadRecipeVersionDto(
                recipe.Id,
                recipe.DeviceId,
                recipe.ProcessId,
                recipe.RecipeName,
                recipe.Version,
                recipe.Status.ToString()))
            .ToList();

        return Result.Success(new AiReadListResponse<AiReadRecipeVersionDto>(
            items,
            DateTimeOffset.UtcNow,
            "recipe_versions",
            AiReadQueryGuard.BuildScope(
                ("deviceId", request.DeviceId.ToString()),
                ("processId", request.ProcessId?.ToString()),
                ("delegatedUserId", scopeAccessor.DelegatedUserId?.ToString())),
            items.Count,
            totalCount > items.Count));
    }
}

[AuthorizeAiRead(AiReadPermissions.Capacity)]
public sealed record GetAiReadCapacitySummaryQuery(
    Guid DeviceId,
    DateOnly StartDate,
    DateOnly EndDate,
    string? PlcName = null,
    int? MaxRows = null) : IAiReadQuery<Result<AiReadListResponse<AiReadCapacitySummaryDto>>>;

public sealed class GetAiReadCapacitySummaryHandler(
    ICapacityQueryService capacityQueryService,
    IAiReadScopeAccessor scopeAccessor,
    IOptions<AiReadOptions> options)
    : IQueryHandler<GetAiReadCapacitySummaryQuery, Result<AiReadListResponse<AiReadCapacitySummaryDto>>>
{
    public async Task<Result<AiReadListResponse<AiReadCapacitySummaryDto>>> Handle(
        GetAiReadCapacitySummaryQuery request,
        CancellationToken cancellationToken)
    {
        var validation = AiReadQueryGuard.ValidateDeviceAndDateRange(
            request.DeviceId,
            request.StartDate,
            request.EndDate,
            scopeAccessor.DelegatedDeviceIds,
            options.Value);
        if (validation is not null)
            return validation;

        var maxRows = AiReadQueryGuard.NormalizeMaxRows(request.MaxRows, options.Value);
        var data = await capacityQueryService.GetSummaryRangeAsync(
            request.DeviceId,
            request.StartDate,
            request.EndDate,
            request.PlcName,
            cancellationToken);

        var items = data
            .Take(maxRows)
            .Select(item => new AiReadCapacitySummaryDto(
                item.Date,
                item.TotalCount,
                item.OkCount,
                item.NgCount,
                item.DayShiftTotal,
                item.NightShiftTotal))
            .ToList();

        return Result.Success(new AiReadListResponse<AiReadCapacitySummaryDto>(
            items,
            DateTimeOffset.UtcNow,
            "capacity.summary",
            AiReadQueryGuard.BuildScope(
                ("deviceId", request.DeviceId.ToString()),
                ("startDate", request.StartDate.ToString("yyyy-MM-dd")),
                ("endDate", request.EndDate.ToString("yyyy-MM-dd")),
                ("plcName", request.PlcName),
                ("delegatedUserId", scopeAccessor.DelegatedUserId?.ToString())),
            items.Count,
            data.Count > items.Count));
    }
}

[AuthorizeAiRead(AiReadPermissions.DeviceLog)]
public sealed record GetAiReadDeviceLogsQuery(
    Guid DeviceId,
    DateTime? StartTime,
    DateTime? EndTime,
    string? Level = null,
    string? Keyword = null,
    int? MaxRows = null) : IAiReadQuery<Result<AiReadListResponse<AiReadDeviceLogDto>>>;

public sealed class GetAiReadDeviceLogsHandler(
    IDeviceLogQueryService deviceLogQueryService,
    IAiReadScopeAccessor scopeAccessor,
    IOptions<AiReadOptions> options)
    : IQueryHandler<GetAiReadDeviceLogsQuery, Result<AiReadListResponse<AiReadDeviceLogDto>>>
{
    public async Task<Result<AiReadListResponse<AiReadDeviceLogDto>>> Handle(
        GetAiReadDeviceLogsQuery request,
        CancellationToken cancellationToken)
    {
        var rangeValidation = AiReadQueryGuard.ValidateDeviceAndTimeRange(
            request.DeviceId,
            request.StartTime,
            request.EndTime,
            scopeAccessor.DelegatedDeviceIds,
            options.Value);
        if (rangeValidation is not null)
            return rangeValidation;

        var maxRows = AiReadQueryGuard.NormalizeMaxRows(request.MaxRows, options.Value);
        var startTime = AiReadQueryGuard.NormalizeUtc(request.StartTime!.Value);
        var endTime = AiReadQueryGuard.NormalizeUtc(request.EndTime!.Value);
        var (items, totalCount) = await deviceLogQueryService.GetLogsByConditionAsync(
            new Pagination { PageNumber = 1, PageSize = maxRows },
            request.DeviceId,
            request.Level,
            request.Keyword,
            startTime,
            endTime,
            cancellationToken);

        var resultItems = items
            .Take(maxRows)
            .Select(item => new AiReadDeviceLogDto(
                item.Id,
                item.DeviceId,
                item.DeviceName,
                item.Level,
                AiReadQueryGuard.Truncate(item.Message, options.Value.MaxLogMessageLength),
                AiReadQueryGuard.NormalizeUtc(item.LogTime),
                AiReadQueryGuard.NormalizeUtc(item.ReceivedAt)))
            .ToList();

        return Result.Success(new AiReadListResponse<AiReadDeviceLogDto>(
            resultItems,
            DateTimeOffset.UtcNow,
            "device_logs",
            AiReadQueryGuard.BuildScope(
                ("deviceId", request.DeviceId.ToString()),
                ("startTime", startTime.ToString("O")),
                ("endTime", endTime.ToString("O")),
                ("level", request.Level),
                ("keyword", string.IsNullOrWhiteSpace(request.Keyword) ? null : "present"),
                ("delegatedUserId", scopeAccessor.DelegatedUserId?.ToString())),
            resultItems.Count,
            totalCount > resultItems.Count));
    }
}

[AuthorizeAiRead(AiReadPermissions.PassStation)]
public sealed record GetAiReadPassStationsQuery(
    string TypeKey,
    DateTime? StartTime,
    DateTime? EndTime,
    Guid? DeviceId = null,
    string? Barcode = null,
    int? MaxRows = null) : IAiReadQuery<Result<AiReadListResponse<AiReadPassStationDto>>>;

public sealed class GetAiReadPassStationsHandler(
    IPassStationSchemaProvider schemaProvider,
    IPassStationRecordQueryService passStationRecordQueryService,
    IAiReadScopeAccessor scopeAccessor,
    IOptions<AiReadOptions> options)
    : IQueryHandler<GetAiReadPassStationsQuery, Result<AiReadListResponse<AiReadPassStationDto>>>
{
    public async Task<Result<AiReadListResponse<AiReadPassStationDto>>> Handle(
        GetAiReadPassStationsQuery request,
        CancellationToken cancellationToken)
    {
        var definition = schemaProvider.Find(request.TypeKey);
        if (definition is null)
            return Result.NotFound($"过站类型 [{request.TypeKey}] 不存在。");

        var rangeValidation = AiReadQueryGuard.ValidateTimeRange(
            request.StartTime,
            request.EndTime,
            options.Value);
        if (rangeValidation is not null)
            return rangeValidation;

        if (request.DeviceId.HasValue)
        {
            var deviceValidation = AiReadQueryGuard.ValidateDeviceAllowed(
                request.DeviceId.Value,
                scopeAccessor.DelegatedDeviceIds);
            if (deviceValidation is not null)
                return deviceValidation;
        }

        var maxRows = AiReadQueryGuard.NormalizeMaxRows(request.MaxRows, options.Value);
        var startTime = AiReadQueryGuard.NormalizeUtc(request.StartTime!.Value);
        var endTime = AiReadQueryGuard.NormalizeUtc(request.EndTime!.Value);
        var queryRequest = new PassStationQueryRequest(
            definition.TypeKey,
            PassStationQueryModes.DeviceTime,
            new Pagination { PageNumber = 1, PageSize = maxRows },
            ProcessId: null,
            DeviceId: request.DeviceId,
            Barcode: request.Barcode?.Trim(),
            StartTime: startTime,
            EndTime: endTime);

        var (items, totalCount) = await passStationRecordQueryService.GetByConditionAsync(
            queryRequest,
            scopeAccessor.DelegatedDeviceIds,
            cancellationToken);
        var exposedFieldKeys = definition.ListColumns
            .Where(column => !AiReadQueryGuard.IsPassStationCommonColumn(column))
            .ToHashSet(StringComparer.Ordinal);

        var resultItems = items
            .Take(maxRows)
            .Select(item => new AiReadPassStationDto(
                item.Id,
                item.DeviceId,
                item.Barcode,
                item.CellResult,
                item.CompletedTime.HasValue ? AiReadQueryGuard.NormalizeUtc(item.CompletedTime.Value) : null,
                item.ReceivedAt.HasValue ? AiReadQueryGuard.NormalizeUtc(item.ReceivedAt.Value) : null,
                item.Fields
                    .Where(field => exposedFieldKeys.Contains(field.Key))
                    .ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal)))
            .ToList();

        return Result.Success(new AiReadListResponse<AiReadPassStationDto>(
            resultItems,
            DateTimeOffset.UtcNow,
            $"pass_station_records:{definition.TypeKey}",
            AiReadQueryGuard.BuildScope(
                ("typeKey", definition.TypeKey),
                ("deviceId", request.DeviceId?.ToString()),
                ("barcode", string.IsNullOrWhiteSpace(request.Barcode) ? null : "present"),
                ("startTime", startTime.ToString("O")),
                ("endTime", endTime.ToString("O")),
                ("delegatedUserId", scopeAccessor.DelegatedUserId?.ToString()),
                ("delegatedDeviceCount", scopeAccessor.DelegatedDeviceIds?.Count.ToString())),
            resultItems.Count,
            totalCount > resultItems.Count));
    }
}

internal static class AiReadQueryGuard
{
    private static readonly HashSet<string> PassStationCommonColumns = new(StringComparer.Ordinal)
    {
        "id",
        "deviceId",
        "barcode",
        "cellResult",
        "completedTime",
        "receivedAt"
    };

    public static int NormalizeMaxRows(int? requestedMaxRows, AiReadOptions options)
    {
        var requested = requestedMaxRows.GetValueOrDefault(options.MaxRows);
        return Math.Clamp(requested, 1, options.MaxRows);
    }

    public static Result? ValidateDeviceAndDateRange(
        Guid deviceId,
        DateOnly startDate,
        DateOnly endDate,
        IReadOnlyCollection<Guid>? delegatedDeviceIds,
        AiReadOptions options)
    {
        var deviceValidation = ValidateDeviceAllowed(deviceId, delegatedDeviceIds);
        if (deviceValidation is not null)
            return deviceValidation;

        if (startDate > endDate)
            return Result.Invalid("开始日期不能晚于结束日期。");

        var days = endDate.DayNumber - startDate.DayNumber + 1;
        if (days > options.MaxTimeRangeDays)
            return Result.Invalid($"AiRead 查询时间跨度不能超过 {options.MaxTimeRangeDays} 天。");

        return null;
    }

    public static Result? ValidateDeviceAndTimeRange(
        Guid deviceId,
        DateTime? startTime,
        DateTime? endTime,
        IReadOnlyCollection<Guid>? delegatedDeviceIds,
        AiReadOptions options)
    {
        var deviceValidation = ValidateDeviceAllowed(deviceId, delegatedDeviceIds);
        if (deviceValidation is not null)
            return deviceValidation;

        return ValidateTimeRange(startTime, endTime, options);
    }

    public static Result? ValidateDeviceAllowed(
        Guid deviceId,
        IReadOnlyCollection<Guid>? delegatedDeviceIds)
    {
        if (deviceId == Guid.Empty)
            return Result.Invalid("设备不能为空。");

        if (delegatedDeviceIds is not null && !delegatedDeviceIds.Contains(deviceId))
            return Result.Forbidden("AiRead delegated device scope 不包含该设备。");

        return null;
    }

    public static Result? ValidateTimeRange(
        DateTime? startTime,
        DateTime? endTime,
        AiReadOptions options)
    {
        if (!startTime.HasValue || !endTime.HasValue)
            return Result.Invalid("AiRead 查询必须提供开始时间和结束时间。");

        var startUtc = NormalizeUtc(startTime.Value);
        var endUtc = NormalizeUtc(endTime.Value);
        if (startUtc > endUtc)
            return Result.Invalid("开始时间不能晚于结束时间。");

        if ((endUtc - startUtc).TotalDays > options.MaxTimeRangeDays)
            return Result.Invalid($"AiRead 查询时间跨度不能超过 {options.MaxTimeRangeDays} 天。");

        return null;
    }

    public static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    public static string BuildScope(params (string Key, string? Value)[] values)
    {
        var parts = values
            .Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .Select(value => $"{value.Key}={value.Value}");

        var scope = string.Join(";", parts);
        return string.IsNullOrWhiteSpace(scope) ? "default" : scope;
    }

    public static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    public static bool IsPassStationCommonColumn(string column)
    {
        return PassStationCommonColumns.Contains(column);
    }
}
