using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

[AuthorizeRequirement("Device.Read")]
public record GetHourlyCapacityAggregateQuery(
    DateOnly Date,
    Guid? ProcessId = null
) : IHumanQuery<Result<List<HourlyCapacityAggregateDto>>>;

public class GetHourlyCapacityAggregateHandler(
    ICurrentUser currentUser,
    IDevicePermissionService devicePermissionService,
    ICapacityQueryService queryService)
    : IQueryHandler<GetHourlyCapacityAggregateQuery, Result<List<HourlyCapacityAggregateDto>>>
{
    public async Task<Result<List<HourlyCapacityAggregateDto>>> Handle(
        GetHourlyCapacityAggregateQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await ResolveAllowedDeviceIdsAsync(cancellationToken);
        if (scope.IsFailure)
        {
            return Result.Failure(scope.ErrorMessage!);
        }

        if (scope.DeviceIds is { Count: 0 })
        {
            return Result.Success(new List<HourlyCapacityAggregateDto>());
        }

        var data = await queryService.GetHourlyAggregateAsync(
            request.Date,
            request.ProcessId,
            scope.DeviceIds,
            cancellationToken);

        return Result.Success(data);
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
