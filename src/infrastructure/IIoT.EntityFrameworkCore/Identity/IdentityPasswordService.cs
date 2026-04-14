using IIoT.Services.Common.Contracts;
using IIoT.SharedKernel.Result;
using Microsoft.AspNetCore.Identity;

namespace IIoT.EntityFrameworkCore.Identity;

public sealed class IdentityPasswordService(UserManager<ApplicationUser> userManager) : IIdentityPasswordService
{
    public async Task<Result<bool>> SetPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Failure("用户不存在");
        }

        var result = await userManager.AddPasswordAsync(user, password);
        return result.Succeeded
            ? Result.Success(true)
            : Result.Failure(result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task<Result<bool>> CheckPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsEnabled)
        {
            return Result.Success(false);
        }

        var isValid = await userManager.CheckPasswordAsync(user, password);
        return Result.Success(isValid);
    }

    public async Task<Result> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Failure("用户不存在");
        }

        var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        return result.ToResult();
    }

    public async Task<Result<bool>> ResetPasswordAsync(
        Guid userId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result.Failure("用户不存在");
        }

        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            var removeResult = await userManager.RemovePasswordAsync(user);
            if (!removeResult.Succeeded)
            {
                return Result.Failure(removeResult.Errors.Select(e => e.Description).ToArray());
            }
        }

        var addResult = await userManager.AddPasswordAsync(user, newPassword);
        return addResult.Succeeded
            ? Result.Success(true)
            : Result.Failure(addResult.Errors.Select(e => e.Description).ToArray());
    }
}
