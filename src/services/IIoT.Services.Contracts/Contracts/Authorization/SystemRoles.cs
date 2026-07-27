namespace IIoT.Services.Contracts.Authorization;

/// <summary>
/// 系统内置角色常量。
/// </summary>
public static class SystemRoles
{
    public const string Admin = "Admin";
    public const string DeviceAdmin = "DeviceAdmin";
    public const string ClientInstallerOperator = "ClientInstallerOperator";
    public const string ClientReleaseManager = "ClientReleaseManager";
    public const string ProductionViewer = "ProductionViewer";
    public const string RoleAdmin = "RoleAdmin";
    public const string HrAdmin = "HrAdmin";

    public static bool IsAdmin(string? roleName)
        => string.Equals(
            roleName?.Trim(),
            Admin,
            StringComparison.OrdinalIgnoreCase);

    public static bool ContainsAdmin(IEnumerable<string> roleNames)
        => roleNames.Any(IsAdmin);
}
