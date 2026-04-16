using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Exceptions;
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
    ICurrentUser currentUser,
    IReadRepository<Device> deviceRepository,
    ICacheService cacheService
) : IQueryHandler<GetAllDevicesQuery, Result<List<DeviceSelectDto>>>
{
    public async Task<Result<List<DeviceSelectDto>>> Handle(GetAllDevicesQuery request, CancellationToken cancellationToken)
    {
        if (!string.Equals(currentUser.Role, "Admin", StringComparison.Ordinal))
            throw new ForbiddenException("仅管理员可查看全量设备列表");

        var cacheKey = CacheKeys.AllDevices();

        var cached = await cacheService.GetAsync<List<DeviceSelectDto>>(cacheKey, cancellationToken);
        if (cached != null) return Result.Success(cached);

        var list = await deviceRepository.GetListAsync(cancellationToken: cancellationToken);

        var dtos = list.Select(d => new DeviceSelectDto(
            d.Id, d.DeviceName, d.Code, d.ProcessId
        )).ToList();

        await cacheService.SetAsync(cacheKey, dtos, TimeSpan.FromHours(2), cancellationToken);

        return Result.Success(dtos);
    }
}
