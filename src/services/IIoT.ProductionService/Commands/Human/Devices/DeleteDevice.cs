using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Caching;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

[AuthorizeRequirement("Device.Delete")]
public record DeleteDeviceCommand(Guid DeviceId) : IHumanCommand<Result<bool>>;

public class DeleteDeviceHandler(
    ICurrentUser currentUser,
    IRepository<Device> deviceRepository,
    IDeviceDeletionDependencyQueryService dependencyQueryService,
    IDeviceCacheInvalidationService cacheInvalidationService,
    IRefreshTokenService refreshTokenService,
    IDevicePermissionService devicePermissionService,
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
            return await FailAsync(request.DeviceId.ToString(), "Device was not found.", cancellationToken);

        if (!string.Equals(currentUser.Role, SystemRoles.Admin, StringComparison.Ordinal))
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return await FailAsync(device.Id.ToString(), "Current user identity is invalid.", cancellationToken);

            var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
                userId,
                isAdmin: false,
                cancellationToken);
            if (accessibleDeviceIds is null || !accessibleDeviceIds.Contains(device.Id))
            {
                return await FailAsync(device.Id.ToString(), "Unauthorized device access.", cancellationToken);
            }
        }

        var dependencies = await dependencyQueryService.GetDependenciesAsync(
            request.DeviceId,
            cancellationToken);

        if (dependencies.HasAnyDependency)
        {
            var blockedBy = new List<string>();
            if (dependencies.HasRecipes)
            {
                blockedBy.Add("recipes");
            }
            if (dependencies.HasCapacities)
            {
                blockedBy.Add("capacities");
            }
            if (dependencies.HasDeviceLogs)
            {
                blockedBy.Add("device-logs");
            }
            if (dependencies.HasPassStations)
            {
                blockedBy.Add("pass-stations");
            }

            return await FailAsync(
                device.Id.ToString(),
                $"Device cannot be deleted because dependencies exist: {string.Join(", ", blockedBy)}",
                cancellationToken);
        }

        device.MarkDeleted();
        deviceRepository.Delete(device);
        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheInvalidationService.InvalidateAfterDeleteAsync(
                new DeviceCacheDescriptor(device.Id, device.ProcessId, device.Code),
                cancellationToken);
            await refreshTokenService.RevokeSubjectTokensAsync(
                IIoTClaimTypes.EdgeDeviceActor,
                device.Id,
                "device-deleted",
                cancellationToken);
        }

        await auditTrailService.TryWriteAsync(
            new AuditTrailEntry(
                ParseActorUserId(currentUser.Id),
                currentUser.UserName,
                "Device.Delete",
                "Device",
                device.Id.ToString(),
                DateTime.UtcNow,
                affected > 0,
                $"Deleted device {device.DeviceName} ({device.Code}).",
                affected > 0 ? null : "SaveChangesAsync did not persist any rows."),
            cancellationToken);

        return Result.Success(affected > 0);
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
                $"Delete device {targetIdOrKey}.",
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
}
