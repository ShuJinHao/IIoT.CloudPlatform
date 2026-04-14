using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Repository;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Caching.Options;
using IIoT.SharedKernel.Repository;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IIoT.EntityFrameworkCore;

public static class DependencyInjection
{
    public static void AddEfCore(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<IIoTDbContext>("iiot-db");

        builder.Services.Configure<PermissionCacheOptions>(builder.Configuration.GetSection("PermissionCache"));
        builder.Services.AddScoped<IPermissionProvider, PermissionProvider>();

        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        // 跨聚合的轻量只读查询入口
        builder.Services.AddScoped<IDataQueryService, DataQueryService>();

        builder.Services.AddScoped<IIdentityAccountStore, IdentityAccountStore>();
        builder.Services.AddScoped<IIdentityPasswordService, IdentityPasswordService>();
        builder.Services.AddScoped<IRolePolicyService, RolePolicyService>();
        builder.Services.AddScoped<IUserQueryService, UserQueryService>();

        builder.Services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 8;
        })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IIoTDbContext>();
    }
}
