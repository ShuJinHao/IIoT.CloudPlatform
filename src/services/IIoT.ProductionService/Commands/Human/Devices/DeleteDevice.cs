using IIoT.Core.Employee.Aggregates.Employees;
using IIoT.Core.Employee.Specifications;
using IIoT.Core.Production.Aggregates.Devices;
using IIoT.Core.Production.Specifications.Devices;
using IIoT.Services.Common.Attributes;
using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.RecordQueries;
using IIoT.SharedKernel.Messaging;
using IIoT.SharedKernel.Repository;
using IIoT.SharedKernel.Result;

namespace IIoT.ProductionService.Commands.Devices;

[AuthorizeRequirement("Device.Delete")]
public record DeleteDeviceCommand(Guid DeviceId) : IHumanCommand<Result<bool>>;

public class DeleteDeviceHandler(
    ICurrentUser currentUser,
    IReadRepository<Employee> employeeRepository,
    IRepository<Device> deviceRepository,
    IDeviceDeletionDependencyQueryService dependencyQueryService,
    ICacheService cacheService
) : ICommandHandler<DeleteDeviceCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DeleteDeviceCommand request,
        CancellationToken cancellationToken)
    {
        var device = await deviceRepository.GetSingleOrDefaultAsync(
            new DeviceByIdSpec(request.DeviceId),
            cancellationToken);

        if (device is null)
            return Result.Failure("目标设备不存在");

        if (currentUser.Role != "Admin")
        {
            if (!Guid.TryParse(currentUser.Id, out var userId))
                return Result.Failure("用户凭证异常");

            var employee = await employeeRepository.GetSingleOrDefaultAsync(
                new EmployeeWithAccessesSpec(userId),
                cancellationToken);

            if (employee is null)
                return Result.Failure("系统中未找到您的员工档案");

            var hasDeviceAccess = employee.DeviceAccesses.Any(d => d.DeviceId == device.Id);
            if (!hasDeviceAccess)
                return Result.Failure("越权:您没有该设备的管辖权");
        }

        var dependencies = await dependencyQueryService.GetDependenciesAsync(
            request.DeviceId,
            cancellationToken);

        if (dependencies.HasAnyDependency)
        {
            var blockedBy = new List<string>();
            if (dependencies.HasRecipes) blockedBy.Add("配方");
            if (dependencies.HasCapacities) blockedBy.Add("产能历史");
            if (dependencies.HasDeviceLogs) blockedBy.Add("设备日志");
            if (dependencies.HasPassStations) blockedBy.Add("过站记录");

            return Result.Failure($"设备存在历史依赖，禁止硬删除: {string.Join("、", blockedBy)}");
        }

        deviceRepository.Delete(device);
        var affected = await deviceRepository.SaveChangesAsync(cancellationToken);

        if (affected > 0)
        {
            await cacheService.RemoveAsync(
                CacheKeys.DeviceInstance(device.Instance), cancellationToken);
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
        }

        return Result.Success(true);
    }
}
