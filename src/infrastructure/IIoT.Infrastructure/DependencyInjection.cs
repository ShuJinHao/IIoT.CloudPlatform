using IIoT.Infrastructure.Authentication;
using IIoT.Infrastructure.Caching;
using IIoT.Infrastructure.Locking;
using IIoT.Services.Common.Contracts;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace IIoT.Infrastructure;

public static class DependencyInjection
{
    public static void AddInfrastructures(this IHostApplicationBuilder builder)
    {
        builder.AddRedisDistributedCache("redis-cache");
        var redisConnectionString = builder.Configuration.GetConnectionString("redis-cache");
        var cacheSafetyOptions = builder.Configuration.GetSection(CacheSafetyOptions.SectionName).Get<CacheSafetyOptions>()
            ?? new CacheSafetyOptions();

        builder.Services.AddFusionCache()
            .WithDefaultEntryOptions(new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromMinutes(5),
                IsFailSafeEnabled = true,
                FailSafeMaxDuration = cacheSafetyOptions.ResolveFailSafeDuration(),
                FailSafeThrottleDuration = TimeSpan.FromSeconds(10)
            })
            .WithSystemTextJsonSerializer()
            .WithDistributedCache(provider => provider.GetRequiredService<IDistributedCache>())
            .WithStackExchangeRedisBackplane(options =>
            {
                if (string.IsNullOrWhiteSpace(redisConnectionString))
                    throw new InvalidOperationException("Connection string 'redis-cache' is required for FusionCache backplane.");

                options.Configuration = redisConnectionString;
            });

        builder.Services.Configure<JwtSettings>(
            builder.Configuration.GetSection(JwtSettings.SectionName));
        builder.Services.Configure<DistributedLockOptions>(
            builder.Configuration.GetSection(DistributedLockOptions.SectionName));
        builder.Services.Configure<CacheSafetyOptions>(
            builder.Configuration.GetSection(CacheSafetyOptions.SectionName));
        builder.Services.PostConfigure<JwtSettings>(options =>
        {
            options.Secret = JwtSecretResolver.Resolve(builder.Environment, options.Secret);
        });

        builder.Services.AddSingleton<ICacheService, RedisCacheService>();
        builder.Services.AddSingleton<IDistributedLockService, RedisDistributedLockService>();
        builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
    }
}
