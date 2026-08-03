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

public sealed record DeviceLedgerProcessOptionDto(
    Guid Id,
    string ProcessCode,
    string ProcessName);

[AuthorizeRequirement(DevicePermissions.Read)]
public sealed record GetDeviceLedgerProcessOptionsQuery()
    : IHumanQuery<Result<List<DeviceLedgerProcessOptionDto>>>;

public sealed class GetDeviceLedgerProcessOptionsHandler(
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IReadRepository<Device> deviceRepository,
    IProcessReadQueryService processReadQueryService)
    : IQueryHandler<
        GetDeviceLedgerProcessOptionsQuery,
        Result<List<DeviceLedgerProcessOptionDto>>>
{
    public async Task<Result<List<DeviceLedgerProcessOptionDto>>> Handle(
        GetDeviceLedgerProcessOptionsQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProcessReadItem> processes;
        if (currentUserDeviceAccessService.IsAdministrator)
        {
            (processes, _) = await processReadQueryService.GetPagedAsync(
                null,
                null,
                0,
                int.MaxValue,
                cancellationToken);
        }
        else
        {
            var scope = await currentUserDeviceAccessService
                .GetAccessibleDeviceIdsAsync(cancellationToken);
            if (!scope.IsSuccess)
            {
                return Result.Failure(
                    scope.Errors?.ToArray() ?? ["用户凭证异常"]);
            }

            var allowedDeviceIds = scope.Value?.ToList();
            if (allowedDeviceIds is null or { Count: 0 })
            {
                return Result.Success(new List<DeviceLedgerProcessOptionDto>());
            }

            var devices = await deviceRepository.GetListAsync(
                new DevicePagedSpec(
                    0,
                    0,
                    allowedDeviceIds,
                    isPaging: false),
                cancellationToken);
            processes = await processReadQueryService.GetByIdsAsync(
                devices.Select(device => device.ProcessId).Distinct().ToArray(),
                cancellationToken);
        }

        return Result.Success(processes
            .OrderBy(process => process.ProcessCode, StringComparer.Ordinal)
            .ThenBy(process => process.Id)
            .Select(process => new DeviceLedgerProcessOptionDto(
                process.Id,
                process.ProcessCode,
                process.ProcessName))
            .ToList());
    }
}
