using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
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
    private const int AuditSummaryMaxLength = 512;

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

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
        catch (DeviceDeletionCommitAttemptException exception)
        {
            deletionResult = await ResolveCommitAsync(exception.Impact);
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
            deletionResult = await ResolveCommitAsync(null);
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
            await CloudWriteCommitRecovery.ConfirmRecoveredAuditAsync(
                auditTrailService,
                auditEntry);
        }
        else
        {
            await auditTrailService.TryWriteAsync(
                auditEntry,
                cancellationToken);
        }

        return Result.Success(true);

        async Task<DeviceCascadeDeletionResult> ResolveCommitAsync(
            DeviceDeletionImpact? attemptedImpact)
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
                    attemptedImpact ?? baseline.DeletionImpact);
            }

            throw new CloudWriteConflictException();
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
    {
        var deleted = new
        {
            recipes = impact.Recipes,
            capacities = impact.Capacities,
            logs = impact.DeviceLogs,
            passStations = impact.PassStations,
            clientStates = impact.ClientStates,
            clientVersions = impact.ClientVersionSnapshots,
            pluginVersions = impact.ClientPluginVersions,
            heartbeats = impact.RuntimeHeartbeats,
            uploads = impact.UploadReceiveRegistrations,
            access = impact.EmployeeDeviceAccesses,
            sessions = impact.RefreshTokenSessions,
            plcStates = impact.EdgeHostPlcRuntimeStates,
            pendingCredentials = impact.InstallerPendingCredentials,
            device_plugin_bindings = impact.DevicePluginBindings
        };
        var summary = JsonSerializer.Serialize(new
        {
            action = "DeviceCascadeDelete",
            device = new
            {
                name = device.DeviceName,
                clientCode = device.Code,
                processId = device.ProcessId
            },
            deleted
        }, AuditJsonOptions);
        if (summary.Length <= AuditSummaryMaxLength)
        {
            return summary;
        }

        // The immutable audit column is bounded. Preserve ClientCode and the
        // process identity while replacing only an unusually escape-heavy
        // display name with deterministic evidence.
        var deviceNameSha256 = Sha256Hex(device.DeviceName);
        var compactSummary = JsonSerializer.Serialize(new
        {
            device = new
            {
                nameSha256 = deviceNameSha256,
                clientCode = device.Code,
                processId = device.ProcessId
            },
            deleted
        }, AuditJsonOptions);
        if (compactSummary.Length <= AuditSummaryMaxLength)
        {
            return compactSummary;
        }

        var countEvidence = JsonSerializer.Serialize(
            deleted,
            AuditJsonOptions);
        return JsonSerializer.Serialize(new
        {
            action = "DeviceCascadeDelete",
            device = new
            {
                nameSha256 = deviceNameSha256,
                clientCodeSha256 = Sha256Hex(device.Code),
                processId = device.ProcessId
            },
            deleted = new
            {
                total = impact.TotalAssociatedRows,
                device_plugin_bindings = impact.DevicePluginBindings,
                countsSha256 = Sha256Hex(countEvidence)
            }
        }, AuditJsonOptions);
    }

    private static string Sha256Hex(string value)
        => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
