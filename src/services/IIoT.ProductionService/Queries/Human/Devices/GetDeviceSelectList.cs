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
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IReadRepository<Device> deviceRepository)
    : IQueryHandler<GetDeviceSelectListQuery, Result<List<DeviceSelectDto>>>
{
    public async Task<Result<List<DeviceSelectDto>>> Handle(
        GetDeviceSelectListQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await currentUserDeviceAccessService.GetAccessibleDeviceIdsAsync(cancellationToken);
        if (!scope.IsSuccess)
        {
            return Result.Failure(scope.Errors?.ToArray() ?? ["用户凭证异常"]);
        }

        var allowedDeviceIds = scope.Value?.ToList();
        if (allowedDeviceIds is { Count: 0 })
        {
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
