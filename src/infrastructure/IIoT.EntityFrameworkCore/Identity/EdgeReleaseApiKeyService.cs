using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class EdgeReleaseApiKeyService(IIoTDbContext dbContext) : IEdgeReleaseApiKeyService
{
    private const string KeyPrefix = "iiot_edge_release_";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] DefaultPermissions =
    [
        ClientReleasePermissions.Read,
        ClientReleasePermissions.Publish
    ];
    private static readonly HashSet<string> AllowedPermissions =
        new(DefaultPermissions, StringComparer.Ordinal);

    public async Task<Result<EdgeReleaseApiKeyCreateResult>> CreateAsync(
        string name,
        IReadOnlyCollection<string>? permissions,
        DateTimeOffset? expiresAtUtc,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        if (normalizedName is null)
        {
            return Result.Invalid("发布 API key 名称不能为空。");
        }

        var normalizedPermissions = NormalizePermissions(permissions);
        if (normalizedPermissions is null)
        {
            return Result.Invalid("发布 API key 只能授予 ClientRelease.Read 和 ClientRelease.Publish 权限。");
        }

        var resolvedExpiresAt = expiresAtUtc ?? DateTimeOffset.UtcNow.AddYears(1);
        if (resolvedExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return Result.Invalid("发布 API key 过期时间必须晚于当前时间至少 5 分钟。");
        }

        var exists = await dbContext.Set<EdgeReleaseApiKey>()
            .AnyAsync(x => x.Name == normalizedName, cancellationToken);
        if (exists)
        {
            return Result.Invalid($"发布 API key 名称已存在：{normalizedName}");
        }

        var apiKey = GenerateApiKey();
        var entity = new EdgeReleaseApiKey
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            KeyHash = ComputeHash(apiKey),
            Status = EdgeReleaseApiKeyStatuses.Active,
            PermissionsJson = JsonSerializer.Serialize(normalizedPermissions, JsonOptions),
            ExpiresAtUtc = resolvedExpiresAt,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = createdByUserId
        };

        dbContext.Set<EdgeReleaseApiKey>().Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new EdgeReleaseApiKeyCreateResult(
            entity.Id,
            entity.Name,
            apiKey,
            entity.ExpiresAtUtc,
            normalizedPermissions));
    }

    public async Task<IReadOnlyList<EdgeReleaseApiKeyListItem>> GetListAsync(
        CancellationToken cancellationToken = default)
    {
        var keys = await dbContext.Set<EdgeReleaseApiKey>()
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return keys.Select(ToListItem).ToList();
    }

    public async Task<Result> RevokeAsync(
        Guid id,
        Guid? revokedByUserId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var entity = await dbContext.Set<EdgeReleaseApiKey>()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
        {
            return Result.NotFound("发布 API key 不存在。");
        }

        if (entity.Status == EdgeReleaseApiKeyStatuses.Revoked)
        {
            return Result.Success();
        }

        entity.Status = EdgeReleaseApiKeyStatuses.Revoked;
        entity.RevokedAtUtc = DateTimeOffset.UtcNow;
        entity.RevokedByUserId = revokedByUserId;
        entity.RevokedReason = string.IsNullOrWhiteSpace(reason)
            ? "manual-revoke"
            : reason.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<EdgeReleaseApiKeyValidationResult>> ValidateAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Result.Unauthorized("发布 API key 不能为空。");
        }

        var hash = ComputeHash(apiKey.Trim());
        var entity = await dbContext.Set<EdgeReleaseApiKey>()
            .SingleOrDefaultAsync(x => x.KeyHash == hash, cancellationToken);

        if (entity is null ||
            entity.Status != EdgeReleaseApiKeyStatuses.Active ||
            entity.RevokedAtUtc.HasValue ||
            entity.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return Result.Unauthorized("发布 API key 无效、已吊销或已过期。");
        }

        entity.LastUsedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success(new EdgeReleaseApiKeyValidationResult(
            entity.Id,
            entity.Name,
            DeserializePermissions(entity.PermissionsJson)));
    }

    private static EdgeReleaseApiKeyListItem ToListItem(EdgeReleaseApiKey entity)
        => new(
            entity.Id,
            entity.Name,
            entity.Status,
            entity.ExpiresAtUtc,
            entity.LastUsedAtUtc,
            entity.CreatedAtUtc,
            entity.RevokedAtUtc,
            entity.RevokedReason,
            DeserializePermissions(entity.PermissionsJson));

    private static string? NormalizeName(string name)
    {
        var normalized = name.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static IReadOnlyList<string>? NormalizePermissions(IReadOnlyCollection<string>? permissions)
    {
        var resolved = (permissions is null || permissions.Count == 0
                ? DefaultPermissions
                : permissions)
            .Select(permission => permission.Trim())
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (resolved.Length == 0 || resolved.Any(permission => !AllowedPermissions.Contains(permission)))
        {
            return null;
        }

        return resolved;
    }

    private static IReadOnlyList<string> DeserializePermissions(string permissionsJson)
        => JsonSerializer.Deserialize<string[]>(permissionsJson, JsonOptions) ?? [];

    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"{KeyPrefix}{token}";
    }

    private static string ComputeHash(string apiKey)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
}
