using IIoT.Services.Common.Caching;
using IIoT.Services.Common.Caching.Options;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Contracts.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IIoT.EntityFrameworkCore.Identity;

/// <summary>
/// 设备访问范围服务。
/// 返回某个用户当前可操作的设备 Id 集合，供生产域用例做设备级 ABAC 校验使用。
/// Admin 返回 null，表示不受设备范围约束。
/// </summary>
public sealed class DevicePermissionService(
    IIoTDbContext dbContext,
    ICacheService cacheService,
    IOptions<PermissionCacheOptions> options)
    : IDevicePermissionService
{
    private readonly PermissionCacheOptions _options = options.Value;

    public async Task<IReadOnlyList<Guid>?> GetAccessibleDeviceIdsAsync(
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        if (isAdmin)
        {
            return null;
        }

        var cacheKey = CacheKeys.DeviceAccessesByUser(userId);
        return await cacheService.GetOrSetAsync(
            cacheKey,
            async token => await dbContext.Employees
                .Where(employee => employee.Id == userId)
                .SelectMany(employee => employee.DeviceAccesses.Select(deviceAccess => deviceAccess.DeviceId))
                .Distinct()
                .ToListAsync(token),
            _options.ResolveExpiration(),
            cancellationToken);
    }
}
