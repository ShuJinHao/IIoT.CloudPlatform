namespace IIoT.Services.Common.Contracts;

/// <summary>
/// JWT 令牌生成器契约接口 (完全不依赖底层框架实体)
/// </summary>
public interface IJwtTokenGenerator
{
    /// <summary>
    /// 生成 JWT 令牌
    /// </summary>
    /// <param name="userId">用户的灵魂绑定 ID (Guid)</param>
    /// <param name="employeeNo">工号</param>
    /// <param name="roles">该用户拥有的角色列表</param>
    /// <param name="permissions">该用户拥有的全部权限点列表 (角色权限 + 个人特批权限的并集)</param>
    /// <returns>Base64 编码的 JWT 字符串</returns>
    string GenerateToken(Guid userId, string employeeNo, IList<string> roles, IList<string> permissions);
}
