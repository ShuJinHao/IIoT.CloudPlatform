using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

[AuthorizeRequirement("Device.Read")]
public record GetDeviceSelectListQuery() : IHumanQuery<Result<List<DeviceSelectDto>>>;

public class GetDeviceSelectListHandler(
    ICurrentUser currentUser,
    IDevicePermissionService devicePermissionService,
    IReadRepository<Device> deviceRepository)
    : IQueryHandler<GetDeviceSelectListQuery, Result<List<DeviceSelectDto>>>
{
    public async Task<Result<List<DeviceSelectDto>>> Handle(
        GetDeviceSelectListQuery request,
        CancellationToken cancellationToken)
    {
        List<Guid>? allowedDeviceIds = null;

        if (!string.Equals(currentUser.Role, SystemRoles.Admin, StringComparison.Ordinal))
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
                userId,
                isAdmin: false,
                cancellationToken);

            allowedDeviceIds = accessibleDeviceIds?.ToList();
            if (allowedDeviceIds is null || allowedDeviceIds.Count == 0)
                return Result.Success(new List<DeviceSelectDto>());
        }

        var spec = new DevicePagedSpec(0, 0, allowedDeviceIds, isPaging: false);
        var devices = await deviceRepository.GetListAsync(spec, cancellationToken);

        var dtos = devices
            .Select(d => new DeviceSelectDto(d.Id, d.DeviceName, d.Code, d.ProcessId))
            .ToList();

        return Result.Success(dtos);
    }
}
