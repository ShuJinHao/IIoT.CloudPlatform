using System.Security.Claims;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class RolePolicyService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IIoTDbContext dbContext) : IRolePolicyService
{
    private static readonly TimeSpan ObservationTimeout = TimeSpan.FromSeconds(5);
    private readonly Func<IIoTDbContext> _createContext = dbContext.CreateFreshContext;

    public async Task<IList<string>> GetAllRolesAsync()
    {
        return await roleManager.Roles.Select(role => role.Name!).ToListAsync();
    }

    public Task<bool> RoleExistsAsync(string roleName)
    {
        return roleManager.RoleExistsAsync((roleName ?? string.Empty).Trim());
    }

    public async Task<Result<bool>> DefineRoleAsync(
        string roleName,
        List<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoleName = (roleName ?? string.Empty).Trim();
        if (SystemRoles.IsAdminLike(normalizedRoleName))
        {
            return Result.Failure("禁止定义、覆盖或修改 Admin 角色。");
        }

        var validation = CloudPermissionCatalog.NormalizeForTargetRole(
            normalizedRoleName,
            permissions);
        if (!validation.IsValid)
        {
            return Result.Failure(
                "权限集合包含未知权限或 RoleAdmin 不可分配权限，未执行任何修改。");
        }

        var target = new RoleWriteTarget(
            Guid.NewGuid(),
            normalizedRoleName,
            roleManager.NormalizeKey(normalizedRoleName)
                ?? normalizedRoleName.ToUpperInvariant(),
            Guid.NewGuid().ToString("N"),
            NormalizePermissions(validation.Permissions));
        var writeAttempted = false;

        return await ExecuteRecoverableAsync(
            async callbackToken =>
            {
                await using var context = _createContext();
                var current = await ReadRoleAsync(
                    context,
                    target.NormalizedName,
                    callbackToken);
                if (MatchesTarget(current, target))
                {
                    return Result.Success(true);
                }

                if (current is not null)
                {
                    if (writeAttempted)
                    {
                        throw new CloudWriteConflictException();
                    }

                    return Result.Failure(
                        "角色已存在，DefineRolePolicy 只允许创建新角色。");
                }

                await using var transaction =
                    await context.Database.BeginTransactionAsync(callbackToken);
                writeAttempted = true;
                context.Roles.Add(new IdentityRole<Guid>
                {
                    Id = target.Id,
                    Name = target.Name,
                    NormalizedName = target.NormalizedName,
                    ConcurrencyStamp = target.ConcurrencyStamp
                });
                context.RoleClaims.AddRange(target.Permissions.Select(permission =>
                    new IdentityRoleClaim<Guid>
                    {
                        RoleId = target.Id,
                        ClaimType = IIoTClaimTypes.Permission,
                        ClaimValue = permission
                    }));
                await context.SaveChangesAsync(callbackToken);
                await transaction.CommitAsync(callbackToken);
                return Result.Success(true);
            },
            token => ObserveRoleTargetAsync(target, baseline: null, token),
            cancellationToken);
    }

    public async Task<List<string>?> GetRolePermissionsAsync(string roleName)
    {
        var role = await roleManager.FindByNameAsync(
            (roleName ?? string.Empty).Trim());
        if (role is null)
        {
            return null;
        }

        var claims = await roleManager.GetClaimsAsync(role);
        return claims
            .Where(claim => claim.Type == IIoTClaimTypes.Permission)
            .Select(claim => claim.Value)
            .ToList();
    }

    public async Task<Result<bool>> UpdateRolePermissionsAsync(
        string roleName,
        List<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoleName = (roleName ?? string.Empty).Trim();
        if (SystemRoles.IsAdminLike(normalizedRoleName))
        {
            return Result.Failure("禁止定义、覆盖或修改 Admin 角色。");
        }

        var validation = CloudPermissionCatalog.NormalizeForTargetRole(
            normalizedRoleName,
            permissions);
        if (!validation.IsValid)
        {
            return Result.Failure(
                "权限集合包含未知权限或 RoleAdmin 不可分配权限，未执行任何修改。");
        }

        var normalizedName = roleManager.NormalizeKey(normalizedRoleName)
            ?? normalizedRoleName.ToUpperInvariant();
        var targetPermissions = NormalizePermissions(validation.Permissions);
        RoleWriteSnapshot? baseline = null;
        var targetConcurrencyStamp = Guid.NewGuid().ToString("N");
        Result<bool>? settledReadOnlyResult = null;

        return await ExecuteRecoverableAsync(
            async callbackToken =>
            {
                settledReadOnlyResult = null;
                await using var context = _createContext();
                await using var transaction =
                    await context.Database.BeginTransactionAsync(callbackToken);
                var current = await ReadRoleAsync(
                    context,
                    normalizedName,
                    callbackToken);
                if (current is null)
                {
                    Result<bool> result = Result.Failure("角色不存在");
                    settledReadOnlyResult = result;
                    await transaction.CommitAsync(callbackToken);
                    return result;
                }

                if (MatchesPermissionsTarget(
                        current,
                        targetConcurrencyStamp,
                        targetPermissions))
                {
                    await transaction.CommitAsync(callbackToken);
                    return Result.Success(true);
                }

                baseline ??= current;
                if (!MatchesSnapshot(current, baseline))
                {
                    throw new CloudWriteConflictException();
                }

                var claimed = await context.Roles
                    .Where(role =>
                        role.Id == baseline.Id &&
                        role.ConcurrencyStamp == baseline.ConcurrencyStamp)
                    .ExecuteUpdateAsync(
                        updates => updates.SetProperty(
                            role => role.ConcurrencyStamp,
                            targetConcurrencyStamp),
                        callbackToken);
                if (claimed != 1)
                {
                    throw new CloudWriteConflictException();
                }

                await context.RoleClaims
                    .Where(claim =>
                        claim.RoleId == baseline.Id &&
                        claim.ClaimType == IIoTClaimTypes.Permission)
                    .ExecuteDeleteAsync(callbackToken);
                context.RoleClaims.AddRange(targetPermissions.Select(permission =>
                    new IdentityRoleClaim<Guid>
                    {
                        RoleId = baseline.Id,
                        ClaimType = IIoTClaimTypes.Permission,
                        ClaimValue = permission
                    }));
                await context.SaveChangesAsync(callbackToken);
                await transaction.CommitAsync(callbackToken);
                return Result.Success(true);
            },
            token => ObserveRolePermissionsAsync(
                normalizedName,
                baseline,
                targetConcurrencyStamp,
                targetPermissions,
                token),
            cancellationToken,
            knownResult: () => settledReadOnlyResult);
    }

    public async Task<Result<bool>> UpdateUserPersonalPermissionsAsync(
        Guid userId,
        List<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var validation = CloudPermissionCatalog.Normalize(permissions);
        if (!validation.IsValid)
        {
            return Result.Failure(
                "权限集合包含未知或空白权限，未执行任何修改。");
        }

        var targetPermissions = NormalizePermissions(validation.Permissions);
        UserPermissionSnapshot? baseline = null;
        var targetConcurrencyStamp = Guid.NewGuid().ToString("N");
        Result<bool>? settledReadOnlyResult = null;

        return await ExecuteRecoverableAsync(
            async callbackToken =>
            {
                settledReadOnlyResult = null;
                await using var context = _createContext();
                await using var transaction =
                    await context.Database.BeginTransactionAsync(callbackToken);
                var current = await ReadUserPermissionsAsync(
                    context,
                    userId,
                    callbackToken);
                if (current is null)
                {
                    Result<bool> result = Result.Failure("用户不存在");
                    settledReadOnlyResult = result;
                    await transaction.CommitAsync(callbackToken);
                    return result;
                }

                if (MatchesPermissionsTarget(
                        current,
                        targetConcurrencyStamp,
                        targetPermissions))
                {
                    await transaction.CommitAsync(callbackToken);
                    return Result.Success(true);
                }

                baseline ??= current;
                if (!MatchesSnapshot(current, baseline))
                {
                    throw new CloudWriteConflictException();
                }

                var claimed = await context.Users
                    .Where(user =>
                        user.Id == baseline.Id &&
                        user.ConcurrencyStamp == baseline.ConcurrencyStamp)
                    .ExecuteUpdateAsync(
                        updates => updates.SetProperty(
                            user => user.ConcurrencyStamp,
                            targetConcurrencyStamp),
                        callbackToken);
                if (claimed != 1)
                {
                    throw new CloudWriteConflictException();
                }

                await context.UserClaims
                    .Where(claim =>
                        claim.UserId == baseline.Id &&
                        claim.ClaimType == IIoTClaimTypes.Permission)
                    .ExecuteDeleteAsync(callbackToken);
                context.UserClaims.AddRange(targetPermissions.Select(permission =>
                    new IdentityUserClaim<Guid>
                    {
                        UserId = baseline.Id,
                        ClaimType = IIoTClaimTypes.Permission,
                        ClaimValue = permission
                    }));
                await context.SaveChangesAsync(callbackToken);
                await transaction.CommitAsync(callbackToken);
                return Result.Success(true);
            },
            token => ObserveUserPermissionsAsync(
                userId,
                baseline,
                targetConcurrencyStamp,
                targetPermissions,
                token),
            cancellationToken,
            knownResult: () => settledReadOnlyResult);
    }

    public async Task<List<string>> GetUserPersonalPermissionsAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return [];
        }

        var claims = await userManager.GetClaimsAsync(user);
        return claims
            .Where(claim => claim.Type == IIoTClaimTypes.Permission)
            .Select(claim => claim.Value)
            .ToList();
    }

    private async Task<Result<bool>> ExecuteRecoverableAsync(
        Func<CancellationToken, Task<Result<bool>>> attempt,
        Func<CancellationToken, Task<WriteObservation>> observe,
        CancellationToken cancellationToken,
        Func<Result<bool>?>? knownResult = null)
    {
        try
        {
            await using var strategyContext = _createContext();
            var strategy = strategyContext.Database.CreateExecutionStrategy();
            var result = await strategy.ExecuteAsync(attempt, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CloudWriteConflictException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (knownResult?.Invoke() is { } settledResult)
            {
                return settledResult;
            }

            using var timeout = new CancellationTokenSource(ObservationTimeout);
            WriteObservation observation;
            try
            {
                observation = await observe(timeout.Token);
            }
            catch
            {
                observation = WriteObservation.Unknown;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return observation switch
            {
                WriteObservation.Target => Result.Success(true),
                WriteObservation.Conflict => throw new CloudWriteConflictException(),
                _ => throw new CloudWriteCommitUnknownException()
            };
        }
    }

    private async Task<WriteObservation> ObserveRoleTargetAsync(
        RoleWriteTarget target,
        RoleWriteSnapshot? baseline,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var current = await ReadRoleAsync(
            context,
            target.NormalizedName,
            cancellationToken);
        if (MatchesTarget(current, target))
        {
            return WriteObservation.Target;
        }

        return baseline is not null && MatchesSnapshot(current, baseline)
            ? WriteObservation.Baseline
            : current is null
                ? WriteObservation.Baseline
                : WriteObservation.Conflict;
    }

    private async Task<WriteObservation> ObserveRolePermissionsAsync(
        string normalizedName,
        RoleWriteSnapshot? baseline,
        string targetConcurrencyStamp,
        IReadOnlyList<string> targetPermissions,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var current = await ReadRoleAsync(
            context,
            normalizedName,
            cancellationToken);
        if (MatchesPermissionsTarget(
                current,
                targetConcurrencyStamp,
                targetPermissions))
        {
            return WriteObservation.Target;
        }

        if (baseline is null)
        {
            return WriteObservation.Unknown;
        }

        return MatchesSnapshot(current, baseline)
            ? WriteObservation.Baseline
            : WriteObservation.Conflict;
    }

    private async Task<WriteObservation> ObserveUserPermissionsAsync(
        Guid userId,
        UserPermissionSnapshot? baseline,
        string targetConcurrencyStamp,
        IReadOnlyList<string> targetPermissions,
        CancellationToken cancellationToken)
    {
        await using var context = _createContext();
        var current = await ReadUserPermissionsAsync(
            context,
            userId,
            cancellationToken);
        if (MatchesPermissionsTarget(
                current,
                targetConcurrencyStamp,
                targetPermissions))
        {
            return WriteObservation.Target;
        }

        if (baseline is null)
        {
            return WriteObservation.Unknown;
        }

        return MatchesSnapshot(current, baseline)
            ? WriteObservation.Baseline
            : WriteObservation.Conflict;
    }

    private static async Task<RoleWriteSnapshot?> ReadRoleAsync(
        IIoTDbContext context,
        string normalizedName,
        CancellationToken cancellationToken)
    {
        var role = await context.Roles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.NormalizedName == normalizedName,
                cancellationToken);
        if (role is null)
        {
            return null;
        }

        var permissions = await context.RoleClaims
            .AsNoTracking()
            .Where(claim =>
                claim.RoleId == role.Id &&
                claim.ClaimType == IIoTClaimTypes.Permission)
            .Select(claim => claim.ClaimValue!)
            .ToListAsync(cancellationToken);
        return new RoleWriteSnapshot(
            role.Id,
            role.Name,
            role.NormalizedName,
            role.ConcurrencyStamp,
            NormalizePermissions(permissions));
    }

    private static async Task<UserPermissionSnapshot?> ReadUserPermissionsAsync(
        IIoTDbContext context,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await context.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.ConcurrencyStamp
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        var permissions = await context.UserClaims
            .AsNoTracking()
            .Where(claim =>
                claim.UserId == userId &&
                claim.ClaimType == IIoTClaimTypes.Permission)
            .Select(claim => claim.ClaimValue!)
            .ToListAsync(cancellationToken);
        return new UserPermissionSnapshot(
            user.Id,
            user.ConcurrencyStamp,
            NormalizePermissions(permissions));
    }

    private static string[] NormalizePermissions(IEnumerable<string> permissions)
        => permissions
            .Select(permission => permission.Trim())
            .Where(permission => permission.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool MatchesTarget(
        RoleWriteSnapshot? snapshot,
        RoleWriteTarget target)
        => snapshot is not null &&
           snapshot.Id == target.Id &&
           string.Equals(snapshot.Name, target.Name, StringComparison.Ordinal) &&
           string.Equals(
               snapshot.NormalizedName,
               target.NormalizedName,
               StringComparison.Ordinal) &&
           string.Equals(
               snapshot.ConcurrencyStamp,
               target.ConcurrencyStamp,
               StringComparison.Ordinal) &&
           snapshot.Permissions.SequenceEqual(
               target.Permissions,
               StringComparer.OrdinalIgnoreCase);

    private static bool MatchesPermissionsTarget(
        RoleWriteSnapshot? snapshot,
        string targetConcurrencyStamp,
        IReadOnlyList<string> targetPermissions)
        => snapshot is not null &&
           string.Equals(
               snapshot.ConcurrencyStamp,
               targetConcurrencyStamp,
               StringComparison.Ordinal) &&
           snapshot.Permissions.SequenceEqual(
               targetPermissions,
               StringComparer.OrdinalIgnoreCase);

    private static bool MatchesPermissionsTarget(
        UserPermissionSnapshot? snapshot,
        string targetConcurrencyStamp,
        IReadOnlyList<string> targetPermissions)
        => snapshot is not null &&
           string.Equals(
               snapshot.ConcurrencyStamp,
               targetConcurrencyStamp,
               StringComparison.Ordinal) &&
           snapshot.Permissions.SequenceEqual(
               targetPermissions,
               StringComparer.OrdinalIgnoreCase);

    private static bool MatchesSnapshot(
        RoleWriteSnapshot? current,
        RoleWriteSnapshot baseline)
        => current is not null &&
           current.Id == baseline.Id &&
           string.Equals(current.Name, baseline.Name, StringComparison.Ordinal) &&
           string.Equals(
               current.NormalizedName,
               baseline.NormalizedName,
               StringComparison.Ordinal) &&
           string.Equals(
               current.ConcurrencyStamp,
               baseline.ConcurrencyStamp,
               StringComparison.Ordinal) &&
           current.Permissions.SequenceEqual(
               baseline.Permissions,
               StringComparer.OrdinalIgnoreCase);

    private static bool MatchesSnapshot(
        UserPermissionSnapshot? current,
        UserPermissionSnapshot baseline)
        => current is not null &&
           current.Id == baseline.Id &&
           string.Equals(
               current.ConcurrencyStamp,
               baseline.ConcurrencyStamp,
               StringComparison.Ordinal) &&
           current.Permissions.SequenceEqual(
               baseline.Permissions,
               StringComparer.OrdinalIgnoreCase);

    private sealed record RoleWriteTarget(
        Guid Id,
        string Name,
        string NormalizedName,
        string ConcurrencyStamp,
        IReadOnlyList<string> Permissions);

    private sealed record RoleWriteSnapshot(
        Guid Id,
        string? Name,
        string? NormalizedName,
        string? ConcurrencyStamp,
        IReadOnlyList<string> Permissions);

    private sealed record UserPermissionSnapshot(
        Guid Id,
        string? ConcurrencyStamp,
        IReadOnlyList<string> Permissions);

    private enum WriteObservation
    {
        Baseline,
        Target,
        Conflict,
        Unknown
    }
}
