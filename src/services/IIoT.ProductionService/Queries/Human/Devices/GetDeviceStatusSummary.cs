using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

[AuthorizeRequirement("Device.Read")]
public record GetDeviceStatusSummaryQuery() : IHumanQuery<Result<DeviceStatusSummaryDto>>;

public class GetDeviceStatusSummaryHandler(
    ICurrentUser currentUser,
    IDevicePermissionService devicePermissionService,
    IDeviceOperationalStatusQueryService queryService)
    : IQueryHandler<GetDeviceStatusSummaryQuery, Result<DeviceStatusSummaryDto>>
{
    private const int OfflineThresholdMinutes = 60;
    private const int StatusLogWindowHours = 24;

    public async Task<Result<DeviceStatusSummaryDto>> Handle(
        GetDeviceStatusSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var allowedDeviceIds = await ResolveAllowedDeviceIdsAsync(cancellationToken);
        if (allowedDeviceIds.IsFailure)
        {
            return Result.Failure(allowedDeviceIds.ErrorMessage!);
        }

        if (allowedDeviceIds.DeviceIds is { Count: 0 })
        {
            return Result.Success(new DeviceStatusSummaryDto(0, 0, 0, 0, 0, DateTimeOffset.UtcNow));
        }

        var now = DateTimeOffset.UtcNow;
        var summary = await queryService.GetStatusSummaryAsync(
            now.AddMinutes(-OfflineThresholdMinutes),
            now.AddHours(-StatusLogWindowHours),
            allowedDeviceIds.DeviceIds,
            cancellationToken);

        return Result.Success(summary);
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
