using IIoT.Infrastructure.Authentication;
using IIoT.Infrastructure.Caching;
using IIoT.Services.Common.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Generic;
using System.Text;

namespace IIoT.Infrastructure;

public static class DependencyInjection
{
    public static void AddInfrastructures(this IHostApplicationBuilder builder)
    {
        builder.AddRedisDistributedCache("redis-cache");
        // 绑定配置
        builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
        builder.Services.AddSingleton<ICacheService, RedisCacheService>();

        // 注册 JWT 生成器 (单例即可，因为它只是个纯函数计算器)
        builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
    }
}