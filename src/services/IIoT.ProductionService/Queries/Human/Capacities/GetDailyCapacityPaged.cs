using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Capacities;

/// <summary>
/// 机台日产能分页查询。
/// </summary>
[AuthorizeRequirement("Device.Read")]
public record GetDailyCapacityPagedQuery(
    Pagination PaginationParams,
    DateOnly? Date = null,
    Guid? DeviceId = null
) : IHumanQuery<Result<PagedList<DailyCapacityPagedItemDto>>>;

public class GetDailyCapacityPagedHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    ICapacityQueryService queryService,
    ICacheService cacheService
) : IQueryHandler<GetDailyCapacityPagedQuery, Result<PagedList<DailyCapacityPagedItemDto>>>
{
    public async Task<Result<PagedList<DailyCapacityPagedItemDto>>> Handle(
        GetDailyCapacityPagedQuery request,
        CancellationToken cancellationToken)
    {
        List<Guid>? allowedDeviceIds = null;

        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(userId),
                cancellationToken);

            if (employee == null)
                return Result.Failure("系统中未找到您的员工档案");

            allowedDeviceIds = employee.DeviceAccesses
                .Select(d => d.DeviceId)
                .ToList();

            if (allowedDeviceIds.Count == 0)
                return Result.Success(new PagedList<DailyCapacityPagedItemDto>([], 0, request.PaginationParams));

            if (request.DeviceId.HasValue && !allowedDeviceIds.Contains(request.DeviceId.Value))
                return Result.Failure("无权查看该设备产能");
        }

        var cacheKey = CacheKeys.CapacityPaged(
            request.Date,
            request.DeviceId,
            request.PaginationParams.PageNumber,
            request.PaginationParams.PageSize);

        var canUseCache = currentUser.Role == "Admin" || request.DeviceId.HasValue;

        if (canUseCache)
        {
            var cached = await cacheService.GetAsync<PagedList<DailyCapacityPagedItemDto>>(
                cacheKey, cancellationToken);
            if (cached is not null)
                return Result.Success(cached);
        }

        var (items, totalCount) = await queryService.GetDailyPagedAsync(
            request.PaginationParams,
            request.Date,
            request.DeviceId,
            request.DeviceId.HasValue ? null : allowedDeviceIds,
            cancellationToken);

        var pagedList = new PagedList<DailyCapacityPagedItemDto>(
            items, totalCount, request.PaginationParams);

        if (canUseCache)
        {
            await cacheService.SetAsync(
                cacheKey, pagedList, TimeSpan.FromMinutes(5), cancellationToken);
        }

        return Result.Success(pagedList);
    }
}
