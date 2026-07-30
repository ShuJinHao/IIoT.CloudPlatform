using System.Text.Json;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Persistence;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Persistence;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

[AuthorizeRequirement(DevicePermissions.Delete)]
[AuthorizeRequirement(DevicePermissions.CascadeDelete)]
[AdminOnly]
[DistributedLock("iiot:lock:device-write:{DeviceId}", TimeoutSeconds = 5)]
public record DeleteDeviceCommand(Guid DeviceId)
    : IHumanCommand<Result<bool>>, IAdminOnlyAuditRequest
{
    public string AdminAuditOperationType => "Device.Delete";
    public string AdminAuditTargetType => "Device";
    public string AdminAuditTargetIdOrKey => DeviceId.ToString();
}

public class DeleteDeviceHandler(
    ICurrentUser currentUser,
    IRepository<Device> deviceRepository,
    IDeviceDeletionDependencyQueryService dependencyQueryService,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    IAuditTrailService auditTrailService,
    IDeviceWriteObservationReader observationReader)
    : ICommandHandler<DeleteDeviceCommand, Result<bool>>
{
    private static readonly TimeSpan RecoveredCommitAuditTimeout =
        TimeSpan.FromSeconds(5);

    public async Task<Result<bool>> Handle(
        DeleteDeviceCommand request,
        CancellationToken cancellationToken)
    {
        var accessTarget = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(request.DeviceId),
            cancellationToken);
        if (accessTarget is null)
            return await FailAsync(
                request.DeviceId.ToString(),
                "目标设备不存在",
                cancellationToken);

        var deviceAccess =
            await currentUserDeviceAccessService.EnsureCanAccessDeviceAsync(
                accessTarget.Id,
                cancellationToken);
        if (!deviceAccess.IsSuccess)
        {
            return await FailAsync(
                accessTarget.Id.ToString(),
                deviceAccess.Errors?.FirstOrDefault()
                ?? "越权：未授权访问该设备",
                cancellationToken);
        }

        var baseline = await CloudWriteCommitRecovery.TryObserveAttemptAsync(
            token => observationReader.ObserveDeviceAsync(
                accessTarget.Id,
                accessTarget.DeviceName,
                accessTarget.Code,
                accessTarget.ProcessId,
                token),
            cancellationToken);
        if (baseline?.Target is null)
        {
            throw new CloudWriteCommitUnknownException();
        }

        var auditExecutedAtUtc = DateTime.UtcNow;
        DeviceCascadeDeletionResult deletionResult;
        var commitRecovered = false;
        try
        {
            deletionResult = await dependencyQueryService.DeleteCascadeAsync(
                request.DeviceId,
                cancellationToken,
                baseline.Target.RowVersion);
        }
        catch (DeviceDeletionCommitAttemptException)
        {
            deletionResult = await ResolveCommitAsync();
            commitRecovered = true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CloudWriteException)
        {
            throw;
        }
        catch
        {
            deletionResult = await ResolveCommitAsync();
            commitRecovered = true;
        }

        if (!deletionResult.DeviceDeleted)
        {
            throw new CloudWriteCommitUnknownException();
        }

        var auditEntry = new AuditTrailEntry(
            ParseActorUserId(currentUser.Id),
            currentUser.UserName,
            "Device.Delete",
            "Device",
            accessTarget.Id.ToString(),
            auditExecutedAtUtc,
            true,
            BuildDeletionAuditSummary(
                accessTarget,
                deletionResult.Impact),
            IdempotencyKey: $"device-delete:{request.DeviceId:N}");
        if (commitRecovered)
        {
            await WriteRecoveredCommitAuditAsync(auditEntry);
        }
        else
        {
            await auditTrailService.TryWriteAsync(
                auditEntry,
                cancellationToken);
        }

        return Result.Success(true);

        async Task<DeviceCascadeDeletionResult> ResolveCommitAsync()
        {
            var current = await CloudWriteCommitRecovery.TryObserveCommitAsync(
                token => observationReader.ObserveDeviceAsync(
                    accessTarget.Id,
                    accessTarget.DeviceName,
                    accessTarget.Code,
                    accessTarget.ProcessId,
                    token));
            if (current is null
                || current.Target == baseline.Target)
            {
                throw new CloudWriteCommitUnknownException();
            }

            if (current.Target is null
                && current.DeletionImpact.TotalAssociatedRows == 0)
            {
                return new DeviceCascadeDeletionResult(
                    true,
                    baseline.DeletionImpact);
            }

            throw new CloudWriteConflictException();
        }

        async Task WriteRecoveredCommitAuditAsync(
            AuditTrailEntry auditEntry)
        {
            using var timeout =
                new CancellationTokenSource(RecoveredCommitAuditTimeout);
            try
            {
                if (!await auditTrailService.TryWriteConfirmedAsync(
                        auditEntry,
                        timeout.Token))
                {
                    throw new CloudWriteCommitUnknownException();
                }
            }
            catch (OperationCanceledException)
                when (timeout.IsCancellationRequested)
            {
                throw new CloudWriteCommitUnknownException();
            }
        }
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
        => Guid.TryParse(rawUserId, out var actorUserId)
            ? actorUserId
            : null;

    private static string BuildDeletionAuditSummary(
        Device device,
        DeviceDeletionImpact impact)
        => JsonSerializer.Serialize(new
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
                edge_device_client_version_snapshots =
                    impact.ClientVersionSnapshots,
                edge_device_client_plugin_versions =
                    impact.ClientPluginVersions,
                edge_device_runtime_heartbeats = impact.RuntimeHeartbeats,
                upload_receive_registrations =
                    impact.UploadReceiveRegistrations,
                employee_device_accesses =
                    impact.EmployeeDeviceAccesses,
                refresh_token_sessions = impact.RefreshTokenSessions,
                edge_host_plc_runtime_states =
                    impact.EdgeHostPlcRuntimeStates
            }
        });
}
