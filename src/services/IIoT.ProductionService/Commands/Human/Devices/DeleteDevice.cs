using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.Authorization;
using IIoT.Services.Common.Contracts.RecordQueries;
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
    ICacheService cacheService,
    IDevicePermissionService devicePermissionService)
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
            return Result.Failure("设备不存在");

        if (!string.Equals(
                currentUser.Role,
                IIoT.Services.Common.Contracts.Authorization.SystemRoles.Admin,
                StringComparison.Ordinal))
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
                userId,
                isAdmin: false,
                cancellationToken);
            if (accessibleDeviceIds is null || !accessibleDeviceIds.Contains(device.Id))
            {
                return Result.Failure("越权:未授权访问该设备");
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
                blockedBy.Add("配方");
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
                blockedBy.Add("过站日志");
            }

            return Result.Failure($"设备存在历史依赖，无法删除: {string.Join("、", blockedBy)}");
        }

        await cacheService.RemoveAsync(
            CacheKeys.DeviceCode(device.Code), cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.DevicesByProcess(device.ProcessId), cancellationToken);
        await cacheService.RemoveAsync(CacheKeys.AllDevices(), cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.DeviceIdentity(device.Id), cancellationToken);
        await cacheService.RemoveAsync(
            CacheKeys.RecipesByDevice(device.Id), cancellationToken);
        await cacheService.RemoveByPatternAsync(
            CacheKeys.CapacityHourlyPattern(device.Id), cancellationToken);
        await cacheService.RemoveByPatternAsync(
            CacheKeys.CapacitySummaryPattern(device.Id), cancellationToken);
        await cacheService.RemoveByPatternAsync(
            CacheKeys.CapacityRangePattern(device.Id), cancellationToken);
        await cacheService.RemoveByPatternAsync(
            CacheKeys.CapacityPagedByDevicePattern(device.Id), cancellationToken);

        deviceRepository.Delete(device);
        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(affected > 0);
    }
}
