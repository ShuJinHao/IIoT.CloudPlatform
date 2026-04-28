using IIoT.SharedKernel.Result;

namespace IIoT.Services.Contracts.Identity;

public interface IIdentityPasswordService
{
    Task<Result<bool>> CheckPasswordAsync(
        Guid accountId,
        string password,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> SetPasswordAsync(
        Guid accountId,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(
        Guid accountId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);

    Task<Result<bool>> ResetPasswordAsync(
        Guid accountId,
        string newPassword,
        CancellationToken cancellationToken = default);
}
