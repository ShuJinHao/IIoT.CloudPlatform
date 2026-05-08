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
    Guid? ProcessId = null
) : IHumanQuery<Result<List<DeviceLogListItemDto>>>;

[AuthorizeRequirement("Device.Read")]
public record GetRecentAlertCountQuery(
    int SinceHours = 24,
    string? MinLevel = "WARN",
    Guid? ProcessId = null
) : IHumanQuery<Result<RecentAlertCountDto>>;

public class GetRecentDeviceLogsHandler(
    ICurrentUser currentUser,
    IDevicePermissionService devicePermissionService,
    IDeviceLogQueryService queryService)
    : IQueryHandler<GetRecentDeviceLogsQuery, Result<List<DeviceLogListItemDto>>>
{
    private const int MaxLimit = 100;

    public async Task<Result<List<DeviceLogListItemDto>>> Handle(
        GetRecentDeviceLogsQuery request,
        CancellationToken cancellationToken)
    {
        if (!DeviceLogSeverityLevels.TryGetLevelsAtOrAbove(
                request.MinLevel,
                out var levels,
                out _))
        {
            return Result.Invalid("日志等级仅支持 INFO、WARN、ERROR。");
        }

        var scope = await ResolveAllowedDeviceIdsAsync(cancellationToken);
        if (scope.IsFailure)
        {
            return Result.Failure(scope.ErrorMessage!);
        }

        if (scope.DeviceIds is { Count: 0 })
        {
            return Result.Success(new List<DeviceLogListItemDto>());
        }

        var limit = Math.Clamp(request.Limit, 1, MaxLimit);
        var items = await queryService.GetRecentLogsAsync(
            limit,
            levels,
            request.ProcessId,
            scope.DeviceIds,
            cancellationToken);

        return Result.Success(items);
    }

    private async Task<DeviceScopeResult> ResolveAllowedDeviceIdsAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(currentUser.Role, SystemRoles.Admin, StringComparison.Ordinal))
        {
            return DeviceScopeResult.Success(null);
        }

        if (!Guid.TryParse(currentUser.Id, out var userId))
        {
            return DeviceScopeResult.Failure("用户凭证异常");
        }

        var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
            userId,
            isAdmin: false,
            cancellationToken);

        return DeviceScopeResult.Success(accessibleDeviceIds?.ToList() ?? []);
    }

    private sealed record DeviceScopeResult(IReadOnlyList<Guid>? DeviceIds, string? ErrorMessage)
    {
        public bool IsFailure => ErrorMessage is not null;

        public static DeviceScopeResult Success(IReadOnlyList<Guid>? deviceIds)
        {
            return new DeviceScopeResult(deviceIds, null);
        }

        public static DeviceScopeResult Failure(string errorMessage)
        {
            return new DeviceScopeResult(null, errorMessage);
        }
    }
}

public class GetRecentAlertCountHandler(
    ICurrentUser currentUser,
    IDevicePermissionService devicePermissionService,
    IDeviceLogQueryService queryService)
    : IQueryHandler<GetRecentAlertCountQuery, Result<RecentAlertCountDto>>
{
    private const int DefaultSinceHours = 24;
    private const int MaxSinceHours = 168;

    public async Task<Result<RecentAlertCountDto>> Handle(
        GetRecentAlertCountQuery request,
        CancellationToken cancellationToken)
    {
        if (!DeviceLogSeverityLevels.TryGetLevelsAtOrAbove(
                request.MinLevel,
                out var levels,
                out var normalizedMinLevel))
        {
            return Result.Invalid("日志等级仅支持 INFO、WARN、ERROR。");
        }

        var scope = await ResolveAllowedDeviceIdsAsync(cancellationToken);
        if (scope.IsFailure)
        {
            return Result.Failure(scope.ErrorMessage!);
        }

        var now = DateTimeOffset.UtcNow;
        var sinceHours = request.SinceHours <= 0
            ? DefaultSinceHours
            : Math.Min(request.SinceHours, MaxSinceHours);
        var windowStart = now.AddHours(-sinceHours);

        var count = scope.DeviceIds is { Count: 0 }
            ? 0
            : await queryService.CountRecentAlertsAsync(
                windowStart,
                levels,
                request.ProcessId,
                scope.DeviceIds,
                cancellationToken);

        return Result.Success(new RecentAlertCountDto(
            count,
            sinceHours,
            normalizedMinLevel,
            windowStart,
            now,
            now));
    }

    private async Task<DeviceScopeResult> ResolveAllowedDeviceIdsAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(currentUser.Role, SystemRoles.Admin, StringComparison.Ordinal))
        {
            return DeviceScopeResult.Success(null);
        }

        if (!Guid.TryParse(currentUser.Id, out var userId))
        {
            return DeviceScopeResult.Failure("用户凭证异常");
        }

        var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
            userId,
            isAdmin: false,
            cancellationToken);

        return DeviceScopeResult.Success(accessibleDeviceIds?.ToList() ?? []);
    }

    private sealed record DeviceScopeResult(IReadOnlyList<Guid>? DeviceIds, string? ErrorMessage)
    {
        public bool IsFailure => ErrorMessage is not null;

        public static DeviceScopeResult Success(IReadOnlyList<Guid>? deviceIds)
        {
            return new DeviceScopeResult(deviceIds, null);
        }

        public static DeviceScopeResult Failure(string errorMessage)
        {
            return new DeviceScopeResult(null, errorMessage);
        }
    }
}
