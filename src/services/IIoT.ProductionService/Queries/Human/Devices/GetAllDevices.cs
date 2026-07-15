using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
using IIoT.Services.Contracts;
using IIoT.Services.CrossCutting.Exceptions;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

public record DeviceSelectDto(
    Guid Id,
    string DeviceName,
    string Code,
    Guid ProcessId
);

[AuthorizeRequirement("Device.Read")]
public record GetAllDevicesQuery() : IHumanQuery<Result<List<DeviceSelectDto>>>;

public class GetAllDevicesHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IReadRepository<Device> deviceRepository,
    ICacheService cacheService
) : IQueryHandler<GetAllDevicesQuery, Result<List<DeviceSelectDto>>>
{
    public async Task<Result<List<DeviceSelectDto>>> Handle(GetAllDevicesQuery request, CancellationToken cancellationToken)
    {
        if (!currentUserDeviceAccessService.IsAdministrator)
            throw new ForbiddenException("仅管理员可查看全量设备列表");

        var cacheKey = CacheKeys.AllDevices();

        var dtos = await cacheService.GetOrSetAsync<List<DeviceSelectDto>>(
            cacheKey,
            async factoryCancellationToken =>
            {
                var list = await deviceRepository.GetListAsync(
                    cancellationToken: factoryCancellationToken);
                return list.Select(device => new DeviceSelectDto(
                    device.Id,
                    device.DeviceName,
                    device.Code,
                    device.ProcessId
                )).ToList();
            },
            static value => value is not null,
            TimeSpan.FromHours(2),
            cancellationToken);

        return Result.Success(dtos
            ?? throw new InvalidOperationException("Device cache factory returned null."));
    }
}
