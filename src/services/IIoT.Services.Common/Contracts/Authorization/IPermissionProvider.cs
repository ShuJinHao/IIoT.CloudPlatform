namespace IIoT.Services.Common.Contracts;

public interface IPermissionProvider
{
    /// <summary>
    /// 获取用户的有效权限集合。
    /// </summary>
    Task<IList<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken = default);
}
