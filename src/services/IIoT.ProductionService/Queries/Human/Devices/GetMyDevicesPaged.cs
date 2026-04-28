using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

public record DeviceListItemDto(
    Guid Id,
    string DeviceName,
    string Code,
    Guid ProcessId
);

[AuthorizeRequirement("Device.Read")]
public record GetMyDevicesPagedQuery(Pagination PaginationParams, string? Keyword = null) : IHumanQuery<Result<PagedList<DeviceListItemDto>>>;

public class GetMyDevicesPagedHandler(
    ICurrentUser currentUser,
    IDevicePermissionService devicePermissionService,
    IReadRepository<Device> deviceRepository
) : IQueryHandler<GetMyDevicesPagedQuery, Result<PagedList<DeviceListItemDto>>>
{
    public async Task<Result<PagedList<DeviceListItemDto>>> Handle(
        GetMyDevicesPagedQuery request,
        CancellationToken cancellationToken)
    {
        List<Guid>? allowedDeviceIds = null;

        if (!string.Equals(
                currentUser.Role,
                IIoT.Services.Contracts.Authorization.SystemRoles.Admin,
                StringComparison.Ordinal))
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
                userId,
                isAdmin: false,
                cancellationToken);
            allowedDeviceIds = accessibleDeviceIds?.ToList();

            if (allowedDeviceIds is null || allowedDeviceIds.Count == 0)
            {
                var emptyList = new PagedList<DeviceListItemDto>([], 0, request.PaginationParams);
                return Result.Success(emptyList);
            }
        }

        var skip = (request.PaginationParams.PageNumber - 1) * request.PaginationParams.PageSize;
        var take = request.PaginationParams.PageSize;

        var countSpec = new DevicePagedSpec(0, 0, allowedDeviceIds, request.Keyword, isPaging: false);
        var totalCount = await deviceRepository.CountAsync(countSpec, cancellationToken);

        List<Device> list = [];
        if (totalCount > 0)
        {
            var pagedSpec = new DevicePagedSpec(skip, take, allowedDeviceIds, request.Keyword, isPaging: true);
            list = await deviceRepository.GetListAsync(pagedSpec, cancellationToken);
        }

        var dtos = list.Select(d => new DeviceListItemDto(
            d.Id,
            d.DeviceName,
            d.Code,
            d.ProcessId
        )).ToList();

        var pagedList = new PagedList<DeviceListItemDto>(dtos, totalCount, request.PaginationParams);

        return Result.Success(pagedList);
    }
}
