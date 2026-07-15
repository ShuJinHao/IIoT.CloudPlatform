using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.CloudPlatform.PersistenceTests;

public sealed class PermissionProviderCancellationTests
{
    [Fact]
    public async Task PermissionProvider_CallerCancellationDuringIdentityAwait_ShouldPropagate()
    {
        var interceptor = new BlockingDbCommandInterceptor(command =>
            command.CommandText.Contains("AspNetUserClaims", StringComparison.Ordinal));
        using var provider = CreateIdentityServiceProvider(interceptor);
        using var scope = provider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"cancel-{Guid.NewGuid():N}",
            IsEnabled = true
        };
        Assert.True((await userManager.CreateAsync(user)).Succeeded);
        var permissionProvider = TestServiceProviders.CreatePermissionProvider(scope.ServiceProvider);
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var callerCancellation = new CancellationTokenSource();

        using var preCancelled = new CancellationTokenSource();
        preCancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => permissionProvider.GetPermissionsAsync(user.Id, preCancelled.Token));

        var readTask = permissionProvider.GetPermissionsAsync(user.Id, callerCancellation.Token);
        await interceptor.WaitUntilBlockedAsync(testTimeout.Token);
        await callerCancellation.CancelAsync();

        try
        {
            var cancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => readTask.WaitAsync(TimeSpan.FromSeconds(5), testTimeout.Token));
            Assert.Equal(callerCancellation.Token, cancellation.CancellationToken);
        }
        finally
        {
            interceptor.Release();
        }
    }

    private static ServiceProvider CreateIdentityServiceProvider(DbCommandInterceptor interceptor)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_ =>
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            return connection;
        });
        services.AddScoped<IIoTDbContext>(provider =>
        {
            var options = new DbContextOptionsBuilder<IIoTDbContext>()
                .UseSqlite(provider.GetRequiredService<SqliteConnection>())
                .AddInterceptors(interceptor)
                .Options;
            return new SqliteTestDbContext(options);
        });
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IIoTDbContext>();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IIoTDbContext>().Database.EnsureCreated();
        return provider;
    }

}
