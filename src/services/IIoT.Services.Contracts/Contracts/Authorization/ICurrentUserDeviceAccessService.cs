using IIoT.SharedKernel.Result;

namespace IIoT.Services.Contracts.Authorization;

/// <summary>
/// 当前登录用户的生产设备访问范围服务。
/// Admin 返回 null 表示全量设备，普通用户返回已分配设备集合。
/// </summary>
public interface ICurrentUserDeviceAccessService
{
    bool IsAdministrator { get; }

    Task<Result<IReadOnlyList<Guid>?>> GetAccessibleDeviceIdsAsync(
        CancellationToken cancellationToken = default);

    Task<Result> EnsureCanAccessDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);
}
