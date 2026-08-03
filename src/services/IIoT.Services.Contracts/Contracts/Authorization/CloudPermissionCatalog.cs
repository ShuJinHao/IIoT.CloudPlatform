namespace IIoT.Services.Contracts.Authorization;

/// <summary>
/// Cloud 权限的唯一代码级目录。
/// 所有外部输入必须先通过本目录规范化，禁止把任意字符串写入身份声明。
/// </summary>
public static class CloudPermissionCatalog
{
    public static class Employee
    {
        public const string Read = "Employee.Read";
        public const string Onboard = "Employee.Onboard";
        public const string Update = "Employee.Update";
        public const string UpdateAccess = "Employee.UpdateAccess";
        public const string Deactivate = "Employee.Deactivate";
        public const string Terminate = "Employee.Terminate";
    }

    public static class Process
    {
        public const string Read = "Process.Read";
        public const string Create = "Process.Create";
        public const string Update = "Process.Update";
        public const string Delete = "Process.Delete";
    }

    public static class Device
    {
        public const string Read = "Device.Read";
        public const string Create = "Device.Create";
        public const string Update = "Device.Update";
        public const string MigrateProcess = "Device.MigrateProcess";
        public const string Delete = "Device.Delete";
        public const string CascadeDelete = "Device.CascadeDelete";
    }

    public static class DeviceClientOverview
    {
        public const string Read = "DeviceClientOverview.Read";
    }

    public static class EdgeHost
    {
        public const string Read = "EdgeHost.Read";
    }

    public static class Recipe
    {
        public const string Read = "Recipe.Read";
        public const string Create = "Recipe.Create";
        public const string Update = "Recipe.Update";
        public const string Delete = "Recipe.Delete";
    }

    public static class Role
    {
        public const string Read = "Role.Read";
        public const string Define = "Role.Define";
        public const string Update = "Role.Update";
    }

    public static class ClientRelease
    {
        public const string Read = "ClientRelease.Read";
        public const string GenerateInstaller = "ClientRelease.GenerateInstaller";
        public const string Publish = "ClientRelease.Publish";
        public const string Manage = "ClientRelease.Manage";
        public const string HardDelete = "ClientRelease.HardDelete";
    }

    public static class AiRead
    {
        public const string Device = "AiRead.Device";
        public const string Process = "AiRead.Process";
        public const string ClientRelease = "AiRead.ClientRelease";
        public const string DeviceClientState = "AiRead.DeviceClientState";
        public const string Capacity = "AiRead.Capacity";
        public const string DeviceLog = "AiRead.DeviceLog";
        public const string ProductionRecord = "AiRead.ProductionRecord";
        public const string IdentityStatus = "AiRead.IdentityStatus";
    }

    public static class EdgeConfiguration
    {
        public const string Hardware = "Hardware.Config";
        public const string Parameter = "Param.Config";
    }

    private static readonly string[] AllPermissionValues =
    [
        Employee.Read,
        Employee.Onboard,
        Employee.Update,
        Employee.UpdateAccess,
        Employee.Deactivate,
        Employee.Terminate,
        Process.Read,
        Process.Create,
        Process.Update,
        Process.Delete,
        Device.Read,
        Device.Create,
        Device.Update,
        Device.MigrateProcess,
        Device.Delete,
        Device.CascadeDelete,
        DeviceClientOverview.Read,
        EdgeHost.Read,
        Recipe.Read,
        Recipe.Create,
        Recipe.Update,
        Recipe.Delete,
        Role.Read,
        Role.Define,
        Role.Update,
        ClientRelease.Read,
        ClientRelease.GenerateInstaller,
        ClientRelease.Publish,
        ClientRelease.Manage,
        ClientRelease.HardDelete,
        AiRead.Device,
        AiRead.Process,
        AiRead.ClientRelease,
        AiRead.DeviceClientState,
        AiRead.Capacity,
        AiRead.DeviceLog,
        AiRead.ProductionRecord,
        AiRead.IdentityStatus,
        EdgeConfiguration.Hardware,
        EdgeConfiguration.Parameter
    ];

    private static readonly HashSet<string> RoleAdminExcludedPermissions =
    [
        Role.Define,
        Role.Update,
        Employee.Terminate,
        Device.Create,
        Device.MigrateProcess,
        Device.Delete,
        Device.CascadeDelete,
        ClientRelease.HardDelete
    ];

    private static readonly IReadOnlyDictionary<string, string> CanonicalPermissions =
        AllPermissionValues.ToDictionary(
            permission => permission,
            permission => permission,
            StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, string> RoleAdminAssignableCanonicalPermissions =
        AllPermissionValues
            .Where(permission => !RoleAdminExcludedPermissions.Contains(permission))
            .ToDictionary(
                permission => permission,
                permission => permission,
                StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly(AllPermissionValues);

    public static IReadOnlyList<string> RoleAdminAssignable { get; } =
        Array.AsReadOnly(
            AllPermissionValues
                .Where(permission => !RoleAdminExcludedPermissions.Contains(permission))
                .ToArray());

    public static PermissionSetValidation Normalize(IEnumerable<string>? permissions)
        => NormalizeAgainst(permissions, CanonicalPermissions);

    public static PermissionSetValidation NormalizeRoleAdminAssignable(
        IEnumerable<string>? permissions)
        => NormalizeAgainst(permissions, RoleAdminAssignableCanonicalPermissions);

    /// <summary>
    /// 按目标角色统一规范化角色权限更新请求。
    /// RoleAdmin 的治理权限为内置锁定项：请求可以携带，也可以省略，
    /// 但不能把这些权限作为普通可编辑权限扩散到其它角色。
    /// </summary>
    public static PermissionSetValidation NormalizeForTargetRole(
        string? roleName,
        IEnumerable<string>? permissions)
    {
        var normalizedInput = Normalize(permissions);
        if (!normalizedInput.IsValid)
        {
            return normalizedInput;
        }

        var isRoleAdmin = string.Equals(
            roleName?.Trim(),
            SystemRoles.RoleAdmin,
            StringComparison.OrdinalIgnoreCase);
        var editablePermissions = isRoleAdmin
            ? normalizedInput.Permissions
                .Where(permission =>
                    !string.Equals(permission, Role.Read, StringComparison.Ordinal)
                    && !string.Equals(permission, Role.Define, StringComparison.Ordinal)
                    && !string.Equals(permission, Role.Update, StringComparison.Ordinal))
            : normalizedInput.Permissions;
        var editableValidation = NormalizeRoleAdminAssignable(editablePermissions);
        if (!editableValidation.IsValid)
        {
            return new PermissionSetValidation(
                normalizedInput.Permissions,
                editableValidation.RejectedPermissions);
        }

        if (!isRoleAdmin)
        {
            return editableValidation;
        }

        var effectivePermissions = new HashSet<string>(
            editableValidation.Permissions,
            StringComparer.OrdinalIgnoreCase)
        {
            Role.Read,
            Role.Define,
            Role.Update
        };

        return new PermissionSetValidation(
            AllPermissionValues
                .Where(effectivePermissions.Contains)
                .ToArray(),
            []);
    }

    private static PermissionSetValidation NormalizeAgainst(
        IEnumerable<string>? permissions,
        IReadOnlyDictionary<string, string> allowedPermissions)
    {
        if (permissions is null)
        {
            return new PermissionSetValidation([], ["<null>"]);
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rejected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPermission in permissions)
        {
            var permission = rawPermission?.Trim();
            if (string.IsNullOrWhiteSpace(permission))
            {
                rejected.Add("<empty>");
                continue;
            }

            if (!allowedPermissions.TryGetValue(permission, out var canonicalPermission))
            {
                rejected.Add(permission);
                continue;
            }

            normalized.Add(canonicalPermission);
        }

        return new PermissionSetValidation(
            AllPermissionValues
                .Where(normalized.Contains)
                .ToArray(),
            rejected
                .OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }
}

public sealed record PermissionSetValidation(
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> RejectedPermissions)
{
    public bool IsValid => RejectedPermissions.Count == 0;
}
