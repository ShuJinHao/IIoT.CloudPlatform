using IIoT.SharedKernel.Result;

namespace IIoT.Services.Common.Contracts;

/// <summary>
/// 纯粹的身份认证返回模型 (绝对不含任何业务数据)
/// </summary>
public record IdentityUserDto(Guid Id, string EmployeeNo, IList<string> Roles);

/// <summary>
/// 纯粹的身份认证与安全策略接口 (保安科)
/// 职责：处理账号(CRUD)、角色定义、权限点，不涉及任何业务档案
/// </summary>
public interface IIdentityService
{
    // --- 1. 账号全生命周期管理 (Account CRUD) ---
    Task<Result> CreateUserAsync(Guid id, string employeeNo, string password);

    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword);

    Task<Result> DeleteUserAsync(Guid userId); // 🌟 调用此方法会触发级联删除工厂用户

    Task<Result<bool>> CheckPasswordAsync(string employeeNo, string password);

    Task<Guid?> GetUserIdByEmployeeNoAsync(string employeeNo);

    // --- 2. 账号与角色查询端 (Queries) ---
    Task<IList<IdentityUserDto>> GetAllUsersAsync();

    Task<IdentityUserDto?> GetUserByEmployeeNoAsync(string employeeNo);

    Task<IList<string>> GetAllRolesAsync();

    // --- 3. 角色与权限策略 (Security Policy) ---
    Task<Result> CreateRoleAsync(string roleName);

    Task<Result<bool>> UpdateRolePermissionsAsync(string roleName, List<string> permissions);

    Task<Result<bool>> UpdateUserPersonalPermissionsAsync(Guid userId, List<string> permissions);

    // --- 4. 身份关联操作 (Assignment) ---
    Task<Result<bool>> AssignRoleToUserAsync(string employeeNo, string roleName);

    Task<Result> RemoveRoleFromUserAsync(string employeeNo, string roleName);

    Task<IList<string>> GetRolesAsync(string employeeNo);
}