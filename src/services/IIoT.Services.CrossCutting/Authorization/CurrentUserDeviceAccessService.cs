using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.SharedKernel.Result;

namespace IIoT.Services.CrossCutting.Authorization;

public sealed class CurrentUserDeviceAccessService(
    ICurrentUser currentUser,
    IDevicePermissionService devicePermissionService)
    : ICurrentUserDeviceAccessService
{
    private const string InvalidUserCredentialMessage = "用户凭证异常";
    private const string UnauthorizedDeviceMessage = "越权: 未授权访问该设备";

    public bool IsAdministrator => string.Equals(
        currentUser.Role,
        SystemRoles.Admin,
        StringComparison.Ordinal);

    public async Task<Result<IReadOnlyList<Guid>?>> GetAccessibleDeviceIdsAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsAdministrator)
        {
            return Result.Success<IReadOnlyList<Guid>?>(null);
        }

        if (!Guid.TryParse(currentUser.Id, out var userId))
        {
            return Result.Failure(InvalidUserCredentialMessage);
        }

        var accessibleDeviceIds = await devicePermissionService.GetAccessibleDeviceIdsAsync(
            userId,
            cancellationToken);

        return Result.Success<IReadOnlyList<Guid>?>(accessibleDeviceIds);
    }

    public async Task<Result> EnsureCanAccessDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var scope = await GetAccessibleDeviceIdsAsync(cancellationToken);
        if (!scope.IsSuccess)
        {
            return Result.Failure(scope.Errors?.ToArray() ?? [InvalidUserCredentialMessage]);
        }

        if (scope.Value is null || scope.Value.Contains(deviceId))
        {
            return Result.Success();
        }

        return Result.Failure(UnauthorizedDeviceMessage);
    }
}
