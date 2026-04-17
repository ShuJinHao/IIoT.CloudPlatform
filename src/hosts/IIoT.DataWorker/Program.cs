using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Dapper;
using IIoT.DataWorker.Consumers;
using IIoT.Infrastructure;
using IIoT.Infrastructure.Logging;
using IIoT.ProductionService;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.Services.Common.Behaviors;
using IIoT.Services.Common.DependencyInjection;
using IIoT.Services.Common.Events.PassStations;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
var consumerOptions = builder.Configuration.GetSection(ConsumerConcurrencyOptions.SectionName).Get<ConsumerConcurrencyOptions>()
                      ?? new ConsumerConcurrencyOptions();

builder.ConfigureContainer(
    new DefaultServiceProviderFactory(new ServiceProviderOptions
    {
        ValidateOnBuild = false,
        ValidateScopes = false
    }));

builder.AddSerilog("dataworker");
builder.AddServiceDefaults();
builder.AddDapper();
builder.AddInfrastructures();

builder.Services.AddConfiguredMediatR(builder.Configuration, cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<ReceiveHourlyCapacityCommand>();
    cfg.AddOpenBehavior(typeof(DistributedLockBehavior<,>));
});

builder.Services.AddPassStationType<
    PassDataInjectionReceivedEvent,
    InjectionWriteModel,
    InjectionMapper>();
builder.Services.AddPassStationType<
    PassDataStackingReceivedEvent,
    StackingWriteModel,
    StackingMapper>();

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddConsumer<PassStationConsumer<PassDataInjectionReceivedEvent>>(cfg =>
    {
        cfg.ConcurrentMessageLimit = consumerOptions.PassStationConcurrentMessageLimit;
    });
    x.AddConsumer<PassStationConsumer<PassDataStackingReceivedEvent>>(cfg =>
    {
        cfg.ConcurrentMessageLimit = consumerOptions.PassStationConcurrentMessageLimit;
    });
    x.AddConsumer<DeviceLogConsumer>(cfg =>
    {
        cfg.ConcurrentMessageLimit = consumerOptions.DeviceLogConcurrentMessageLimit;
    });
    x.AddConsumer<HourlyCapacityConsumer>(cfg =>
    {
        cfg.ConcurrentMessageLimit = consumerOptions.HourlyCapacityConcurrentMessageLimit;
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        var connectionString = builder.Configuration.GetConnectionString("eventbus");
        cfg.Host(connectionString);

        // 成功消费后确认消息，失败时按重试策略重新处理。
        cfg.UseMessageRetry(r => r.Incremental(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));

        cfg.ConfigureEndpoints(context);
    });
});

var host = builder.Build();
host.Run();
