using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

public sealed record ScopedDeviceSelectDto(
    Guid Id,
    string DeviceName,
    string Code,
    Guid ProcessId,
    string ProcessCode,
    string ProcessName);

[AuthorizeRequirement("Device.Read")]
public record GetDeviceSelectListQuery() : IHumanQuery<Result<List<ScopedDeviceSelectDto>>>;

public class GetDeviceSelectListHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IReadRepository<Device> deviceRepository,
    IProcessReadQueryService processReadQueryService)
    : IQueryHandler<GetDeviceSelectListQuery, Result<List<ScopedDeviceSelectDto>>>
{
    public async Task<Result<List<ScopedDeviceSelectDto>>> Handle(
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
            return Result.Success(new List<ScopedDeviceSelectDto>());
        }

        var spec = new DevicePagedSpec(0, 0, allowedDeviceIds, isPaging: false);
        var devices = await deviceRepository.GetListAsync(spec, cancellationToken);
        var processIds = devices
            .Select(device => device.ProcessId)
            .Distinct()
            .ToArray();
        var processes = await processReadQueryService.GetByIdsAsync(
            processIds,
            cancellationToken);
        var processById = processes.ToDictionary(process => process.Id);

        if (processById.Count != processIds.Length)
        {
            return Result.Failure("设备关联的工序主数据不完整。");
        }

        var dtos = devices.Select(device =>
        {
            var process = processById[device.ProcessId];
            return new ScopedDeviceSelectDto(
                device.Id,
                device.DeviceName,
                device.Code,
                device.ProcessId,
                process.ProcessCode,
                process.ProcessName);
        }).ToList();

        return Result.Success(dtos);
    }
}
