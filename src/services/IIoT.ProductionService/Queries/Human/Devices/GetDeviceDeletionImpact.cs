using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

[AuthorizeRequirement(DevicePermissions.Delete)]
[AuthorizeRequirement(DevicePermissions.CascadeDelete)]
public sealed record GetDeviceDeletionImpactQuery(Guid DeviceId)
    : IHumanQuery<Result<DeviceDeletionImpactDto>>;

public sealed record DeviceDeletionImpactDto(
    Guid DeviceId,
    string DeviceName,
    string ClientCode,
    Guid ProcessId,
    long Recipes,
    long Capacities,
    long DeviceLogs,
    long PassStations,
    long ClientVersionSnapshots,
    long ClientPluginVersions,
    long UploadReceiveRegistrations,
    long EmployeeDeviceAccesses,
    long RefreshTokenSessions,
    long TotalAssociatedRows);

public sealed class GetDeviceDeletionImpactHandler(
    IReadRepository<Device> deviceRepository,
    IDeviceDeletionDependencyQueryService dependencyQueryService,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService)
    : IQueryHandler<GetDeviceDeletionImpactQuery, Result<DeviceDeletionImpactDto>>
{
    public async Task<Result<DeviceDeletionImpactDto>> Handle(
        GetDeviceDeletionImpactQuery request,
        CancellationToken cancellationToken)
    {
        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(request.DeviceId),
            cancellationToken);

        if (device is null)
        {
            return Result.Failure("目标设备不存在");
        }

        var access = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            device.Id,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.Failure(access.Errors?.ToArray() ?? ["越权: 未授权访问该设备"]);
        }

        var impact = await dependencyQueryService.GetImpactAsync(device.Id, cancellationToken);
        return Result.Success(new DeviceDeletionImpactDto(
            device.Id,
            device.DeviceName,
            device.Code,
            device.ProcessId,
            impact.Recipes,
            impact.Capacities,
            impact.DeviceLogs,
            impact.PassStations,
            impact.ClientVersionSnapshots,
            impact.ClientPluginVersions,
            impact.UploadReceiveRegistrations,
            impact.EmployeeDeviceAccesses,
            impact.RefreshTokenSessions,
            impact.TotalAssociatedRows));
    }
}
