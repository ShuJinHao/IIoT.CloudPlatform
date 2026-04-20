using IIoT.Services.Contracts;

namespace IIoT.Services.Contracts.Authorization;

/// <summary>
/// 设备访问范围查询抽象。
/// 用来获取某个用户当前被授予的设备集合，供设备级访问控制使用。
/// </summary>
public interface IDevicePermissionService
{
    Task<IReadOnlyList<Guid>?> GetAccessibleDeviceIdsAsync(
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken = default);
}
