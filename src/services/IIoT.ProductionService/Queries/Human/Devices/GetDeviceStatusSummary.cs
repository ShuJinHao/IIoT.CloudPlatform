using IIoT.Core.Production.Contracts.ClientReleases;
using IIoT.ProductionService.ClientReleases;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

[AuthorizeRequirement(DeviceClientOverviewPermissions.Read)]
public record GetDeviceStatusSummaryQuery(
    Guid? DeviceId = null
) : IHumanQuery<Result<DeviceStatusSummaryDto>>;

public class GetDeviceStatusSummaryHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IDeviceOperationalStatusQueryService queryService,
    IDeviceClientStateQueryService clientStateQueryService)
    : IQueryHandler<GetDeviceStatusSummaryQuery, Result<DeviceStatusSummaryDto>>
{
    public async Task<Result<DeviceStatusSummaryDto>> Handle(
        GetDeviceStatusSummaryQuery request,
        CancellationToken cancellationToken)
    {
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
            var allowedDeviceIds = await currentUserDeviceAccessService
                .GetAccessibleDeviceIdsAsync(cancellationToken);
            if (!allowedDeviceIds.IsSuccess)
            {
                return Result.Failure(allowedDeviceIds.Errors?.ToArray() ?? ["用户凭证异常"]);
            }

            if (allowedDeviceIds.Value is { Count: 0 })
            {
                return Result.Success(
                    new DeviceStatusSummaryDto(0, 0, 0, 0, 0, DateTimeOffset.UtcNow));
            }

            deviceIds = allowedDeviceIds.Value;
        }

        var targets = await queryService.GetScopedDevicesAsync(
            deviceIds,
            cancellationToken);
        if (targets.Count == 0)
        {
            return Result.Success(
                new DeviceStatusSummaryDto(0, 0, 0, 0, 0, DateTimeOffset.UtcNow));
        }

        var states = await clientStateQueryService.GetStatesByDevicesAsync(
            targets.Select(target => target.DeviceId).ToArray(),
            cancellationToken);
        var statesByIdentity = states
            .GroupBy(state => (state.DeviceId, NormalizeClientCode(state.ClientCode)))
            .ToDictionary(group => group.Key, group => group.First());
        var now = DateTimeOffset.UtcNow;
        var resolutions = targets
            .Select(target =>
            {
                statesByIdentity.TryGetValue(
                    (target.DeviceId, NormalizeClientCode(target.ClientCode)),
                    out var state);
                return DeviceClientSoftwareStatusResolver.Resolve(state, now.UtcDateTime);
            })
            .ToArray();

        var summary = new DeviceStatusSummaryDto(
            targets.Count,
            resolutions.Count(status => status.SoftwareStatus == "Running"),
            resolutions.Count(status => status.SoftwareStatus == "Starting"),
            resolutions.Count(status => status.SoftwareStatus == "Unknown"),
            resolutions.Count(status => status.SoftwareStatus is "Stopped" or "MissingRuntimeHeartbeat" or "RuntimeHeartbeatStale"),
            now,
            resolutions.Length == 1 ? resolutions[0].SoftwareStatus : null,
            resolutions.Length == 1 ? resolutions[0].Issue : null);

        return Result.Success(summary);
    }

    private static string NormalizeClientCode(string clientCode)
        => clientCode.Trim().ToUpperInvariant();
}
