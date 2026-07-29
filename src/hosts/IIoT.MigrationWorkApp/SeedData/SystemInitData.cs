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
    internal const long SingleAdminSeedAdvisoryLockKey = 0x49494F545F41444D;

    public static async Task SeedAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ValidateRolePermissionTemplates();

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.DiscardPendingDomainEvents();
            dbContext.ChangeTracker.Clear();
            await using var transaction =
                await dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await AcquireSingleAdminSeedLockAsync(
                    dbContext,
                    cancellationToken);
                await SeedCoreAsync(
                    dbContext,
                    userManager,
                    roleManager,
                    configuration,
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
        });
    }

    private static async Task SeedCoreAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var resetPasswordRequested = SeedAdminOptions.IsPasswordResetRequested(configuration);
        var adminAssignments = await ReadCanonicalAdminAssignmentsAsync(
            dbContext,
            cancellationToken);
        ThrowIfMultipleAdmins(adminAssignments, "SeedLockedPreflight");

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
            existingAdmin = await RequireCompleteAdminAsync(
                dbContext,
                adminAssignments[0],
                cancellationToken);

            if (!resetPasswordRequested)
            {
                ThrowIfAdminDisabled(existingAdmin);
            }
            else
            {
                seedAdmin = SeedAdminOptions.Load(configuration);
                EnsureResetTargetsExistingAdmin(seedAdmin, existingAdmin);
                targetPassword = seedAdmin.RequirePassword();
            }
        }

        await EnsureRoleAsync(roleManager, SystemRoles.Admin);
        await EnsureRolePermissionTemplatesAsync(roleManager);

        if (existingAdmin is null)
        {
            await CreateFirstAdminAsync(
                dbContext,
                userManager,
                seedAdmin!,
                targetPassword!,
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
                $"ℹ️ 检测到唯一且完整启用的管理员账号 [{existingAdmin.EmployeeNo}]，账号播种幂等跳过。");
        }

        await AssertSingleAdminInvariantAsync(dbContext, cancellationToken);
    }

    internal static async Task EnsureSingleAdminAssignmentPreflightAsync(
        IIoTDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var assignments = await ReadCanonicalAdminAssignmentsAsync(
            dbContext,
            cancellationToken);
        ThrowIfMultipleAdmins(assignments, "MigrationPreflight");
    }

    private static async Task AcquireSingleAdminSeedLockAsync(
        IIoTDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "管理员播种锁必须在数据库事务内获取。");
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({SingleAdminSeedAdvisoryLockKey});",
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

    private static void ThrowIfMultipleAdmins(
        IReadOnlyList<CanonicalAdminAssignment> assignments,
        string conflictType)
    {
        if (assignments.Count <= 1)
        {
            return;
        }

        var details = assignments.Select(assignment =>
            $"accountId={assignment.AccountId}, "
            + $"employeeNo={JsonSerializer.Serialize(assignment.EmployeeNo)}, "
            + $"conflictType={conflictType}");
        throw new InvalidOperationException(
            "唯一 Admin 身份预检失败：检测到多个规范 Admin 账号。"
            + " 未执行自动删除、降级、合并或账号改写。冲突账号："
            + string.Join("; ", details));
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
                "唯一 Admin 身份预检失败：Identity 与 Employee 工号不一致。"
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
            "唯一 Admin 身份预检失败：管理员身份状态不完整。"
            + $" accountId={accountId}, "
            + $"employeeNo={JsonSerializer.Serialize(employeeNo)}, "
            + $"conflictType={conflictType}。"
            + " 未执行自动补造、迁移或账号改写。");
    }

    private static void ThrowIfAdminDisabled(ExistingAdminState existingAdmin)
    {
        if (existingAdmin.IdentityEnabled && existingAdmin.EmployeeActive)
        {
            return;
        }

        throw new InvalidOperationException(
            "唯一 Admin 身份预检失败：管理员账号或员工档案已停用。"
            + $" accountId={existingAdmin.AccountId}, "
            + $"employeeNo={JsonSerializer.Serialize(existingAdmin.EmployeeNo)}, "
            + "conflictType=AdminDisabledResetRequired。"
            + $" 请显式设置 {SeedAdminOptions.ResetPasswordKey}=true 后再执行修复。");
    }

    private static void EnsureResetTargetsExistingAdmin(
        SeedAdminOptions seedAdmin,
        ExistingAdminState existingAdmin)
    {
        if (string.Equals(
            seedAdmin.EmployeeNo,
            existingAdmin.EmployeeNo,
            StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException(
            "唯一 Admin 密码修复目标不匹配。"
            + $" accountId={existingAdmin.AccountId}, "
            + $"employeeNo={JsonSerializer.Serialize(existingAdmin.EmployeeNo)}, "
            + $"requestedEmployeeNo={JsonSerializer.Serialize(seedAdmin.EmployeeNo)}, "
            + "conflictType=SeedAdminNumberMismatch。"
            + " 未创建第二个账号，也未修改现有管理员。");
    }

    private static async Task EnsureSeedTargetIsUnusedAsync(
        IIoTDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        string employeeNo,
        CancellationToken cancellationToken)
    {
        var identityUser = await userManager.FindByNameAsync(employeeNo);
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

    private static async Task EnsureRolePermissionTemplatesAsync(
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        foreach (var (roleName, permissions) in SystemRolePermissionTemplates.Templates)
        {
            var role = await EnsureRoleAsync(roleManager, roleName);
            var claims = await roleManager.GetClaimsAsync(role);
            var retiredClaims = SelectRetiredDeviceAdminPermissionClaims(
                roleName,
                claims);
            foreach (var retiredClaim in retiredClaims)
            {
                var removeResult = await roleManager.RemoveClaimAsync(role, retiredClaim);
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
        string roleName)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is not null)
        {
            return role;
        }

        role = new IdentityRole<Guid>(roleName);
        var createResult = await roleManager.CreateAsync(role);
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
        CancellationToken cancellationToken)
    {
        var identityUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = seedAdmin.EmployeeNo,
            IsEnabled = true
        };

        var createResult = await userManager.CreateAsync(identityUser, targetPassword);
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
        var identityUser = await userManager.FindByIdAsync(
            existingAdmin.AccountId.ToString());
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
                "唯一 Admin 密码修复期间检测到身份漂移。"
                + $" accountId={existingAdmin.AccountId}, "
                + $"identityEmployeeNo={JsonSerializer.Serialize(identityUser.UserName)}, "
                + $"employeeNo={JsonSerializer.Serialize(employee.EmployeeNo)}, "
                + "conflictType=AdminIdentityChangedDuringReset。");
        }

        if (!identityUser.IsEnabled)
        {
            identityUser.IsEnabled = true;
            var updateResult = await userManager.UpdateAsync(identityUser);
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
            existingAdmin.EmployeeNo);

        employee.Activate();
        await dbContext.SaveChangesAsync(cancellationToken);
        Console.WriteLine(
            $"✅ 唯一管理员账号 [{existingAdmin.EmployeeNo}] 已准备按显式运维开关修复；原员工 ID 与姓名保持不变。");
    }

    private static async Task AssertSingleAdminInvariantAsync(
        IIoTDbContext dbContext,
        CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var assignments = await ReadCanonicalAdminAssignmentsAsync(
            dbContext,
            cancellationToken);
        if (assignments.Count != 1)
        {
            var details = assignments.Select(assignment =>
                $"accountId={assignment.AccountId}, "
                + $"employeeNo={JsonSerializer.Serialize(assignment.EmployeeNo)}, "
                + "conflictType=FinalAdminCountInvalid");
            throw new InvalidOperationException(
                "管理员播种提交前复核失败：规范 Admin 数量必须恰好为 1。"
                + " 冲突账号："
                + string.Join("; ", details));
        }

        var state = await RequireCompleteAdminAsync(
            dbContext,
            assignments[0],
            cancellationToken);
        if (!state.IdentityEnabled || !state.EmployeeActive)
        {
            throw new InvalidOperationException(
                "管理员播种提交前复核失败：唯一 Admin 必须保持启用且在职。"
                + $" accountId={state.AccountId}, "
                + $"employeeNo={JsonSerializer.Serialize(state.EmployeeNo)}, "
                + "conflictType=FinalAdminDisabled。");
        }
    }

    private static async Task ResetPasswordAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser identityUser,
        string targetPassword,
        string employeeNo)
    {
        if (!string.IsNullOrWhiteSpace(identityUser.PasswordHash))
        {
            var removePasswordResult = await userManager.RemovePasswordAsync(identityUser);
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

        var addPasswordResult = await userManager.AddPasswordAsync(identityUser, targetPassword);
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
}
