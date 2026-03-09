using IIoT.SharedKernel.Result;

namespace IIoT.Services.Common.Contracts;

/// <summary>
/// 身份认证领域服务接口 (依赖倒置的抽象契约)
/// </summary>
public interface IIdentityService
{
    // 创建底层身份账号 (保安)
    Task<Result> CreateUserAsync(Guid id, string employeeNo, string password);

    // 创建角色
    Task<Result> CreateRoleAsync(string roleName);

    // 校验密码
    Task<Result<bool>> CheckPasswordAsync(string employeeNo, string password);

    // 获取用户角色集合
    Task<IList<string>> GetRolesAsync(string employeeNo);

    // 获取用户的灵魂绑定ID
    Task<Guid?> GetUserIdByEmployeeNoAsync(string employeeNo);

    // 给角色分配权限集合 (写入 AspNetRoleClaims 表)
    Task<Result<bool>> UpdateRolePermissionsAsync(string roleName, List<string> permissions);

    // 给用户分配角色 (写入 AspNetUserRoles 表)
    Task<Result<bool>> AssignRoleToUserAsync(string employeeNo, string roleName);
}