namespace IIoT.Services.Common.Contracts;

public interface IPermissionProvider
{
    /// <summary>
    /// 根据用户 ID，动态获取该用户所拥有的全部权限标识集合 (角色权限 + 个人特批权限的并集)
    /// </summary>
    /// <param name="userId">当前用户的灵魂绑定 Guid</param>
    /// <param name="cancellationToken"></param>
    Task<IList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
}