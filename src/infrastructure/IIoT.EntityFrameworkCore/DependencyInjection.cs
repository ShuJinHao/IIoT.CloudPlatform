using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Outbox;
using IIoT.EntityFrameworkCore.Persistence;
using IIoT.EntityFrameworkCore.Repository;
using IIoT.Services.CrossCutting.Caching.Options;
using IIoT.Services.Contracts.Auditing;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Authorization;
using IIoT.Services.Contracts.Identity;
using IIoT.Services.Contracts.RecordQueries;
using IIoT.SharedKernel.Configuration;
using IIoT.SharedKernel.Repository;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IIoT.EntityFrameworkCore;

public static class DependencyInjection
{
    public static void AddEfCore(this IHostApplicationBuilder builder)
    {
        var postgresOptions = builder.Configuration.GetRequiredValidatedOptions<PostgresOptions>(
            PostgresOptions.SectionName,
            static options => options.Validate());

        builder.AddNpgsqlDbContext<IIoTDbContext>(
            ConnectionResourceNames.IiotDatabase,
            settings =>
            {
                settings.DisableRetry = !postgresOptions.EnableRetry;
                settings.CommandTimeout = postgresOptions.CommandTimeoutSeconds;
            },
            options =>
            {
                options.UseNpgsql(npgsqlOptions =>
                {
                    npgsqlOptions.CommandTimeout(postgresOptions.CommandTimeoutSeconds);
                    if (postgresOptions.EnableRetry)
                    {
                        npgsqlOptions.EnableRetryOnFailure(
                            postgresOptions.MaxRetryCount,
                            TimeSpan.FromSeconds(postgresOptions.MaxRetryDelaySeconds),
                            null);
                    }
                });
            });

        builder.Services.Configure<PermissionCacheOptions>(
            builder.Configuration.GetSection("PermissionCache"));
        builder.Services.AddScoped<IPermissionProvider, PermissionProvider>();

        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        builder.Services.AddScoped<IIdentityAccountStore, IdentityAccountStore>();
        builder.Services.AddScoped<IEmployeeLookupService, EmployeeLookupService>();
        builder.Services.AddScoped<IDevicePermissionService, DevicePermissionService>();
        builder.Services.AddScoped<IIdentityPasswordService, IdentityPasswordService>();
        builder.Services.AddScoped<IRefreshTokenService, EfRefreshTokenService>();
        builder.Services.AddScoped<IRolePolicyService, RolePolicyService>();
        builder.Services.AddScoped<IUserQueryService, UserQueryService>();
        builder.Services.AddScoped<IAuditTrailService, Auditing.EfAuditTrailService>();
        builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        builder.Services.AddScoped<IProcessReadQueryService, QueryServices.ProcessReadQueryService>();
        builder.Services.AddScoped<IDeviceReadQueryService, QueryServices.DeviceReadQueryService>();
        builder.Services.AddScoped<IRecipeReadQueryService, QueryServices.RecipeReadQueryService>();

        builder.Services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 8;
        })
        .AddRoles<IdentityRole<Guid>>()
        .AddEntityFrameworkStores<IIoTDbContext>();
    }
}
