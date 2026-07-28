using IIoT.Services.Contracts.Identity;

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

    /// <summary>
    /// 调用者授权只接受身份系统签发的规范 Admin 角色名。
    /// 不对角色 Claim 做 Trim 或大小写折叠，避免历史异常角色获得管理员绕过。
    /// </summary>
    public static bool IsCanonicalAdminRole(string? roleName)
        => string.Equals(roleName, Admin, StringComparison.Ordinal);

    public static bool ContainsCanonicalAdminRole(IEnumerable<string> roleNames)
        => roleNames.Any(IsCanonicalAdminRole);

    /// <summary>
    /// 管理员调用通道的唯一判定。
    /// 只有已认证的人类主体且携带身份系统签发的规范 Admin 角色时才成立。
    /// </summary>
    public static bool IsAuthenticatedHumanAdmin(
        bool isAuthenticated,
        string? actorType,
        IEnumerable<string>? roleNames)
        => isAuthenticated
           && string.Equals(
               actorType,
               IIoTClaimTypes.HumanActor,
               StringComparison.Ordinal)
           && roleNames is not null
           && ContainsCanonicalAdminRole(roleNames);

    /// <summary>
    /// 用户输入和数据库目标保护使用的保守判定。
    /// 任何 Trim 后大小写等于 Admin 的名称都按受保护目标处理。
    /// </summary>
    public static bool IsAdminLike(string? roleName)
        => string.Equals(
            roleName?.Trim(),
            Admin,
            StringComparison.OrdinalIgnoreCase);

    public static bool ContainsAdminLike(IEnumerable<string> roleNames)
        => roleNames.Any(IsAdminLike);
}
