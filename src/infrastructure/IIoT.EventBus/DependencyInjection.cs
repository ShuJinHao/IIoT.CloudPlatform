using System.Reflection;
using IIoT.Services.Contracts;
using IIoT.SharedKernel.Configuration;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IIoT.EventBus;

public static class DependencyInjection
{
    public static void AddEventBus(this IHostApplicationBuilder builder, params Assembly[] assemblies)
    {
        var eventBusOptions = builder.Configuration.GetRequiredValidatedOptions<EventBusOptions>(
            EventBusOptions.SectionName,
            static options => options.Validate());

        builder.Services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        builder.Services.AddMassTransit(x =>
        {
            if (assemblies.Length > 0)
            {
                x.AddConsumers(assemblies);
            }

            x.SetKebabCaseEndpointNameFormatter();

            x.UsingRabbitMq((context, cfg) =>
            {
                var connectionString = builder.Configuration.GetConnectionString(ConnectionResourceNames.EventBus);
                if (!string.IsNullOrEmpty(connectionString))
                {
                    cfg.Host(connectionString);
                }

                if (eventBusOptions.RetryLimit > 0)
                {
                    cfg.UseMessageRetry(r => r.Incremental(
                        eventBusOptions.RetryLimit,
                        TimeSpan.FromSeconds(eventBusOptions.RetryInitialSeconds),
                        TimeSpan.FromSeconds(eventBusOptions.RetryIncrementSeconds)));
                }

                cfg.ConfigureEndpoints(context);
            });
        });

        // 不阻塞应用启动，避免 RabbitMQ 尚未就绪时卡住宿主。
        builder.Services.Configure<MassTransitHostOptions>(options =>
        {
            options.WaitUntilStarted = false;
            options.StartTimeout = TimeSpan.FromSeconds(eventBusOptions.HostStartTimeoutSeconds);
            options.StopTimeout = TimeSpan.FromSeconds(eventBusOptions.HostStopTimeoutSeconds);
        });
    }
}
