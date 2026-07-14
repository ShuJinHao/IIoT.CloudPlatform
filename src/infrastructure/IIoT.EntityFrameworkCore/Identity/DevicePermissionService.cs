using IIoT.Services.Contracts.Authorization;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Identity;

/// <summary>
/// 设备访问范围服务。
/// 返回某个普通用户当前可操作的设备 Id 集合，供生产域用例做设备级 ABAC 校验使用。
/// </summary>
public sealed class DevicePermissionService(
    IIoTDbContext dbContext)
    : IDevicePermissionService
{
    public Task<IReadOnlyList<Guid>> GetAccessibleDeviceIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return ReadAccessibleDeviceIdsAsync(userId, cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> ReadAccessibleDeviceIdsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Employees
            .Where(employee => employee.Id == userId)
            .SelectMany(employee => employee.DeviceAccesses.Select(deviceAccess => deviceAccess.DeviceId))
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
