using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using IIoT.SharedKernel.Repository;
using IIoT.Infrastructure.EntityFrameworkCore.Repository;
using IIoT.Application.Contracts;

namespace IIoT.Infrastructure.EntityFrameworkCore;

public static class DependencyInjection
{
    public static void AddEfCore(this IHostApplicationBuilder builder)
    {
        // 🌟 完美保留 Aspire 注入方式，连接字符串名称改为适合工业项目的 "iiot-db"
        builder.AddNpgsqlDbContext<IIoTDbContext>("iiot-db");

        // 完美保留泛型仓储的生命周期注册
        builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
        builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        // 完美保留只读查询服务的注册
        builder.Services.AddScoped<IDataQueryService, DataQueryService>();

        // 完美保留 Identity 的精简密码规则，仅替换底层的 DbContext
        builder.Services.AddIdentityCore<IdentityUser>(options =>
        {
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 8;
        })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<IIoTDbContext>();
    }
}