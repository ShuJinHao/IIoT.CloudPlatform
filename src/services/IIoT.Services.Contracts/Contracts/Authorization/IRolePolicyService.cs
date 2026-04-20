using IIoT.SharedKernel.Result;

namespace IIoT.Services.Contracts.Authorization;

public interface IRolePolicyService
{
    Task<IList<string>> GetAllRolesAsync();

    Task<bool> RoleExistsAsync(string roleName);

    Task<Result> CreateRoleAsync(string roleName);

    Task<Result> DeleteRoleAsync(string roleName);

    Task<Result> RemoveRoleFromUserAsync(string employeeNo, string roleName);

    Task<List<string>?> GetRolePermissionsAsync(string roleName);

    Task<Result<bool>> UpdateRolePermissionsAsync(string roleName, List<string> permissions);

    Task<Result<bool>> UpdateUserPersonalPermissionsAsync(Guid userId, List<string> permissions);

    /// <summary>
    /// 获取指定用户的个人特批权限点列表（不含角色继承的权限）。
    /// </summary>
    Task<List<string>> GetUserPersonalPermissionsAsync(Guid userId);
}
