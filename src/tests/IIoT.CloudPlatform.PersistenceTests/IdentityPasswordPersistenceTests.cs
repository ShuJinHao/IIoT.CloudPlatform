using IIoT.EntityFrameworkCore.Identity;
using IIoT.Infrastructure.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class IdentityPasswordPersistenceTests
{
    [Fact]
    public async Task IdentityPasswordService_ShouldLockUserAfterConsecutivePasswordFailures()
    {
        using var runtime = await IdentityPasswordRuntime.CreateAsync();
        var userManager = runtime.UserManager;
        var user = runtime.User;
        var disableLockout = await userManager.SetLockoutEnabledAsync(user, false);
        Assert.True(disableLockout.Succeeded, string.Join("; ", disableLockout.Errors.Select(e => e.Description)));
        await AssertPasswordFailuresAsync(runtime.PasswordService, user.Id, count: 5);

        var lockedUser = await runtime.ReloadUserAsync();
        Assert.True(await userManager.GetLockoutEnabledAsync(lockedUser));
        Assert.True(await userManager.IsLockedOutAsync(lockedUser));

        var lockedCheck = await runtime.PasswordService.CheckPasswordAsync(user.Id, "Password123");
        Assert.True(lockedCheck.IsSuccess);
        Assert.False(lockedCheck.Value);
    }

    [Fact]
    public async Task IdentityPasswordService_ShouldResetFailedCountAfterSuccessfulPasswordCheck()
    {
        using var runtime = await IdentityPasswordRuntime.CreateAsync();

        var failedCheck = await runtime.PasswordService.CheckPasswordAsync(
            runtime.User.Id,
            "WrongPassword123");
        Assert.True(failedCheck.IsSuccess);
        Assert.False(failedCheck.Value);
        var failedUser = await runtime.ReloadUserAsync();
        Assert.Equal(1, await runtime.UserManager.GetAccessFailedCountAsync(failedUser));

        var successfulCheck = await runtime.PasswordService.CheckPasswordAsync(
            runtime.User.Id,
            "Password123");
        Assert.True(successfulCheck.IsSuccess);
        Assert.True(successfulCheck.Value);
        var resetUser = await runtime.ReloadUserAsync();
        Assert.Equal(0, await runtime.UserManager.GetAccessFailedCountAsync(resetUser));
        Assert.False(await runtime.UserManager.IsLockedOutAsync(resetUser));
    }

    [Fact]
    public async Task IdentityPasswordService_ResetPassword_ShouldNotUnlockLockedUser()
    {
        using var runtime = await IdentityPasswordRuntime.CreateAsync();
        await AssertPasswordFailuresAsync(runtime.PasswordService, runtime.User.Id, count: 5);
        var lockedUser = await runtime.ReloadUserAsync();
        Assert.True(await runtime.UserManager.IsLockedOutAsync(lockedUser));

        Assert.True((await runtime.PasswordService.ResetPasswordAsync(
            runtime.User.Id,
            "NewPassword123")).IsSuccess);
        var resetUser = await runtime.ReloadUserAsync();
        Assert.True(await runtime.UserManager.IsLockedOutAsync(resetUser));
    }

    private static async Task AssertPasswordFailuresAsync(
        IdentityPasswordService passwordService,
        Guid userId,
        int count)
    {
        for (var attempt = 0; attempt < count; attempt++)
        {
            var check = await passwordService.CheckPasswordAsync(userId, "WrongPassword123");
            Assert.True(check.IsSuccess);
            Assert.False(check.Value);
        }
    }

    private static async Task<ApplicationUser> CreateIdentityUserAsync(
        UserManager<ApplicationUser> userManager,
        string password)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"identity-{Guid.NewGuid():N}",
            IsEnabled = true
        };
        var createUser = await userManager.CreateAsync(user, password);
        Assert.True(createUser.Succeeded, string.Join("; ", createUser.Errors.Select(e => e.Description)));
        return user;
    }

    private sealed class IdentityPasswordRuntime : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;

        private IdentityPasswordRuntime(
            ServiceProvider provider,
            IServiceScope scope,
            UserManager<ApplicationUser> userManager,
            ApplicationUser user)
        {
            _provider = provider;
            _scope = scope;
            UserManager = userManager;
            User = user;
            PasswordService = new IdentityPasswordService(userManager);
        }

        public UserManager<ApplicationUser> UserManager { get; }

        public ApplicationUser User { get; }

        public IdentityPasswordService PasswordService { get; }

        public async Task<ApplicationUser> ReloadUserAsync() =>
            await UserManager.FindByIdAsync(User.Id.ToString())
            ?? throw new InvalidOperationException("User was not created.");

        public static async Task<IdentityPasswordRuntime> CreateAsync()
        {
            var provider = TestServiceProviders.CreateIdentityServiceProvider();
            var scope = provider.CreateScope();
            try
            {
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await CreateIdentityUserAsync(userManager, "Password123");
                return new IdentityPasswordRuntime(provider, scope, userManager, user);
            }
            catch
            {
                scope.Dispose();
                provider.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }
}
