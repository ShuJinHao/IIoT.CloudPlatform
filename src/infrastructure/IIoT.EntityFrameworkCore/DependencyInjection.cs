using IIoT.Application.Contracts;
using IIoT.EntityFrameworkCore.Identity;
using IIoT.EntityFrameworkCore.Repository;
using IIoT.Services.Common.Contracts;
using IIoT.Services.Common.Options;
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
        // 在这里注册业务级的配置和实现
        builder.Services.Configure<PermissionCacheOptions>(builder.Configuration.GetSection("PermissionCache")); // 配置注入
        builder.Services.AddScoped<IPermissionProvider, PermissionProvider>(); // 业务注入

        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        builder.Services.AddScoped<IDataQueryService, DataQueryService>();
        builder.Services.AddScoped<IIdentityService, IdentityService>();

        // 🌟 核心改造：使用 ApplicationUser 和 Guid 版本的 Role 进行注册
        builder.Services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 8;
        })
            .AddRoles<IdentityRole<Guid>>() // 指定 Role 也是 Guid 主键
            .AddEntityFrameworkStores<IIoTDbContext>();
    }
}