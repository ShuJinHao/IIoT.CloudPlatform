using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Identity;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IIoT.CloudPlatform.TestKit;

internal static class TestServiceProviders
{
    public static ServiceProvider CreateEfServiceProvider(IMediator mediator)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(mediator);
        services.AddSingleton<IMediator>(mediator);
        AddSqliteDbContext(services);

        return BuildInitializedProvider(services);
    }

    public static ServiceProvider CreateIdentityServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        AddSqliteDbContext(services);
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 8;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IIoTDbContext>();

        return BuildInitializedProvider(services);
    }

    public static PermissionProvider CreatePermissionProvider(IServiceProvider services) =>
        new(
            services.GetRequiredService<IUserStore<ApplicationUser>>(),
            services.GetRequiredService<IRoleStore<IdentityRole<Guid>>>(),
            services.GetRequiredService<ILookupNormalizer>());

    private static void AddSqliteDbContext(IServiceCollection services)
    {
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
                .Options;
            return new SqliteTestDbContext(options);
        });
    }

    private static ServiceProvider BuildInitializedProvider(IServiceCollection services)
    {
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IIoTDbContext>().Database.EnsureCreated();
        return provider;
    }
}
