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

        builder.Services.Configure<PermissionCacheOptions>(builder.Configuration.GetSection("PermissionCache"));
        builder.Services.AddScoped<IPermissionProvider, PermissionProvider>();

        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        // 跨聚合的轻量只读查询入口
        builder.Services.AddScoped<IDataQueryService, DataQueryService>();

        // 一个实现类注册三个接口,共享同一个 Scoped 实例
        builder.Services.AddScoped<IdentityService>();
        builder.Services.AddScoped<IAccountService>(sp => sp.GetRequiredService<IdentityService>());
        builder.Services.AddScoped<IRolePolicyService>(sp => sp.GetRequiredService<IdentityService>());
        builder.Services.AddScoped<IUserQueryService>(sp => sp.GetRequiredService<IdentityService>());

        builder.Services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 8;
        })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IIoTDbContext>();
    }
}