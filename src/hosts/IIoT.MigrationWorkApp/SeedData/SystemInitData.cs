using IIoT.Core.Employees.Aggregates.Employees;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace IIoT.MigrationWorkApp.SeedData;

public static class SystemInitData
{
    internal const long AdminSeedAdvisoryLockKey = 0x49494F545F41444D;

    internal static SeedRetryTarget CreateRetryTarget()
    {
        var roleIds = SystemRolePermissionTemplates.Templates.Keys
            .Append(SystemRoles.Admin)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                roleName => roleName,
                _ => Guid.NewGuid(),
                StringComparer.Ordinal);
        return new SeedRetryTarget(Guid.NewGuid(), roleIds);
    }

    public static async Task SeedAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ValidateRolePermissionTemplates();
        var retryTarget = CreateRetryTarget();
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(
            callbackToken => SeedAttemptAsync(
                dbContext,
                userManager,
                roleManager,
                configuration,
                retryTarget,
                callbackToken),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal static async Task SeedAttemptAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IConfiguration configuration,
        SeedRetryTarget retryTarget,
        CancellationToken cancellationToken)
    {
        ValidateRolePermissionTemplates();
        dbContext.DiscardPendingDomainEvents();
        dbContext.ChangeTracker.Clear();
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await AcquireAdminSeedLockAsync(
                dbContext,
                cancellationToken);
            await SeedCoreAsync(
                dbContext,
                userManager,
                roleManager,
                configuration,
                retryTarget,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            Console.WriteLine("✅ 系统身份角色模板和管理员播种事务提交成功。");
        }
        catch
        {
            dbContext.DiscardPendingDomainEvents();
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private static async Task SeedCoreAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IConfiguration configuration,
        SeedRetryTarget retryTarget,
        CancellationToken cancellationToken)
    {
        var resetPasswordRequested = SeedAdminOptions.IsPasswordResetRequested(configuration);
        var adminAssignments = await ReadCanonicalAdminAssignmentsAsync(
            dbContext,
            cancellationToken);

        SeedAdminOptions? seedAdmin = null;
        ExistingAdminState? existingAdmin = null;
        string? targetPassword = null;

        if (adminAssignments.Count == 0)
        {
            seedAdmin = SeedAdminOptions.Load(configuration);
            await EnsureSeedTargetIsUnusedAsync(
                dbContext,
                userManager,
                seedAdmin.EmployeeNo,
                cancellationToken);
            targetPassword = seedAdmin.RequirePassword();
        }
        else
        {
            var existingAdmins = await RequireCompleteAdminsAsync(
                dbContext,
                adminAssignments,
                cancellationToken);

            if (resetPasswordRequested)
            {
                seedAdmin = SeedAdminOptions.Load(configuration);
                existingAdmin = EnsureResetTargetsExistingAdmin(
                    seedAdmin,
                    existingAdmins);
                targetPassword = seedAdmin.RequirePassword();
            }
            else
            {
                ThrowIfNoEnabledActiveAdmin(existingAdmins, "SeedLockedPreflight");
                existingAdmin = existingAdmins.First(admin =>
                    admin.IdentityEnabled && admin.EmployeeActive);
            }
        }

        var adminRole = await EnsureRoleAsync(
            roleManager,
            SystemRoles.Admin,
            retryTarget.RoleIds[SystemRoles.Admin],
            cancellationToken);
        await EnsureAdminDelegatedAiReadPermissionsAsync(
            roleManager,
            adminRole,
            cancellationToken);
        await EnsureRolePermissionTemplatesAsync(
            roleManager,
            retryTarget.RoleIds,
            cancellationToken);

        if (existingAdmin is null)
        {
            await CreateFirstAdminAsync(
                dbContext,
                userManager,
                seedAdmin!,
                targetPassword!,
                retryTarget.AdminAccountId,
                cancellationToken);
        }
        else if (resetPasswordRequested)
        {
            await RepairExistingAdminAsync(
                dbContext,
                userManager,
                existingAdmin,
                targetPassword!,
                cancellationToken);
        }
        else
        {
            Console.WriteLine(
                $"ℹ️ 检测到 {adminAssignments.Count} 个规范 Admin，且至少一个账号启用、员工在职；账号播种幂等跳过。");
        }

        await AssertAdminInvariantAsync(dbContext, cancellationToken);
    }

    internal static async Task EnsureAdminAssignmentPreflightAsync(
        IIoTDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var assignments = await ReadCanonicalAdminAssignmentsAsync(
            dbContext,
            cancellationToken);
        if (assignments.Count == 0)
        {
            return;
        }

        var states = await RequireCompleteAdminsAsync(
            dbContext,
            assignments,
            cancellationToken);

        if (SeedAdminOptions.IsPasswordResetRequested(configuration))
        {
            var seedAdmin = SeedAdminOptions.Load(configuration);
            _ = seedAdmin.RequirePassword();
            _ = EnsureResetTargetsExistingAdmin(seedAdmin, states);
            return;
        }

        ThrowIfNoEnabledActiveAdmin(states, "MigrationPreflight");
    }

    private static async Task AcquireAdminSeedLockAsync(
        IIoTDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "管理员播种锁必须在数据库事务内获取。");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({AdminSeedAdvisoryLockKey});",
            cancellationToken);
    }

    private static async Task<IReadOnlyList<CanonicalAdminAssignment>>
        ReadCanonicalAdminAssignmentsAsync(
            IIoTDbContext dbContext,
            CancellationToken cancellationToken)
    {
        return await (
                from userRole in dbContext.UserRoles.AsNoTracking()
                join role in dbContext.Roles.AsNoTracking()
                    on userRole.RoleId equals role.Id
                join user in dbContext.Users.AsNoTracking()
                    on userRole.UserId equals user.Id
                where role.Name == SystemRoles.Admin
                orderby user.Id
                select new CanonicalAdminAssignment(
                    user.Id,
                    user.UserName))
            .ToArrayAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<ExistingAdminState>> RequireCompleteAdminsAsync(
        IIoTDbContext dbContext,
        IReadOnlyList<CanonicalAdminAssignment> assignments,
        CancellationToken cancellationToken)
    {
        var states = new List<ExistingAdminState>(assignments.Count);
        foreach (var assignment in assignments)
        {
            states.Add(await RequireCompleteAdminAsync(
                dbContext,
                assignment,
                cancellationToken));
        }

        return states;
    }

    private static async Task<ExistingAdminState> RequireCompleteAdminAsync(
        IIoTDbContext dbContext,
        CanonicalAdminAssignment assignment,
        CancellationToken cancellationToken)
    {
        var identityUser = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(
                user => user.Id == assignment.AccountId,
                cancellationToken);
        var employee = await dbContext.Employees
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == assignment.AccountId,
                cancellationToken);

        if (identityUser is null)
        {
            throw AdminInvariantFailure(
                "AdminIdentityMissing",
                assignment.AccountId,
                assignment.EmployeeNo);
        }

        if (employee is null)
        {
            throw AdminInvariantFailure(
                "AdminEmployeeMissing",
                assignment.AccountId,
                identityUser.UserName);
        }

        if (string.IsNullOrWhiteSpace(identityUser.UserName)
            || !string.Equals(
                identityUser.UserName,
                employee.EmployeeNo,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Admin 身份预检失败：Identity 与 Employee 工号不一致。"
                + $" accountId={identityUser.Id}, "
                + $"identityEmployeeNo={JsonSerializer.Serialize(identityUser.UserName)}, "
                + $"employeeNo={JsonSerializer.Serialize(employee.EmployeeNo)}, "
                + "conflictType=AdminEmployeeNumberMismatch。"
                + " 未执行自动补造、迁移或档案改写。");
        }

        return new ExistingAdminState(
            identityUser.Id,
            identityUser.UserName,
            identityUser.IsEnabled,
            employee.IsActive);
    }

    private static InvalidOperationException AdminInvariantFailure(
        string conflictType,
        Guid accountId,
        string? employeeNo)
    {
        return new InvalidOperationException(
            "Admin 身份预检失败：管理员身份状态不完整。"
            + $" accountId={accountId}, "
            + $"employeeNo={JsonSerializer.Serialize(employeeNo)}, "
            + $"conflictType={conflictType}。"
            + " 未执行自动补造、迁移或账号改写。");
    }

    private static void ThrowIfNoEnabledActiveAdmin(
        IReadOnlyCollection<ExistingAdminState> existingAdmins,
        string conflictType)
    {
        if (existingAdmins.Any(admin => admin.IdentityEnabled && admin.EmployeeActive))
        {
            return;
        }

        var details = existingAdmins.Select(admin =>
            $"accountId={admin.AccountId}, "
            + $"employeeNo={JsonSerializer.Serialize(admin.EmployeeNo)}, "
            + $"identityEnabled={admin.IdentityEnabled}, "
            + $"employeeActive={admin.EmployeeActive}");
        throw new InvalidOperationException(
            "Admin 身份预检失败：至少需要一个账号启用且员工在职的规范 Admin。"
            + $" conflictType={conflictType}NoEnabledActiveAdmin。"
            + " 当前 Admin："
            + string.Join("; ", details)
            + "。"
            + $" 请显式设置 {SeedAdminOptions.ResetPasswordKey}=true 后再执行修复。");
    }

    private static ExistingAdminState EnsureResetTargetsExistingAdmin(
        SeedAdminOptions seedAdmin,
        IReadOnlyCollection<ExistingAdminState> existingAdmins)
    {
        var existingAdmin = existingAdmins.SingleOrDefault(admin =>
            string.Equals(
                seedAdmin.EmployeeNo,
                admin.EmployeeNo,
                StringComparison.Ordinal));
        if (existingAdmin is not null)
        {
            return existingAdmin;
        }

        throw new InvalidOperationException(
            "Admin 密码修复目标不匹配。"
            + $"requestedEmployeeNo={JsonSerializer.Serialize(seedAdmin.EmployeeNo)}, "
            + "conflictType=SeedAdminNumberMismatch。"
            + " 目标必须精确命中一个既有规范 Admin；未提升普通账号，也未修改其他管理员。");
    }

    private static async Task EnsureSeedTargetIsUnusedAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        string employeeNo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identityUser = await userManager.FindByNameAsync(employeeNo);
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedEmployeeNo = employeeNo.ToUpperInvariant();
        var employees = await dbContext.Employees
            .AsNoTracking()
            .Where(employee =>
                employee.EmployeeNo.ToUpper() == normalizedEmployeeNo)
            .Select(employee => new
            {
                employee.Id,
                employee.EmployeeNo
            })
            .OrderBy(employee => employee.Id)
            .ToArrayAsync(cancellationToken);

        if (identityUser is null && employees.Length == 0)
        {
            return;
        }

        var conflicts = new List<string>();
        if (identityUser is not null)
        {
            conflicts.Add(
                $"accountId={identityUser.Id}, "
                + $"employeeNo={JsonSerializer.Serialize(identityUser.UserName)}, "
                + "conflictType=TargetIdentityAlreadyExists");
        }

        conflicts.AddRange(employees.Select(employee =>
            $"accountId={employee.Id}, "
            + $"employeeNo={JsonSerializer.Serialize(employee.EmployeeNo)}, "
            + "conflictType=TargetEmployeeAlreadyExists"));

        throw new InvalidOperationException(
            "首次 Admin 播种目标与既有普通账号或员工冲突。"
            + " 未执行静默提权、账号创建或档案改写。冲突："
            + string.Join("; ", conflicts));
    }

    private static void ValidateRolePermissionTemplates()
    {
        var adminAiReadValidation = CloudPermissionCatalog.Normalize(
            SystemRolePermissionTemplates.AdminDelegatedAiReadPermissions);
        if (!adminAiReadValidation.IsValid ||
            adminAiReadValidation.Permissions.Count !=
            SystemRolePermissionTemplates.AdminDelegatedAiReadPermissions.Count ||
            adminAiReadValidation.Permissions.Any(permission =>
                !permission.StartsWith("AiRead.", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Admin 交互式 AiRead 权限模板包含未知、重复或非 AiRead 权限。");
        }

        foreach (var (roleName, permissions) in SystemRolePermissionTemplates.Templates)
        {
            if (string.IsNullOrWhiteSpace(roleName)
                || SystemRoles.IsAdminLike(roleName))
            {
                throw new InvalidOperationException(
                    $"内置角色模板名称非法：[{roleName}]。");
            }

            var validation = CloudPermissionCatalog.NormalizeForTargetRole(
                roleName,
                permissions);
            if (!validation.IsValid
                || validation.Permissions.Count != permissions.Count)
            {
                throw new InvalidOperationException(
                    $"内置角色模板 [{roleName}] 包含未知、重复或不可分配权限。");
            }

            if (string.Equals(
                    roleName,
                    SystemRoles.DeviceAdmin,
                    StringComparison.OrdinalIgnoreCase)
                && permissions.Any(permission =>
                    SystemRolePermissionTemplates.DeviceAdminRetiredPermissions.Contains(
                        permission,
                        StringComparer.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "DeviceAdmin 内置模板不得重新携带设备注册或删除权限。");
            }
        }
    }

    private static async Task EnsureAdminDelegatedAiReadPermissionsAsync(
        RoleManager<IdentityRole<Guid>> roleManager,
        IdentityRole<Guid> adminRole,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var claims = await roleManager.GetClaimsAsync(adminRole);
        cancellationToken.ThrowIfCancellationRequested();
        var existingPermissions = claims
            .Where(claim => claim.Type == IIoTClaimTypes.Permission)
            .Select(claim => claim.Value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var permission in SystemRolePermissionTemplates.AdminDelegatedAiReadPermissions)
        {
            if (existingPermissions.Contains(permission))
            {
                continue;
            }

            var addResult = await roleManager.AddClaimAsync(
                adminRole,
                new Claim(IIoTClaimTypes.Permission, permission));
            cancellationToken.ThrowIfCancellationRequested();
            if (!addResult.Succeeded)
            {
                Console.WriteLine($"❌ Admin 交互式 AiRead 权限 [{permission}] 播种失败！");
                foreach (var error in addResult.Errors)
                {
                    Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                }

                throw new Exception("Admin 交互式 AiRead 权限播种失败。");
            }
        }
    }

    private static async Task EnsureRolePermissionTemplatesAsync(
        RoleManager<IdentityRole<Guid>> roleManager,
        IReadOnlyDictionary<string, Guid> roleIds,
        CancellationToken cancellationToken)
    {
        foreach (var (roleName, permissions) in SystemRolePermissionTemplates.Templates)
        {
            var role = await EnsureRoleAsync(
                roleManager,
                roleName,
                roleIds[roleName],
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var claims = await roleManager.GetClaimsAsync(role);
            cancellationToken.ThrowIfCancellationRequested();
            var retiredClaims = SelectRetiredDeviceAdminPermissionClaims(
                roleName,
                claims);
            foreach (var retiredClaim in retiredClaims)
            {
                var removeResult = await roleManager.RemoveClaimAsync(role, retiredClaim);
                cancellationToken.ThrowIfCancellationRequested();
                if (!removeResult.Succeeded)
                {
                    Console.WriteLine(
                        $"❌ 角色 [{roleName}] 旧高危权限 [{retiredClaim.Value}] 清理失败！");
                    foreach (var error in removeResult.Errors)
                    {
                        Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                    }

                    throw new Exception($"角色 [{roleName}] 旧高危权限清理失败。");
                }
            }

            var existingPermissions = claims
                .Where(claim => claim.Type == IIoTClaimTypes.Permission)
                .Except(retiredClaims)
                .Select(claim => claim.Value.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var permission in permissions)
            {
                if (existingPermissions.Contains(permission))
                {
                    continue;
                }

                var addResult = await roleManager.AddClaimAsync(
                    role,
                    new Claim(IIoTClaimTypes.Permission, permission));
                cancellationToken.ThrowIfCancellationRequested();
                if (!addResult.Succeeded)
                {
                    Console.WriteLine($"❌ 角色 [{roleName}] 权限 [{permission}] 播种失败！");
                    foreach (var error in addResult.Errors)
                    {
                        Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                    }

                    throw new Exception($"角色 [{roleName}] 权限播种失败。");
                }
            }
        }
    }

    internal static IReadOnlyList<Claim> SelectRetiredDeviceAdminPermissionClaims(
        string roleName,
        IEnumerable<Claim> claims)
    {
        if (!string.Equals(
                roleName.Trim(),
                SystemRoles.DeviceAdmin,
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var retiredPermissions = SystemRolePermissionTemplates.DeviceAdminRetiredPermissions
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return claims
            .Where(claim =>
                claim.Type == IIoTClaimTypes.Permission
                && retiredPermissions.Contains(claim.Value.Trim()))
            .ToArray();
    }

    private static async Task<IdentityRole<Guid>> EnsureRoleAsync(
        RoleManager<IdentityRole<Guid>> roleManager,
        string roleName,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var role = await roleManager.FindByNameAsync(roleName);
        cancellationToken.ThrowIfCancellationRequested();
        if (role is not null)
        {
            return role;
        }

        role = new IdentityRole<Guid>(roleName)
        {
            Id = roleId
        };
        var createResult = await roleManager.CreateAsync(role);
        cancellationToken.ThrowIfCancellationRequested();
        if (!createResult.Succeeded)
        {
            Console.WriteLine($"❌ 角色 [{roleName}] 创建失败！");
            foreach (var error in createResult.Errors)
            {
                Console.WriteLine($"   - [{error.Code}]: {error.Description}");
            }

            throw new Exception($"角色 [{roleName}] 创建失败。");
        }

        Console.WriteLine($"✅ 角色 [{roleName}] 创建成功！");
        return role;
    }

    private static async Task CreateFirstAdminAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        SeedAdminOptions seedAdmin,
        string targetPassword,
        Guid targetAccountId,
        CancellationToken cancellationToken)
    {
        var identityUser = new ApplicationUser
        {
            Id = targetAccountId,
            UserName = seedAdmin.EmployeeNo,
            IsEnabled = true
        };

        cancellationToken.ThrowIfCancellationRequested();
        var createResult = await userManager.CreateAsync(identityUser, targetPassword);
        cancellationToken.ThrowIfCancellationRequested();
        if (!createResult.Succeeded)
        {
            Console.WriteLine($"❌ 账号 [{seedAdmin.EmployeeNo}] 创建失败！");
            foreach (var error in createResult.Errors)
            {
                Console.WriteLine($"   - [{error.Code}]: {error.Description}");
            }

            throw new Exception("Identity 账号创建失败，事务终止！");
        }

        var addRoleResult = await userManager.AddToRoleAsync(
            identityUser,
            SystemRoles.Admin);
        cancellationToken.ThrowIfCancellationRequested();
        if (!addRoleResult.Succeeded)
        {
            Console.WriteLine($"❌ 账号 [{seedAdmin.EmployeeNo}] 授予 Admin 角色失败！");
            foreach (var error in addRoleResult.Errors)
            {
                Console.WriteLine($"   - [{error.Code}]: {error.Description}");
            }

            throw new Exception("Admin 角色授予失败，事务终止！");
        }

        dbContext.Employees.Add(
            new Employee(identityUser.Id, seedAdmin.EmployeeNo, seedAdmin.RealName));
        await dbContext.SaveChangesAsync(cancellationToken);

        Console.WriteLine(
            $"✅ 账号 [{seedAdmin.EmployeeNo}] 及同 ID 员工业务数据已准备首次播种。");
    }

    private static async Task RepairExistingAdminAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        ExistingAdminState existingAdmin,
        string targetPassword,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identityUser = await userManager.FindByIdAsync(
            existingAdmin.AccountId.ToString());
        cancellationToken.ThrowIfCancellationRequested();
        if (identityUser is null)
        {
            throw AdminInvariantFailure(
                "AdminIdentityMissingDuringReset",
                existingAdmin.AccountId,
                existingAdmin.EmployeeNo);
        }

        var employee = await dbContext.Employees.SingleOrDefaultAsync(
            candidate => candidate.Id == existingAdmin.AccountId,
            cancellationToken);
        if (employee is null)
        {
            throw AdminInvariantFailure(
                "AdminEmployeeMissingDuringReset",
                existingAdmin.AccountId,
                existingAdmin.EmployeeNo);
        }

        if (!string.Equals(
                identityUser.UserName,
                existingAdmin.EmployeeNo,
                StringComparison.Ordinal)
            || !string.Equals(
                employee.EmployeeNo,
                existingAdmin.EmployeeNo,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Admin 密码修复期间检测到身份漂移。"
                + $" accountId={existingAdmin.AccountId}, "
                + $"identityEmployeeNo={JsonSerializer.Serialize(identityUser.UserName)}, "
                + $"employeeNo={JsonSerializer.Serialize(employee.EmployeeNo)}, "
                + "conflictType=AdminIdentityChangedDuringReset。");
        }

        if (!identityUser.IsEnabled)
        {
            identityUser.IsEnabled = true;
            var updateResult = await userManager.UpdateAsync(identityUser);
            cancellationToken.ThrowIfCancellationRequested();
            if (!updateResult.Succeeded)
            {
                Console.WriteLine($"❌ 账号 [{existingAdmin.EmployeeNo}] 启用失败！");
                foreach (var error in updateResult.Errors)
                {
                    Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                }

                throw new Exception("Identity 账号启用失败，事务终止！");
            }
        }

        await ResetPasswordAsync(
            userManager,
            identityUser,
            targetPassword,
            existingAdmin.EmployeeNo,
            cancellationToken);

        employee.Activate();
        await dbContext.SaveChangesAsync(cancellationToken);
        Console.WriteLine(
            $"✅ 管理员账号 [{existingAdmin.EmployeeNo}] 已准备按显式运维开关精确修复；原员工 ID 与姓名保持不变。");
    }

    private static async Task AssertAdminInvariantAsync(
        IIoTDbContext dbContext,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var assignments = await ReadCanonicalAdminAssignmentsAsync(
            dbContext,
            cancellationToken);
        if (assignments.Count < 1)
        {
            throw new InvalidOperationException(
                "管理员播种提交前复核失败：规范 Admin 数量必须至少为 1。"
                + " conflictType=FinalAdminCountInvalid。");
        }

        var states = await RequireCompleteAdminsAsync(
            dbContext,
            assignments,
            cancellationToken);
        ThrowIfNoEnabledActiveAdmin(states, "FinalAdminInvariant");
    }

    private static async Task ResetPasswordAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser identityUser,
        string targetPassword,
        string employeeNo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await userManager.CheckPasswordAsync(identityUser, targetPassword))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine(
                $"ℹ️ 账号 [{employeeNo}] 已符合目标密码，密码修复幂等跳过。");
            return;
        }

        if (!string.IsNullOrWhiteSpace(identityUser.PasswordHash))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var removePasswordResult = await userManager.RemovePasswordAsync(identityUser);
            cancellationToken.ThrowIfCancellationRequested();
            if (!removePasswordResult.Succeeded)
            {
                Console.WriteLine($"❌ 账号 [{employeeNo}] 移除旧密码失败！");
                foreach (var error in removePasswordResult.Errors)
                {
                    Console.WriteLine($"   - [{error.Code}]: {error.Description}");
                }

                throw new Exception("Identity 旧密码移除失败，事务终止！");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var addPasswordResult = await userManager.AddPasswordAsync(identityUser, targetPassword);
        cancellationToken.ThrowIfCancellationRequested();
        if (!addPasswordResult.Succeeded)
        {
            Console.WriteLine($"❌ 账号 [{employeeNo}] 设置新密码失败！");
            foreach (var error in addPasswordResult.Errors)
            {
                Console.WriteLine($"   - [{error.Code}]: {error.Description}");
            }

            throw new Exception("Identity 新密码设置失败，事务终止！");
        }
    }

    private sealed record CanonicalAdminAssignment(
        Guid AccountId,
        string? EmployeeNo);

    private sealed record ExistingAdminState(
        Guid AccountId,
        string EmployeeNo,
        bool IdentityEnabled,
        bool EmployeeActive);

    internal sealed record SeedRetryTarget(
        Guid AdminAccountId,
        IReadOnlyDictionary<string, Guid> RoleIds);
}
