using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Queries.Devices;

[AuthorizeRequirement(DevicePermissions.MigrateProcess)]
[AdminOnly]
public sealed record GetDeviceProcessMigrationImpactQuery(
    Guid DeviceId,
    Guid TargetProcessId)
    : IHumanQuery<Result<DeviceProcessMigrationImpactDto>>,
        IAdminOnlyAuditRequest
{
    public string AdminAuditOperationType => "Device.ProcessMigrationImpact.Read";

    public string AdminAuditTargetType => "Device";

    public string AdminAuditTargetIdOrKey => DeviceId.ToString();
}

public sealed record DeviceProcessMigrationProcessDto(
    Guid Id,
    string ProcessCode,
    string ProcessName);

public sealed record DeviceProcessMigrationBlockerDto(
    string Code,
    string Message,
    long Count);

public sealed record DeviceProcessMigrationImpactDto(
    Guid DeviceId,
    string DeviceName,
    string ClientCode,
    DeviceProcessMigrationProcessDto SourceProcess,
    DeviceProcessMigrationProcessDto TargetProcess,
    uint RowVersion,
    DeviceDeletionImpact RelatedCounts,
    IReadOnlyList<DeviceProcessMigrationBlockerDto> Blockers,
    string ConfirmationText,
    bool CanMigrate);

public sealed class GetDeviceProcessMigrationImpactHandler(
    IReadRepository<Device> deviceRepository,
    IProcessReadQueryService processReadQueryService,
    IDeviceDeletionDependencyQueryService dependencyQueryService,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService)
    : IQueryHandler<
        GetDeviceProcessMigrationImpactQuery,
        Result<DeviceProcessMigrationImpactDto>>
{
    public async Task<Result<DeviceProcessMigrationImpactDto>> Handle(
        GetDeviceProcessMigrationImpactQuery request,
        CancellationToken cancellationToken)
    {
        if (request.TargetProcessId == Guid.Empty)
        {
            return Result.Failure("目标工序不能为空。");
        }

        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(request.DeviceId),
            cancellationToken);
        if (device is null)
        {
            return Result.Failure("目标设备不存在。");
        }

        var access = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            device.Id,
            cancellationToken);
        if (!access.IsSuccess)
        {
            return Result.Failure(
                access.Errors?.ToArray() ?? ["越权：未授权访问该设备。"]);
        }

        var processes = await processReadQueryService.GetByIdsAsync(
            [device.ProcessId, request.TargetProcessId],
            cancellationToken);
        var source = processes.SingleOrDefault(
            process => process.Id == device.ProcessId);
        var target = processes.SingleOrDefault(
            process => process.Id == request.TargetProcessId);
        if (source is null)
        {
            return Result.Failure("设备当前工序主数据不存在。");
        }

        if (target is null)
        {
            return Result.Failure("目标工序不存在。");
        }

        var impact = await dependencyQueryService.GetImpactAsync(
            device.Id,
            cancellationToken);
        var blockers = DeviceProcessMigrationPolicy.BuildBlockers(
            device.ProcessId,
            target.Id,
            impact);
        var confirmationText = DeviceProcessMigrationPolicy.BuildConfirmationText(
            device.Code,
            target.ProcessCode);

        return Result.Success(new DeviceProcessMigrationImpactDto(
            device.Id,
            device.DeviceName,
            device.Code,
            ToDto(source),
            ToDto(target),
            device.RowVersion,
            impact,
            blockers,
            confirmationText,
            blockers.Count == 0));
    }

    private static DeviceProcessMigrationProcessDto ToDto(ProcessReadItem process)
        => new(process.Id, process.ProcessCode, process.ProcessName);
}

public static class DeviceProcessMigrationPolicy
{
    public static string BuildConfirmationText(
        string clientCode,
        string targetProcessCode)
        => $"MIGRATE {clientCode} TO {targetProcessCode}";

    public static IReadOnlyList<DeviceProcessMigrationBlockerDto> BuildBlockers(
        Guid sourceProcessId,
        Guid targetProcessId,
        DeviceDeletionImpact impact)
    {
        var blockers = new List<DeviceProcessMigrationBlockerDto>();
        if (sourceProcessId == targetProcessId)
        {
            blockers.Add(new DeviceProcessMigrationBlockerDto(
                "same_process",
                "设备已经属于目标工序。",
                0));
        }

        AddIfNonZero("recipes", "存在绑定旧工序的配方记录。", impact.Recipes);
        AddIfNonZero(
            "hourly_capacity",
            "存在带旧工序语义的产能记录。",
            impact.Capacities);
        AddIfNonZero(
            "pass_station_records",
            "存在带旧工序语义的过站记录。",
            impact.PassStations);
        AddIfNonZero(
            "edge_host_plc_runtime_states",
            "存在绑定旧工序的 PLC 运行状态。",
            impact.EdgeHostPlcRuntimeStates);
        return blockers;

        void AddIfNonZero(string code, string message, long count)
        {
            if (count > 0)
            {
                blockers.Add(new DeviceProcessMigrationBlockerDto(
                    code,
                    message,
                    count));
            }
        }
    }
}
