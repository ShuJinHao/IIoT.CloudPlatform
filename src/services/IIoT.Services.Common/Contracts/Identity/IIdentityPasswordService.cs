using IIoT.SharedKernel.Result;

namespace IIoT.Services.Common.Contracts;

public interface IIdentityPasswordService
{
    Task<Result<bool>> SetPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    Task<Result<bool>> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    Task<Result<bool>> ResetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default);
}
