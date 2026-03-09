using IIoT.SharedKernel.Result;

namespace IIoT.Services.Common.Contracts;

/// <summary>
/// 纯粹的身份认证与安全策略接口 (保安科)
/// 职责：仅处理账号、角色定义、权限点，不涉及任何业务档案
/// </summary>
public interface IIdentityService
{
    // --- 账号核心 (Account) ---
    // 创建底层账号并返回结果，不涉及业务实体保存
    Task<Result> CreateUserAsync(Guid id, string employeeNo, string password);

    Task<Result<bool>> CheckPasswordAsync(string employeeNo, string password);

    Task<Guid?> GetUserIdByEmployeeNoAsync(string employeeNo);

    // --- 安全策略 (Security Policy) ---
    // 角色定义：仅创建角色的“名字”
    Task<Result> CreateRoleAsync(string roleName);

    // 权限分配：定义该角色能点哪些按钮 (写入 AspNetRoleClaims)
    Task<Result<bool>> UpdateRolePermissionsAsync(string roleName, List<string> permissions);

    // 个人特权：给特定用户开小灶 (写入 AspUserClaims)
    Task<Result<bool>> UpdateUserPersonalPermissionsAsync(Guid userId, List<string> permissions);

    // --- 身份关联 (Identity Mapping) ---
    // 授予用户角色：仅操作 Identity 关联表
    Task<Result<bool>> AssignRoleToUserAsync(string employeeNo, string roleName);

    Task<IList<string>> GetRolesAsync(string employeeNo);
}