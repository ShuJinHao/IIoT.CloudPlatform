using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Reflection;

namespace IIoT.EventBus;

public static class DependencyInjection
{
    public static void AddEventBus(this IHostApplicationBuilder builder, params Assembly[] assemblies)
    {
        builder.Services.AddMassTransit(x =>
        {
            if (assemblies.Length > 0)
            {
                x.AddConsumers(assemblies);
            }

            x.SetKebabCaseEndpointNameFormatter();

            x.UsingRabbitMq((context, cfg) =>
            {
                var connectionString = builder.Configuration.GetConnectionString("eventbus");
                cfg.Host(connectionString);

                // 全局重试策略：3次，间隔递增 (1s → 3s → 5s)
                cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

                cfg.ConfigureEndpoints(context);
            });
        });
    }
}