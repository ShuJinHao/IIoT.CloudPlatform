using IIoT.Core.Production.Aggregates.ClientReleases;
using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.Core.Production.Specifications.ClientReleases;
using IIoT.ProductionService.AiRead;
using IIoT.ProductionService.ClientReleases;
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
    string DeviceCode,
    string DeviceName,
    Guid ProcessId);

public sealed record AiReadProcessDto(
    Guid Id,
    string ProcessCode,
    string ProcessName);

public sealed record AiReadClientReleaseVersionDto(
    Guid Id,
    string ComponentKind,
    string ComponentKey,
    string DisplayName,
    string Channel,
    string TargetRuntime,
    string Version,
    string Status,
    string? ReleaseNotes,
    DateTime CreatedAtUtc,
    DateTime? PublishedAtUtc,
    DateTime? DeletedAtUtc);

public sealed record AiReadDeviceClientStateDto(
    Guid DeviceId,
    string DeviceName,
    string ClientCode,
    string? PrimaryIp,
    string? Channel,
    string? HostVersion,
    string? HostApiVersion,
    DateTime? VersionReportedAtUtc,
    DateTime? VersionReceivedAtUtc,
    string SoftwareStatus,
    string? RuntimeStatus,
    DateTime? RuntimeStartedAtUtc,
    DateTime? LastRuntimeHeartbeatAtUtc,
    DateTime? UpdatedAtUtc);

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

public sealed record AiReadHourlyCapacityDto(
    DateTime Time,
    DateOnly Date,
    int Hour,
    int Minute,
    string TimeLabel,
    string ShiftCode,
    int TotalCount,
    int OkCount,
    int NgCount,
    decimal OkRate);

public sealed record AiReadProductionFieldSchemaDto(
    string Key,
    string Label,
    string Type,
    string? Unit,
    int? Precision,
    bool Required);

public sealed record AiReadProductionRecordDto(
    Guid RecordId,
    string TypeKey,
    string TypeName,
    Guid DeviceId,
    string DeviceName,
    string? Barcode,
    string? Result,
    DateTime? CompletedAt,
    DateTime? ReceivedAt,
    IReadOnlyDictionary<string, object?> Fields,
    IReadOnlyList<AiReadProductionFieldSchemaDto> FieldSchema);

[AuthorizeAiRead(AiReadPermissions.Device)]
public sealed record GetAiReadDevicesQuery(
    Guid? DeviceId = null,
    string? DeviceCode = null,
    Guid? ProcessId = null,
    string? Keyword = null,
    int? MaxRows = null,
    IReadOnlyList<string>? UnsupportedParameters = null,
    bool DeviceCodeSupplied = false)
    : IAiReadQuery<Result<AiReadListResponse<AiReadDeviceDto>>>;

public sealed class GetAiReadDevicesHandler(
    IAiReadDeviceQueryService deviceQueryService,
    IAiReadScopeAccessor scopeAccessor,
    IOptions<AiReadOptions> options)
    : IQueryHandler<GetAiReadDevicesQuery, Result<AiReadListResponse<AiReadDeviceDto>>>
{
    public async Task<Result<AiReadListResponse<AiReadDeviceDto>>> Handle(
        GetAiReadDevicesQuery request,
        CancellationToken cancellationToken)
    {
        var queryParameterValidation = AiReadQueryGuard.ValidateDeviceQueryParameters(
            request.DeviceCode,
            request.DeviceCodeSupplied,
            request.UnsupportedParameters);
        if (queryParameterValidation is not null)
            return queryParameterValidation;

        var scopeValidation = AiReadQueryGuard.ResolveDeviceScope(scopeAccessor, out var allowedDeviceIds);
        if (scopeValidation is not null)
            return scopeValidation;

        if (request.DeviceId.HasValue)
        {
            var validation = AiReadQueryGuard.ValidateDeviceAllowed(
                request.DeviceId.Value,
                allowedDeviceIds);
            if (validation is not null)
                return validation;
        }

        if (request.ProcessId == Guid.Empty)
            return Result.Invalid("工序不能为空。");

        var maxRows = AiReadQueryGuard.NormalizeMaxRows(request.MaxRows, options.Value);
        var (devices, totalCount) = await deviceQueryService.GetPagedAsync(
            new AiReadDeviceQueryRequest(
                request.DeviceId,
                request.DeviceCode,
                request.ProcessId,
                request.Keyword,
                allowedDeviceIds,
                Skip: 0,
                Take: maxRows),
            cancellationToken);

        var items = devices
            .Select(device => new AiReadDeviceDto(device.Id, device.DeviceCode, device.DeviceName, device.ProcessId))
            .ToList();

        return Result.Success(new AiReadListResponse<AiReadDeviceDto>(
            items,
            DateTimeOffset.UtcNow,
            "devices",
            AiReadQueryGuard.BuildScope(
                ("deviceId", AiReadQueryGuard.ScopeGuid(request.DeviceId)),
                ("deviceCode", AiReadQueryGuard.ScopeText(request.DeviceCode)),
                ("processId", AiReadQueryGuard.ScopeGuid(request.ProcessId)),
                ("keyword", AiReadQueryGuard.ScopeText(request.Keyword)),
                ("delegatedUserId", AiReadQueryGuard.ScopeGuid(scopeAccessor.DelegatedUserId)),
                ("delegatedDeviceCount", AiReadQueryGuard.ScopeNumber(allowedDeviceIds?.Count))),
            items.Count,
            totalCount > items.Count));
    }
}

[AuthorizeAiRead(AiReadPermissions.Process)]
public sealed record GetAiReadProcessesQuery(
    Guid? ProcessId = null,
    string? Keyword = null,
    int? MaxRows = null) : IAiReadQuery<Result<AiReadListResponse<AiReadProcessDto>>>;

public sealed class GetAiReadProcessesHandler(
    IProcessReadQueryService processReadQueryService,
    IOptions<AiReadOptions> options)
    : IQueryHandler<GetAiReadProcessesQuery, Result<AiReadListResponse<AiReadProcessDto>>>
{
    public async Task<Result<AiReadListResponse<AiReadProcessDto>>> Handle(
        GetAiReadProcessesQuery request,
        CancellationToken cancellationToken)
    {
        if (request.ProcessId == Guid.Empty)
            return Result.Invalid("工序不能为空。");

        var maxRows = AiReadQueryGuard.NormalizeMaxRows(request.MaxRows, options.Value);
        var (processes, totalCount) = await processReadQueryService.GetPagedAsync(
            request.ProcessId,
            request.Keyword,
            0,
            maxRows,
            cancellationToken);

        var items = processes
            .Take(maxRows)
            .Select(process => new AiReadProcessDto(process.Id, process.ProcessCode, process.ProcessName))
            .ToList();

        return Result.Success(new AiReadListResponse<AiReadProcessDto>(
            items,
            DateTimeOffset.UtcNow,
            "processes",
            AiReadQueryGuard.BuildScope(
                ("processId", AiReadQueryGuard.ScopeGuid(request.ProcessId)),
                ("keyword", AiReadQueryGuard.ScopeText(request.Keyword))),
            items.Count,
            totalCount > items.Count));
    }
}

[AuthorizeAiRead(AiReadPermissions.ClientRelease)]
public sealed record GetAiReadClientReleaseVersionsQuery(
    string? Channel = null,
    string? TargetRuntime = null,
    string? Status = null,
    bool IncludeArchived = false,
    int? MaxRows = null) : IAiReadQuery<Result<AiReadListResponse<AiReadClientReleaseVersionDto>>>;

public sealed class GetAiReadClientReleaseVersionsHandler(
    IReadRepository<ClientReleaseComponent> componentRepository,
    IOptions<AiReadOptions> options)
    : IQueryHandler<GetAiReadClientReleaseVersionsQuery, Result<AiReadListResponse<AiReadClientReleaseVersionDto>>>
{
    public async Task<Result<AiReadListResponse<AiReadClientReleaseVersionDto>>> Handle(
        GetAiReadClientReleaseVersionsQuery request,
        CancellationToken cancellationToken)
    {
        var maxRows = AiReadQueryGuard.NormalizeMaxRows(request.MaxRows, options.Value);
        var components = await componentRepository.GetListAsync(
            new ClientReleaseComponentsByChannelSpec(
                string.IsNullOrWhiteSpace(request.Channel) ? null : request.Channel,
                string.IsNullOrWhiteSpace(request.TargetRuntime) ? null : request.TargetRuntime,
                onlyPublished: false,
                includeArchived: request.IncludeArchived),
            cancellationToken);
        var status = string.IsNullOrWhiteSpace(request.Status) ? null : request.Status.Trim();
        var allItems = components
            .SelectMany(component => component.Versions.Select(version => (Component: component, Version: version)))
            .Where(item => status is null || string.Equals(item.Version.Status.ToString(), status, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Component.ComponentKind.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Component.ComponentKey, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(item => item.Version.PublishedAtUtc ?? item.Version.CreatedAtUtc)
            .Select(item => new AiReadClientReleaseVersionDto(
                item.Version.Id,
                item.Component.ComponentKind.ToString(),
                item.Component.ComponentKey,
                item.Component.DisplayName,
                item.Component.Channel,
                item.Component.TargetRuntime,
                item.Version.Version,
                item.Version.Status.ToString(),
                item.Version.ReleaseNotes,
                item.Version.CreatedAtUtc,
                item.Version.PublishedAtUtc,
                item.Version.DeletedAtUtc))
            .ToList();
        var items = allItems.Take(maxRows).ToList();

        return Result.Success(new AiReadListResponse<AiReadClientReleaseVersionDto>(
            items,
            DateTimeOffset.UtcNow,
            "client_release_versions",
            AiReadQueryGuard.BuildScope(
                ("channel", AiReadQueryGuard.ScopeText(request.Channel)),
                ("targetRuntime", AiReadQueryGuard.ScopeText(request.TargetRuntime)),
                ("status", AiReadQueryGuard.ScopeText(request.Status)),
                ("includeArchived", AiReadQueryGuard.ScopeBoolean(request.IncludeArchived))),
            items.Count,
            allItems.Count > items.Count));
    }
}

[AuthorizeAiRead(AiReadPermissions.DeviceClientState)]
public sealed record GetAiReadDeviceClientStatesQuery(
    Guid? DeviceId = null,
    string? DeviceCode = null,
    Guid? ProcessId = null,
    string? Keyword = null,
    int? MaxRows = null,
    IReadOnlyList<string>? UnsupportedParameters = null,
    bool DeviceCodeSupplied = false)
    : IAiReadQuery<Result<AiReadListResponse<AiReadDeviceClientStateDto>>>;

public sealed class GetAiReadDeviceClientStatesHandler(
    IAiReadDeviceQueryService deviceQueryService,
    IDeviceClientStateQueryService clientStateStore,
    IAiReadScopeAccessor scopeAccessor,
    IOptions<AiReadOptions> options)
    : IQueryHandler<GetAiReadDeviceClientStatesQuery, Result<AiReadListResponse<AiReadDeviceClientStateDto>>>
{
    public async Task<Result<AiReadListResponse<AiReadDeviceClientStateDto>>> Handle(
        GetAiReadDeviceClientStatesQuery request,
        CancellationToken cancellationToken)
    {
        var queryParameterValidation = AiReadQueryGuard.ValidateDeviceQueryParameters(
            request.DeviceCode,
            request.DeviceCodeSupplied,
            request.UnsupportedParameters);
        if (queryParameterValidation is not null)
            return queryParameterValidation;

        var scopeValidation = AiReadQueryGuard.ResolveDeviceScope(scopeAccessor, out var allowedDeviceIds);
        if (scopeValidation is not null)
            return scopeValidation;

        if (request.DeviceId.HasValue)
        {
            var validation = AiReadQueryGuard.ValidateDeviceAllowed(
                request.DeviceId.Value,
                allowedDeviceIds);
            if (validation is not null)
                return validation;
        }

        if (request.ProcessId == Guid.Empty)
            return Result.Invalid("工序不能为空。");

        var maxRows = AiReadQueryGuard.NormalizeMaxRows(request.MaxRows, options.Value);
        var (devices, totalCount) = await deviceQueryService.GetPagedAsync(
            new AiReadDeviceQueryRequest(
                request.DeviceId,
                request.DeviceCode,
                request.ProcessId,
                request.Keyword,
                allowedDeviceIds,
                Skip: 0,
                Take: maxRows),
            cancellationToken);
        var deviceIds = devices.Select(device => device.Id).ToList();
        var states = deviceIds.Count == 0
            ? []
            : await clientStateStore.GetStatesByDevicesAsync(deviceIds, cancellationToken);
        var statesByDevice = states
            .GroupBy(state => state.DeviceId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var utcNow = DateTime.UtcNow;
        var items = devices
            .Select(device =>
            {
                var state = statesByDevice.GetValueOrDefault(device.Id)?
                    .Where(candidate => string.Equals(
                        candidate.ClientCode,
                        device.DeviceCode,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(candidate => candidate.UpdatedAtUtc)
                    .FirstOrDefault();
                var softwareStatus = DeviceClientSoftwareStatusResolver.Resolve(state, utcNow);
                return new AiReadDeviceClientStateDto(
                    device.Id,
                    device.DeviceName,
                    device.DeviceCode,
                    ResolvePrimaryIp(state),
                    state?.Channel,
                    state?.HostVersion,
                    state?.HostApiVersion,
                    state?.VersionReportedAtUtc,
                    state?.VersionReceivedAtUtc,
                    softwareStatus.SoftwareStatus,
                    state?.RuntimeStatus,
                    state?.RuntimeStartedAtUtc,
                    state?.LastRuntimeHeartbeatAtUtc,
                    state?.UpdatedAtUtc);
            })
            .ToList();

        return Result.Success(new AiReadListResponse<AiReadDeviceClientStateDto>(
            items,
            new DateTimeOffset(utcNow),
            "device_client_states",
            AiReadQueryGuard.BuildScope(
                ("deviceId", AiReadQueryGuard.ScopeGuid(request.DeviceId)),
                ("deviceCode", AiReadQueryGuard.ScopeText(request.DeviceCode)),
                ("processId", AiReadQueryGuard.ScopeGuid(request.ProcessId)),
                ("keyword", AiReadQueryGuard.ScopeText(request.Keyword)),
                ("delegatedUserId", AiReadQueryGuard.ScopeGuid(scopeAccessor.DelegatedUserId)),
                ("delegatedDeviceCount", AiReadQueryGuard.ScopeNumber(allowedDeviceIds?.Count))),
            items.Count,
            totalCount > items.Count));
    }

    private static string? ResolvePrimaryIp(DeviceClientState? state)
    {
        return state?.GetRuntimeLocalIpAddresses().FirstOrDefault()
            ?? state?.GetVersionLocalIpAddresses().FirstOrDefault()
            ?? state?.RuntimeRemoteIpAddress
            ?? state?.VersionRemoteIpAddress;
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
        var scopeValidation = AiReadQueryGuard.ResolveDeviceScope(scopeAccessor, out var allowedDeviceIds);
        if (scopeValidation is not null)
            return scopeValidation;

        var validation = AiReadQueryGuard.ValidateDeviceAndDateRange(
            request.DeviceId,
            request.StartDate,
            request.EndDate,
            allowedDeviceIds,
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
                ("deviceId", AiReadQueryGuard.ScopeGuid(request.DeviceId)),
                ("startDate", AiReadQueryGuard.ScopeDate(request.StartDate)),
                ("endDate", AiReadQueryGuard.ScopeDate(request.EndDate)),
                ("plcName", AiReadQueryGuard.ScopeText(request.PlcName)),
                ("delegatedUserId", AiReadQueryGuard.ScopeGuid(scopeAccessor.DelegatedUserId))),
            items.Count,
            data.Count > items.Count));
    }
}

[AuthorizeAiRead(AiReadPermissions.Capacity)]
public sealed record GetAiReadCapacityHourlyQuery(
    Guid DeviceId,
    DateOnly? Date = null,
    string? Preset = null,
    string? PlcName = null,
    int? MaxRows = null) : IAiReadQuery<Result<AiReadListResponse<AiReadHourlyCapacityDto>>>;

public sealed class GetAiReadCapacityHourlyHandler(
    ICapacityQueryService capacityQueryService,
    IAiReadScopeAccessor scopeAccessor,
    IOptions<AiReadOptions> options)
    : IQueryHandler<GetAiReadCapacityHourlyQuery, Result<AiReadListResponse<AiReadHourlyCapacityDto>>>
{
    public async Task<Result<AiReadListResponse<AiReadHourlyCapacityDto>>> Handle(
        GetAiReadCapacityHourlyQuery request,
        CancellationToken cancellationToken)
    {
        var scopeValidation = AiReadQueryGuard.ResolveDeviceScope(scopeAccessor, out var allowedDeviceIds);
        if (scopeValidation is not null)
            return scopeValidation;

        var deviceValidation = AiReadQueryGuard.ValidateDeviceAllowed(
            request.DeviceId,
            allowedDeviceIds);
        if (deviceValidation is not null)
            return deviceValidation;

        var rangeValidation = AiReadQueryGuard.ResolveCapacityHourlyRange(
            request.Date,
            request.Preset,
            out var range);
        if (rangeValidation is not null)
            return rangeValidation;

        var maxRows = AiReadQueryGuard.NormalizeMaxRows(request.MaxRows, options.Value);
        var rows = await capacityQueryService.GetHourlyRangeByDeviceIdAsync(
            request.DeviceId,
            range!.StartTime,
            range.EndTime,
            request.PlcName,
            cancellationToken);
        var items = rows
            .Take(maxRows)
            .Select(row => new AiReadHourlyCapacityDto(
                AiReadQueryGuard.NormalizeUtc(row.Time),
                row.Date,
                row.Hour,
                row.Minute,
                row.TimeLabel,
                row.ShiftCode,
                row.TotalCount,
                row.OkCount,
                row.NgCount,
                row.TotalCount > 0 ? Math.Round(row.OkCount * 100m / row.TotalCount, 2) : 0m))
            .ToList();

        return Result.Success(new AiReadListResponse<AiReadHourlyCapacityDto>(
            items,
            DateTimeOffset.UtcNow,
            "capacity.hourly",
            AiReadQueryGuard.BuildScope(
                ("deviceId", AiReadQueryGuard.ScopeGuid(request.DeviceId)),
                ("date", AiReadQueryGuard.ScopeDate(request.Date)),
                ("preset", AiReadQueryGuard.ScopeClosed(
                    range.RangeSource,
                    "date", "last_24h", "today", "yesterday")),
                ("startTime", AiReadQueryGuard.ScopeDateTime(range.StartTime)),
                ("endTime", AiReadQueryGuard.ScopeDateTime(range.EndTime)),
                ("plcName", AiReadQueryGuard.ScopeText(request.PlcName)),
                ("delegatedUserId", AiReadQueryGuard.ScopeGuid(scopeAccessor.DelegatedUserId))),
            items.Count,
            rows.Count > items.Count));
    }
}

[AuthorizeAiRead(AiReadPermissions.DeviceLog)]
public sealed record GetAiReadDeviceLogsQuery(
    Guid DeviceId,
    DateTime? StartTime,
    DateTime? EndTime,
    string? Level = null,
    string? Keyword = null,
    string? Preset = null,
    string? MinLevel = null,
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
        var scopeValidation = AiReadQueryGuard.ResolveDeviceScope(scopeAccessor, out var allowedDeviceIds);
        if (scopeValidation is not null)
            return scopeValidation;

        if (!string.IsNullOrWhiteSpace(request.Level) && !string.IsNullOrWhiteSpace(request.MinLevel))
            return Result.Invalid("level 和 minLevel 不能同时传。");

        var rangeValidation = AiReadQueryGuard.ValidateDeviceAllowed(
            request.DeviceId,
            allowedDeviceIds);
        if (rangeValidation is not null)
            return rangeValidation;

        var timeRangeValidation = AiReadQueryGuard.ResolveTimeRange(
            request.StartTime,
            request.EndTime,
            request.Preset,
            options.Value,
            out var range);
        if (timeRangeValidation is not null)
            return timeRangeValidation;

        IReadOnlyCollection<string>? normalizedLevels = null;
        if (!string.IsNullOrWhiteSpace(request.MinLevel))
        {
            if (!DeviceLogSeverityLevels.TryGetLevelsAtOrAbove(
                    request.MinLevel,
                    out normalizedLevels,
                    out _))
            {
                return Result.Invalid("minLevel 只支持 info、warn、error。");
            }
        }

        var maxRows = AiReadQueryGuard.NormalizeMaxRows(request.MaxRows, options.Value);
        var (items, totalCount) = await deviceLogQueryService.GetLogsByConditionAsync(
            new Pagination { PageNumber = 1, PageSize = maxRows },
            request.DeviceId,
            request.Level,
            request.Keyword,
            range!.StartTime,
            range.EndTime,
            normalizedLevels,
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
                ("deviceId", AiReadQueryGuard.ScopeGuid(request.DeviceId)),
                ("startTime", AiReadQueryGuard.ScopeDateTime(range.StartTime)),
                ("endTime", AiReadQueryGuard.ScopeDateTime(range.EndTime)),
                ("preset", AiReadQueryGuard.ScopeClosed(
                    range.RangeSource,
                    "explicit", "last_24h", "last_7d", "today", "yesterday")),
                ("level", AiReadQueryGuard.ScopeText(request.Level)),
                ("minLevel", AiReadQueryGuard.ScopeText(request.MinLevel)),
                ("keyword", AiReadQueryGuard.ScopeText(request.Keyword)),
                ("delegatedUserId", AiReadQueryGuard.ScopeGuid(scopeAccessor.DelegatedUserId))),
            resultItems.Count,
            totalCount > resultItems.Count));
    }
}

[AuthorizeAiRead(AiReadPermissions.ProductionRecord)]
public sealed record GetAiReadProductionRecordsQuery(
    string? TypeKey = null,
    Guid? ProcessId = null,
    Guid? DeviceId = null,
    string? Barcode = null,
    string? Result = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null,
    string? Preset = null,
    string? FieldMode = null,
    int? MaxRows = null) : IAiReadQuery<Result<AiReadListResponse<AiReadProductionRecordDto>>>;

public sealed class GetAiReadProductionRecordsHandler(
    IPassStationSchemaProvider schemaProvider,
    IAiProductionRecordQueryService productionRecordQueryService,
    IAiReadScopeAccessor scopeAccessor,
    IOptions<AiReadOptions> options)
    : IQueryHandler<GetAiReadProductionRecordsQuery, Result<AiReadListResponse<AiReadProductionRecordDto>>>
{
    public async Task<Result<AiReadListResponse<AiReadProductionRecordDto>>> Handle(
        GetAiReadProductionRecordsQuery request,
        CancellationToken cancellationToken)
    {
        var scopeValidation = AiReadQueryGuard.ResolveDeviceScope(scopeAccessor, out var allowedDeviceIds);
        if (scopeValidation is not null)
            return scopeValidation;

        var fieldMode = string.IsNullOrWhiteSpace(request.FieldMode)
            ? "list"
            : request.FieldMode.Trim().ToLowerInvariant();
        if (fieldMode is not ("list" or "full"))
            return Result.Invalid("fieldMode 只支持 list 或 full。");

        PassStationTypeDefinitionDto? requestedDefinition = null;
        string? normalizedTypeKey = null;
        if (!string.IsNullOrWhiteSpace(request.TypeKey))
        {
            normalizedTypeKey = request.TypeKey.Trim().ToLowerInvariant();
            requestedDefinition = schemaProvider.Find(normalizedTypeKey);
            if (requestedDefinition is null)
                return Result.NotFound($"生产数据类型 [{request.TypeKey}] 不存在。");
        }

        if (string.IsNullOrWhiteSpace(normalizedTypeKey)
            && !request.DeviceId.HasValue
            && !request.ProcessId.HasValue)
        {
            return Result.Invalid("跨类型查询 production-records 时必须提供 deviceId 或 processId。");
        }

        var rangeValidation = AiReadQueryGuard.ResolveTimeRange(
            request.StartTime,
            request.EndTime,
            request.Preset,
            options.Value,
            out var range);
        if (rangeValidation is not null)
            return rangeValidation;

        if (request.DeviceId.HasValue)
        {
            var deviceValidation = AiReadQueryGuard.ValidateDeviceAllowed(
                request.DeviceId.Value,
                allowedDeviceIds);
            if (deviceValidation is not null)
                return deviceValidation;
        }

        var maxRows = AiReadQueryGuard.NormalizeMaxRows(request.MaxRows, options.Value);
        var queryRequest = new AiProductionRecordQueryRequest(
            new Pagination { PageNumber = 1, PageSize = maxRows },
            range!.StartTime,
            range.EndTime,
            TypeKey: requestedDefinition?.TypeKey,
            ProcessId: request.ProcessId,
            DeviceId: request.DeviceId,
            Barcode: request.Barcode?.Trim(),
            Result: request.Result?.Trim());

        var (items, totalCount) = await productionRecordQueryService.GetAsync(
            queryRequest,
            allowedDeviceIds,
            cancellationToken);
        var definitions = schemaProvider.GetAll()
            .ToDictionary(definition => definition.TypeKey, StringComparer.Ordinal);

        var resultItems = items
            .Take(maxRows)
            .Select(item =>
            {
                definitions.TryGetValue(item.TypeKey, out var definition);
                List<PassStationFieldDefinitionDto> fieldDefinitions = definition is null
                    ? []
                    : SelectFieldDefinitions(definition, fieldMode);
                var exposedFieldKeys = fieldDefinitions
                    .Select(field => field.Key)
                    .ToHashSet(StringComparer.Ordinal);

                return new AiReadProductionRecordDto(
                item.Id,
                item.TypeKey,
                definition?.DisplayName ?? item.TypeKey,
                item.DeviceId,
                item.DeviceName,
                item.Barcode,
                item.Result,
                item.CompletedTime.HasValue ? AiReadQueryGuard.NormalizeUtc(item.CompletedTime.Value) : null,
                item.ReceivedAt.HasValue ? AiReadQueryGuard.NormalizeUtc(item.ReceivedAt.Value) : null,
                item.Fields
                    .Where(field => exposedFieldKeys.Contains(field.Key))
                    .ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal),
                fieldDefinitions
                    .Select(field => new AiReadProductionFieldSchemaDto(
                        field.Key,
                        field.Label,
                        field.Type,
                        field.Unit,
                        field.Precision,
                        field.Required))
                    .ToList());
            })
            .ToList();

        return Result.Success(new AiReadListResponse<AiReadProductionRecordDto>(
            resultItems,
            DateTimeOffset.UtcNow,
            "production_records",
            AiReadQueryGuard.BuildScope(
                ("typeKey", AiReadQueryGuard.ScopeText(requestedDefinition?.TypeKey)),
                ("processId", AiReadQueryGuard.ScopeGuid(request.ProcessId)),
                ("deviceId", AiReadQueryGuard.ScopeGuid(request.DeviceId)),
                ("barcode", AiReadQueryGuard.ScopeText(request.Barcode)),
                ("result", AiReadQueryGuard.ScopeText(request.Result)),
                ("fieldMode", AiReadQueryGuard.ScopeClosed(fieldMode, "list", "full")),
                ("preset", AiReadQueryGuard.ScopeClosed(
                    range.RangeSource,
                    "explicit", "last_24h", "last_7d", "today", "yesterday")),
                ("startTime", AiReadQueryGuard.ScopeDateTime(range.StartTime)),
                ("endTime", AiReadQueryGuard.ScopeDateTime(range.EndTime)),
                ("delegatedUserId", AiReadQueryGuard.ScopeGuid(scopeAccessor.DelegatedUserId)),
                ("delegatedDeviceCount", AiReadQueryGuard.ScopeNumber(allowedDeviceIds?.Count))),
            resultItems.Count,
            totalCount > resultItems.Count));
    }

    private static List<PassStationFieldDefinitionDto> SelectFieldDefinitions(
        PassStationTypeDefinitionDto definition,
        string fieldMode)
    {
        if (fieldMode == "full")
            return definition.Fields;

        var listFieldKeys = definition.ListColumns
            .Where(column => !AiReadQueryGuard.IsProductionRecordCommonColumn(column))
            .ToHashSet(StringComparer.Ordinal);
        return definition.Fields
            .Where(field => listFieldKeys.Contains(field.Key))
            .ToList();
    }
}

internal sealed record AiReadResolvedTimeRange(
    DateTime StartTime,
    DateTime EndTime,
    string RangeSource);

internal readonly record struct AiReadScopeValue(string? Value);

internal static class AiReadQueryGuard
{
    private static readonly HashSet<string> KnownUnsupportedQueryParameters = new(
        [
            "softwareStatus",
            "runtimeStatus",
            "status",
            "lineName",
            "processName",
            "updatedAt",
            "updatedAtUtc"
        ],
        StringComparer.Ordinal);

    public static Result? ValidateDeviceQueryParameters(
        string? deviceCode,
        bool deviceCodeSupplied,
        IReadOnlyList<string>? unsupportedParameters)
    {
        if ((deviceCodeSupplied && string.IsNullOrWhiteSpace(deviceCode))
            || (deviceCode is not null && string.IsNullOrWhiteSpace(deviceCode)))
            return Result.Invalid("设备编码不能为空白。");

        if (unsupportedParameters is not { Count: > 0 })
            return null;

        var names = unsupportedParameters
            .Select(parameter => KnownUnsupportedQueryParameters.Contains(parameter) ? parameter : "unknown")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(parameter => parameter, StringComparer.Ordinal)
            .ToArray();
        return Result.Invalid($"不支持的查询参数：{string.Join(", ", names)}。");
    }

    private static readonly HashSet<string> ProductionRecordCommonColumns = new(StringComparer.Ordinal)
    {
        "id",
        "deviceId",
        "barcode",
        "cellResult",
        "result",
        "completedTime",
        "completedAt",
        "receivedAt"
    };

    public static int NormalizeMaxRows(int? requestedMaxRows, AiReadOptions options)
    {
        var requested = requestedMaxRows.GetValueOrDefault(options.MaxRows);
        return Math.Clamp(requested, 1, options.MaxRows);
    }

    public static Result? ResolveDeviceScope(
        IAiReadScopeAccessor scopeAccessor,
        out IReadOnlyCollection<Guid>? allowedDeviceIds)
    {
        switch (scopeAccessor.ScopeKind)
        {
            case AiReadScopeKind.Global:
                allowedDeviceIds = null;
                return null;
            case AiReadScopeKind.Delegated:
                allowedDeviceIds = scopeAccessor.DelegatedDeviceIds?.Distinct().ToArray() ?? [];
                return null;
            case AiReadScopeKind.Invalid:
                allowedDeviceIds = [];
                return Result.Forbidden("AiRead delegated device scope 无效。");
            default:
                allowedDeviceIds = [];
                return Result.Forbidden("AiRead delegated device scope 无效。");
        }
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

    public static Result? ResolveTimeRange(
        DateTime? startTime,
        DateTime? endTime,
        string? preset,
        AiReadOptions options,
        out AiReadResolvedTimeRange? range)
    {
        range = null;
        var hasExplicitTime = startTime.HasValue || endTime.HasValue;
        var hasPreset = !string.IsNullOrWhiteSpace(preset);
        if (hasExplicitTime && hasPreset)
            return Result.Invalid("preset 和 startTime/endTime 不能同时传。");

        if (hasPreset)
        {
            var now = DateTime.UtcNow;
            var normalizedPreset = preset!.Trim().ToLowerInvariant();
            var (start, end) = normalizedPreset switch
            {
                "last_24h" => (now.AddHours(-24), now),
                "last_7d" => (now.AddDays(-7), now),
                "today" => (now.Date, now),
                "yesterday" => (now.Date.AddDays(-1), now.Date.AddTicks(-1)),
                _ => (DateTime.MinValue, DateTime.MinValue)
            };
            if (start == DateTime.MinValue)
                return Result.Invalid("preset 只支持 last_24h、last_7d、today、yesterday。");

            if ((end - start).TotalDays > options.MaxTimeRangeDays)
                return Result.Invalid($"AiRead 查询时间跨度不能超过 {options.MaxTimeRangeDays} 天。");

            range = new AiReadResolvedTimeRange(start, end, normalizedPreset);
            return null;
        }

        var validation = ValidateTimeRange(startTime, endTime, options);
        if (validation is not null)
            return validation;

        range = new AiReadResolvedTimeRange(
            NormalizeUtc(startTime!.Value),
            NormalizeUtc(endTime!.Value),
            "explicit");
        return null;
    }

    public static Result? ResolveCapacityHourlyRange(
        DateOnly? date,
        string? preset,
        out AiReadResolvedTimeRange? range)
    {
        range = null;
        var hasPreset = !string.IsNullOrWhiteSpace(preset);
        if (date.HasValue && hasPreset)
            return Result.Invalid("date 和 preset 不能同时传。");

        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            range = new AiReadResolvedTimeRange(start, start.AddDays(1).AddTicks(-1), "date");
            return null;
        }

        if (!hasPreset)
            return Result.Invalid("capacity/hourly 必须提供 date 或 preset。");

        var now = DateTime.UtcNow;
        var normalizedPreset = preset!.Trim().ToLowerInvariant();
        range = normalizedPreset switch
        {
            "last_24h" => new AiReadResolvedTimeRange(now.AddHours(-24), now, normalizedPreset),
            "today" => new AiReadResolvedTimeRange(now.Date, now, normalizedPreset),
            "yesterday" => new AiReadResolvedTimeRange(now.Date.AddDays(-1), now.Date.AddTicks(-1), normalizedPreset),
            _ => null
        };

        return range is null
            ? Result.Invalid("capacity/hourly preset 只支持 last_24h、today、yesterday。")
            : null;
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

    public static AiReadScopeValue ScopeGuid(Guid? value)
    {
        return new AiReadScopeValue(value?.ToString("D"));
    }

    public static AiReadScopeValue ScopeDate(DateOnly? value)
    {
        return new AiReadScopeValue(value?.ToString("yyyy-MM-dd"));
    }

    public static AiReadScopeValue ScopeDateTime(DateTime? value)
    {
        return new AiReadScopeValue(value.HasValue ? NormalizeUtc(value.Value).ToString("O") : null);
    }

    public static AiReadScopeValue ScopeNumber(int? value)
    {
        return new AiReadScopeValue(value?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static AiReadScopeValue ScopeBoolean(bool value)
    {
        return new AiReadScopeValue(value.ToString());
    }

    public static AiReadScopeValue ScopeText(string? value)
    {
        return new AiReadScopeValue(string.IsNullOrWhiteSpace(value) ? null : "present");
    }

    public static AiReadScopeValue ScopeClosed(string? value, params string[] allowedValues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new AiReadScopeValue(null);

        var normalized = allowedValues.FirstOrDefault(allowed =>
            string.Equals(allowed, value.Trim(), StringComparison.OrdinalIgnoreCase));
        return new AiReadScopeValue(normalized ?? "present");
    }

    public static string BuildScope(params (string Key, AiReadScopeValue Value)[] values)
    {
        var parts = values
            .Where(value => !string.IsNullOrWhiteSpace(value.Value.Value))
            .Select(value => $"{value.Key}={value.Value.Value}");

        var scope = string.Join(";", parts);
        return string.IsNullOrWhiteSpace(scope) ? "default" : scope;
    }

    public static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    public static bool IsProductionRecordCommonColumn(string column)
    {
        return ProductionRecordCommonColumns.Contains(column);
    }
}
