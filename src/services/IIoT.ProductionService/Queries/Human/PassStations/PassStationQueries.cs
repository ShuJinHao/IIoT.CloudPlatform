using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Paging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.PassStations;

[AuthorizeRequirement("Device.Read")]
public record GetPassStationListQuery<TDto>(
    Pagination PaginationParams,
    Guid? ProcessId = null,
    Guid? DeviceId = null,
    string? Barcode = null,
    DateTime? StartTime = null,
    DateTime? EndTime = null
) : IHumanQuery<Result<PagedList<TDto>>>;

public sealed class GetPassStationListHandler<TDto>(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IDataQueryService dataQueryService,
    IPassStationQueryService<TDto> queryService)
    : IQueryHandler<GetPassStationListQuery<TDto>, Result<PagedList<TDto>>>
{
    public async Task<Result<PagedList<TDto>>> Handle(
        GetPassStationListQuery<TDto> request,
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

            if (request.DeviceId.HasValue && !allowedDeviceIds.Contains(request.DeviceId.Value))
                return Result.Failure("无权查看该设备过站记录");
        }

        List<Guid>? deviceIds = null;

        if (request.ProcessId.HasValue)
        {
            var devices = await dataQueryService.ToListAsync(
                dataQueryService.Devices.Where(d => d.ProcessId == request.ProcessId.Value));

            if (devices.Count == 0)
                return Result.Failure("该工序下没有设备");

            deviceIds = devices.Select(d => d.Id).ToList();

            if (allowedDeviceIds is not null)
            {
                deviceIds = deviceIds.Intersect(allowedDeviceIds).ToList();

                if (deviceIds.Count == 0)
                    return Result.Success(new PagedList<TDto>([], 0, request.PaginationParams));
            }
        }
        else if (!request.DeviceId.HasValue && allowedDeviceIds is { Count: > 0 })
        {
            deviceIds = allowedDeviceIds;
        }
        else if (!request.DeviceId.HasValue && allowedDeviceIds is { Count: 0 })
        {
            return Result.Success(new PagedList<TDto>([], 0, request.PaginationParams));
        }

        var (items, totalCount) = await queryService.GetByConditionAsync(
            request.PaginationParams,
            deviceIds: deviceIds,
            deviceId: request.DeviceId,
            barcode: request.Barcode,
            startTime: request.StartTime,
            endTime: request.EndTime,
            cancellationToken: cancellationToken);

        return Result.Success(new PagedList<TDto>(items, totalCount, request.PaginationParams));
    }
}

[AuthorizeRequirement("Device.Read")]
public record GetPassStationDetailQuery<TDto>(Guid Id) : IHumanQuery<Result<TDto>>;

public sealed class GetPassStationDetailHandler<TDto>(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IPassStationQueryService<TDto> queryService)
    : IQueryHandler<GetPassStationDetailQuery<TDto>, Result<TDto>>
{
    public async Task<Result<TDto>> Handle(
        GetPassStationDetailQuery<TDto> request,
        CancellationToken cancellationToken)
    {
        var detail = await queryService.GetDetailAsync(request.Id, cancellationToken);
        if (detail is null)
            return Result.Failure("未找到该过站记录");

        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(userId),
                cancellationToken);

            if (employee == null)
                return Result.Failure("系统中未找到您的员工档案");

            var deviceId = TryReadDeviceId(detail);
            if (deviceId == Guid.Empty)
                throw new InvalidOperationException(
                    $"Pass station detail dto '{typeof(TDto).Name}' must expose a DeviceId property.");

            if (!employee.DeviceAccesses.Any(d => d.DeviceId == deviceId))
                return Result.Failure("无权查看该过站记录");
        }

        return Result.Success(detail);
    }

    private static Guid TryReadDeviceId(TDto detail)
    {
        var property = typeof(TDto).GetProperty("DeviceId");
        if (property?.PropertyType != typeof(Guid))
            return Guid.Empty;

        return property.GetValue(detail) is Guid deviceId ? deviceId : Guid.Empty;
    }
}

[AuthorizeRequirement("Device.Read")]
public record GetPassStationLatest200Query<TDto>(
    Guid DeviceId,
    Pagination PaginationParams
) : IHumanQuery<Result<PagedList<TDto>>>;

public sealed class GetPassStationLatest200Handler<TDto>(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IPassStationQueryService<TDto> queryService)
    : IQueryHandler<GetPassStationLatest200Query<TDto>, Result<PagedList<TDto>>>
{
    public async Task<Result<PagedList<TDto>>> Handle(
        GetPassStationLatest200Query<TDto> request,
        CancellationToken cancellationToken)
    {
        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(userId),
                cancellationToken);

            if (employee == null)
                return Result.Failure("系统中未找到您的员工档案");

            if (!employee.DeviceAccesses.Any(d => d.DeviceId == request.DeviceId))
                return Result.Failure("无权查看该设备过站记录");
        }

        var (items, totalCount) = await queryService.GetLatest200ByDeviceAsync(
            request.DeviceId,
            request.PaginationParams,
            cancellationToken);

        return Result.Success(new PagedList<TDto>(items, totalCount, request.PaginationParams));
    }
}
