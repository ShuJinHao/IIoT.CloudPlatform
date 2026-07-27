using IIoT.SharedKernel.Result;

namespace IIoT.Services.Contracts.Authorization;

public interface IAdminTargetGuard
{
    Task<Result> EnsureMutableNonAdminTargetAsync(
        Guid targetUserId,
        CancellationToken cancellationToken = default);
}

public static class AdminTargetProtectionErrors
{
    public const string TargetNotFound = "目标用户不存在。";
    public const string AdminTargetProtected = "管理员账号受保护，禁止通过该入口操作。";
}
