using System.Text.Json;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Caching;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

[AuthorizeRequirement(DevicePermissions.Delete)]
[AuthorizeRequirement(DevicePermissions.CascadeDelete)]
public record DeleteDeviceCommand(Guid DeviceId) : IHumanCommand<Result<bool>>;

public class DeleteDeviceHandler(
    ICurrentUser currentUser,
    IRepository<Device> deviceRepository,
    IDeviceDeletionDependencyQueryService dependencyQueryService,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IDeviceCacheInvalidationService deviceCacheInvalidationService,
    IAuditTrailService auditTrailService)
    : ICommandHandler<DeleteDeviceCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteDeviceCommand request,
        CancellationToken cancellationToken)
    {
        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(request.DeviceId),
            cancellationToken);

        if (device is null)
            return await FailAsync(request.DeviceId.ToString(), "目标设备不存在", cancellationToken);

        var deviceAccess = await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
            device.Id,
            cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return await FailAsync(
                device.Id.ToString(),
                deviceAccess.Errors?.FirstOrDefault() ?? "越权：未授权访问该设备",
                cancellationToken);
        }

        var deletionResult = await dependencyQueryService.DeleteCascadeAsync(
            request.DeviceId,
            cancellationToken);

        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "Device.Delete",
                "Device",
                device.Id.ToString(),
                DateTime.UtcNow,
                deletionResult.DeviceDeleted,
                BuildDeletionAuditSummary(device, deletionResult.Impact),
                deletionResult.DeviceDeleted ? null : "设备级联删除未删除设备主数据。"),
            cancellationToken);

        if (deletionResult.DeviceDeleted)
        {
            await deviceCacheInvalidationService.InvalidateAfterDeleteAsync(
                new DeviceCacheDescriptor(device.Id, device.ProcessId, device.Code),
                deletionResult.AffectedEmployeeIds,
                cancellationToken);
        }

        return Result.Success(deletionResult.DeviceDeleted);
    }

    private async Task<Result<bool>> FailAsync(
        string targetIdOrKey,
        string message,
        CancellationToken cancellationToken)
    {
        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "Device.Delete",
                "Device",
                targetIdOrKey,
                DateTime.UtcNow,
                false,
                $"删除设备 {targetIdOrKey}。",
                message),
            cancellationToken);

        return Result.Failure(message);
    }

    private static Guid? ParseActorUserId(string? rawUserId)
    {
        return Guid.TryParse(rawUserId, out var actorUserId)
            ? actorUserId
            : null;
    }

    private static string BuildDeletionAuditSummary(
        Device device,
        DeviceDeletionImpact impact)
    {
        return JsonSerializer.Serialize(new
        {
            action = "DeviceCascadeDelete",
            deviceId = device.Id,
            deviceName = device.DeviceName,
            clientCode = device.Code,
            processId = device.ProcessId,
            deleted = new
            {
                recipes = impact.Recipes,
                hourly_capacity = impact.Capacities,
                device_logs = impact.DeviceLogs,
                pass_station_records = impact.PassStations,
                edge_device_client_states = impact.ClientStates,
                edge_device_client_version_snapshots = impact.ClientVersionSnapshots,
                edge_device_client_plugin_versions = impact.ClientPluginVersions,
                edge_device_runtime_heartbeats = impact.RuntimeHeartbeats,
                upload_receive_registrations = impact.UploadReceiveRegistrations,
                employee_device_accesses = impact.EmployeeDeviceAccesses,
                refresh_token_sessions = impact.RefreshTokenSessions,
                edge_hosts = impact.EdgeHosts,
                edge_host_plc_bindings = impact.EdgeHostPlcBindings,
                edge_host_plc_runtime_states = impact.EdgeHostPlcRuntimeStates
            }
        });
    }
}
