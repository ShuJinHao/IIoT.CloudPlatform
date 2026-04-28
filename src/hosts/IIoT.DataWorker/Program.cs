using IIoT.Core.Production.Contracts.PassStation;
using IIoT.Dapper;
using IIoT.DataWorker.Consumers;
using IIoT.DataWorker.Outbox;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Outbox;
using IIoT.EventBus;
using IIoT.Infrastructure;
using IIoT.Infrastructure.Logging;
using IIoT.ProductionService;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.ProductionService.Commands.PassStations;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.DependencyInjection;
using IIoT.Services.Contracts.Events.PassStations;
using IIoT.SharedKernel.Configuration;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
var eventBusOptions = builder.Configuration.GetRequiredValidatedOptions<EventBusOptions>(
    EventBusOptions.SectionName,
    static options => options.Validate());

builder.ConfigureContainer(
    new DefaultServiceProviderFactory(new ServiceProviderOptions
    {
        ValidateOnBuild = false,
        ValidateScopes = false
    }));

builder.AddSerilog("dataworker");
builder.AddServiceDefaults();
builder.AddDapper();
builder.AddEfCore();
builder.AddInfrastructures();

builder.Services.AddConfiguredMediatR(builder.Configuration, cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<ReceiveHourlyCapacityCommand>();
    cfg.AddOpenBehavior(typeof(DistributedLockBehavior<,>));
});

builder.Services.AddScoped<IOutboxMessageDispatcher, OutboxMessageDispatcher>();
_ = builder.AddValidatedOptions<OutboxDispatcherOptions>(
    OutboxDispatcherOptions.SectionName,
    static options => options.Validate());
builder.Services.Configure<MassTransitHostOptions>(options =>
{
    options.WaitUntilStarted = false;
    options.StartTimeout = TimeSpan.FromSeconds(eventBusOptions.HostStartTimeoutSeconds);
    options.StopTimeout = TimeSpan.FromSeconds(eventBusOptions.HostStopTimeoutSeconds);
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
    x.AddConsumer<PassStationConsumer<PassDataInjectionReceivedEvent>>();
    x.AddConsumer<PassStationConsumer<PassDataStackingReceivedEvent>>();
    x.AddConsumer<DeviceLogConsumer>();
    x.AddConsumer<HourlyCapacityConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var connectionString = builder.Configuration.GetConnectionString(ConnectionResourceNames.EventBus)
            ?? throw new InvalidOperationException($"Missing {ConnectionResourceNames.EventBus} connection string.");
        cfg.Host(connectionString);

        cfg.ReceiveEndpoint(eventBusOptions.ResolveEndpointName(RabbitMqEndpointNames.PassStationInjection), endpoint =>
        {
            endpoint.ApplyIIoTEndpointDefaults(
                eventBusOptions,
                eventBusOptions.Consumers.PassStationConcurrentMessageLimit);
            endpoint.ConfigureConsumer<PassStationConsumer<PassDataInjectionReceivedEvent>>(context);
        });

        cfg.ReceiveEndpoint(eventBusOptions.ResolveEndpointName(RabbitMqEndpointNames.PassStationStacking), endpoint =>
        {
            endpoint.ApplyIIoTEndpointDefaults(
                eventBusOptions,
                eventBusOptions.Consumers.PassStationConcurrentMessageLimit);
            endpoint.ConfigureConsumer<PassStationConsumer<PassDataStackingReceivedEvent>>(context);
        });

        cfg.ReceiveEndpoint(eventBusOptions.ResolveEndpointName(RabbitMqEndpointNames.DeviceLogs), endpoint =>
        {
            endpoint.ApplyIIoTEndpointDefaults(
                eventBusOptions,
                eventBusOptions.Consumers.DeviceLogConcurrentMessageLimit);
            endpoint.ConfigureConsumer<DeviceLogConsumer>(context);
        });

        cfg.ReceiveEndpoint(eventBusOptions.ResolveEndpointName(RabbitMqEndpointNames.HourlyCapacities), endpoint =>
        {
            endpoint.ApplyIIoTEndpointDefaults(
                eventBusOptions,
                eventBusOptions.Consumers.HourlyCapacityConcurrentMessageLimit);
            endpoint.ConfigureConsumer<HourlyCapacityConsumer>(context);
        });
    });
});

builder.Services.AddHostedService<OutboxDispatcherWorker>();

var host = builder.Build();

var startupLogger = host.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("IIoT.DataWorker.Startup");
var configuredQueues = new[]
{
    eventBusOptions.ResolveEndpointName(RabbitMqEndpointNames.PassStationInjection),
    eventBusOptions.ResolveEndpointName(RabbitMqEndpointNames.PassStationStacking),
    eventBusOptions.ResolveEndpointName(RabbitMqEndpointNames.DeviceLogs),
    eventBusOptions.ResolveEndpointName(RabbitMqEndpointNames.HourlyCapacities)
};

startupLogger.LogInformation(
    "DataWorker queues configured queues={queues} error_queue_suffix={error_queue_suffix} skipped_queue_suffix={skipped_queue_suffix}",
    configuredQueues,
    "_error",
    "_skipped");

host.Run();
