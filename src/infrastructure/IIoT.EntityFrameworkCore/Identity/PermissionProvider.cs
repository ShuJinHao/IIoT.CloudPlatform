using IIoT.Services.Common.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IIoT.Infrastructure.EntityFrameworkCore.Identity;

/// <summary>
/// 动态角色权限提供者 (基于通用 CacheService 与 PostgreSQL)
/// </summary>
public class PermissionProvider(
    RoleManager<ApplicationUser> roleManager,
    ICacheService cacheService, // 🌟 核心：直接注入我们刚刚在 Common 定义的接口！
    IOptions<PermissionCacheOptions> options) : IPermissionProvider
{
    private readonly PermissionCacheOptions _options = options.Value;

    public async Task<IList<string>> GetPermissionsAsync(string roleName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(roleName)) return [];

        var cacheKey = $"{_options.KeyPrefix}{roleName}";

        // ==========================================
        // 🌟 读链路 第一步：通过 Common 接口尝试从缓存极速读取
        // ==========================================
        var cachedPermissions = await cacheService.GetAsync<List<string>>(cacheKey, cancellationToken);
        if (cachedPermissions != null)
        {
            return cachedPermissions; // 缓存命中！
        }

        // ==========================================
        // 🌟 读链路 第二步：缓存未命中，执行 DB 兜底 (Fallback)
        // ==========================================
        var role = await roleManager.FindByNameAsync(roleName);
        if (role == null) return [];

        var claims = await roleManager.GetClaimsAsync(role);
        var permissions = claims
            .Where(c => c.Type == "Permission")
            .Select(c => c.Value)
            .ToList();

        // ==========================================
        // 🌟 读链路 第三步：将 DB 数据回写到通用缓存，并设置过期时间
        // ==========================================
        var expireTime = TimeSpan.FromHours(_options.ExpirationHours);
        await cacheService.SetAsync(cacheKey, permissions, expireTime, cancellationToken);

        return permissions;
    }
}