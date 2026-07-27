using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.DeviceLogs;

[AuthorizeRequirement("Device.Read")]
public record GetRecentDeviceLogsQuery(
    int Limit = 20,
    string? MinLevel = "WARN",
    Guid? ProcessId = null,
    Guid? DeviceId = null
) : IHumanQuery<Result<List<DeviceLogListItemDto>>>;

[AuthorizeRequirement("Device.Read")]
public record GetRecentAlertCountQuery(
    Guid? ProcessId = null,
    Guid? DeviceId = null
) : IHumanQuery<Result<RecentAlertCountDto>>;

public class GetRecentDeviceLogsHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IDeviceLogQueryService queryService)
    : IQueryHandler<GetRecentDeviceLogsQuery, Result<List<DeviceLogListItemDto>>>
{
    private const int MaxLimit = 100;

    public async Task<Result<List<DeviceLogListItemDto>>> Handle(
        GetRecentDeviceLogsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.ProcessId.HasValue && request.DeviceId.HasValue)
        {
            return Result.Invalid("processId 与 deviceId 不能同时指定。");
        }

        if (request.DeviceId == Guid.Empty)
        {
            return Result.Invalid("设备不能为空。");
        }

        if (!DeviceLogSeverityLevels.TryGetLevelsAtOrAbove(
                request.MinLevel,
                out var levels,
                out _))
        {
            return Result.Invalid("日志等级仅支持 INFO、WARN、ERROR。");
        }

        IReadOnlyCollection<Guid>? deviceIds;
        if (request.DeviceId.HasValue)
        {
            var access = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
                request.DeviceId.Value,
                cancellationToken);
            if (!access.IsSuccess)
            {
                return Result.Forbidden(access.Errors?.ToArray() ?? ["越权: 未授权访问该设备"]);
            }

            deviceIds = [request.DeviceId.Value];
        }
        else
        {
            var scope = await currentUserDeviceAccessService.GetAccessibleDeviceIdsAsync(cancellationToken);
            if (!scope.IsSuccess)
            {
                return Result.Failure(scope.Errors?.FirstOrDefault() ?? "用户凭证异常");
            }

            if (scope.Value is { Count: 0 })
            {
                return Result.Success(new List<DeviceLogListItemDto>());
            }

            deviceIds = scope.Value;
        }

        var limit = Math.Clamp(request.Limit, 1, MaxLimit);
        var items = await queryService.GetRecentLogsAsync(
            limit,
            levels,
            request.ProcessId,
            deviceIds,
            cancellationToken);

        return Result.Success(items);
    }
}

public class GetRecentAlertCountHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IDeviceLogQueryService queryService)
    : IQueryHandler<GetRecentAlertCountQuery, Result<RecentAlertCountDto>>
{
    private const int AlertWindowHours = 24;
    private const string AlertMinLevel = "WARN";

    public async Task<Result<RecentAlertCountDto>> Handle(
        GetRecentAlertCountQuery request,
        CancellationToken cancellationToken)
    {
        if (request.ProcessId.HasValue && request.DeviceId.HasValue)
        {
            return Result.Invalid("processId 与 deviceId 不能同时指定。");
        }

        if (request.DeviceId == Guid.Empty)
        {
            return Result.Invalid("设备不能为空。");
        }

        IReadOnlyCollection<Guid>? deviceIds;
        if (request.DeviceId.HasValue)
        {
            var access = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
                request.DeviceId.Value,
                cancellationToken);
            if (!access.IsSuccess)
            {
                return Result.Forbidden(access.Errors?.ToArray() ?? ["越权: 未授权访问该设备"]);
            }

            deviceIds = [request.DeviceId.Value];
        }
        else
        {
            var scope = await currentUserDeviceAccessService.GetAccessibleDeviceIdsAsync(cancellationToken);
            if (!scope.IsSuccess)
            {
                return Result.Failure(scope.Errors?.FirstOrDefault() ?? "用户凭证异常");
            }

            deviceIds = scope.Value;
        }

        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddHours(-AlertWindowHours);

        var count = deviceIds is { Count: 0 }
            ? 0
            : await queryService.CountRecentAlertsAsync(
                windowStart,
                DeviceLogSeverityLevels.WarningAndErrorLevels,
                request.ProcessId,
                deviceIds,
                cancellationToken);

        return Result.Success(new RecentAlertCountDto(
            count,
            AlertWindowHours,
            AlertMinLevel,
            windowStart,
            now,
            now));
    }
}
