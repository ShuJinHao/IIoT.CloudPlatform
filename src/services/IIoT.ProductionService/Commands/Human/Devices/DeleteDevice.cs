using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Caching;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.Services.CrossCutting.Attributes;
using IIoT.Services.CrossCutting.Caching;
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
    IRefreshTokenService refreshTokenService,
    ICurrentUserDeviceAccessService currentUserDeviceAccessService,
    ICacheService cacheService,
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

        var dependencies = await dependencyQueryService.GetDependenciesAsync(
            request.DeviceId,
            cancellationToken);

        if (dependencies.HasAnyDependency)
        {
            var blockedBy = new List<string>();
            if (dependencies.HasRecipes)
            {
                blockedBy.Add("配方数据");
            }
            if (dependencies.HasCapacities)
            {
                blockedBy.Add("产能记录");
            }
            if (dependencies.HasDeviceLogs)
            {
                blockedBy.Add("设备日志");
            }
            if (dependencies.HasPassStations)
            {
                blockedBy.Add("过站数据");
            }

            return await FailAsync(
                device.Id.ToString(),
                $"设备存在历史数据依赖，禁止删除：{string.Join("、", blockedBy)}",
                cancellationToken);
        }

        device.MarkDeleted();
        deviceRepository.Delete(device);
        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await refreshTokenService.RevokeSubjectTokensAsync(
                IIoTClaimTypes.EdgeDeviceActor,
                device.Id,
                "device-deleted",
                cancellationToken);

            await cacheService.RemoveAsync(
                CacheKeys.DeviceCode(device.Code),
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
                $"删除设备 {device.DeviceName}（{device.Code}）。",
                affected > 0 ? null : "保存设备删除记录失败。"),
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
}
