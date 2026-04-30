using IIoT.Dapper;
using IIoT.DataWorker.Consumers;
using IIoT.DataWorker.Outbox;
using IIoT.EntityFrameworkCore;
using IIoT.EntityFrameworkCore.Outbox;
using IIoT.EventBus;
using IIoT.Infrastructure;
using IIoT.Infrastructure.Logging;
using IIoT.ProductionService;
using IIoT.ProductionService.Caching;
using IIoT.ProductionService.Commands.Capacities;
using IIoT.Services.CrossCutting.Behaviors;
using IIoT.Services.CrossCutting.DependencyInjection;
using IIoT.Services.Contracts;
using IIoT.Services.Contracts.Caching;
using IIoT.SharedKernel.Configuration;
using MassTransit;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Sockets;

var builder = Host.CreateApplicationBuilder(args);
var healthCheckRequested = IsHealthCheckMode(args);
EventBusOptions eventBusOptions;
try
{
    eventBusOptions = builder.Configuration.GetRequiredValidatedOptions<EventBusOptions>(
        EventBusOptions.SectionName,
        static options => options.Validate());
}
catch (Exception ex) when (healthCheckRequested)
{
    Console.Error.WriteLine($"IIoT.DataWorker health check failed: {ex.Message}");
    Environment.ExitCode = 1;
    return;
}

builder.ConfigureContainer(
    new DefaultServiceProviderFactory(new ServiceProviderOptions
    {
        ValidateOnBuild = false,
        ValidateScopes = false
    }));

builder.AddSerilog("dataworker");
builder.AddServiceDefaults();
try
{
    builder.AddDapper();
    builder.AddEfCore();
    builder.AddInfrastructures();
}
catch (Exception ex) when (healthCheckRequested)
{
    Console.Error.WriteLine($"IIoT.DataWorker health check failed: {ex.Message}");
    Environment.ExitCode = 1;
    return;
}

if (healthCheckRequested)
{
    using var healthCheckProvider = builder.Services.BuildServiceProvider();
    Environment.ExitCode = await RunHealthCheckAsync(
        healthCheckProvider,
        builder.Configuration,
        CancellationToken.None);
    return;
}

builder.Services.AddConfiguredMediatR(builder.Configuration, cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<ReceiveHourlyCapacityCommand>();
    cfg.AddOpenBehavior(typeof(DistributedLockBehavior<,>));
});

builder.Services.AddScoped<IOutboxMessageDispatcher, OutboxMessageDispatcher>();
builder.Services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
builder.Services.AddScoped<IDeviceCacheInvalidationService, DeviceCacheInvalidationService>();
builder.Services.AddScoped<IRecipeCacheInvalidationService, RecipeCacheInvalidationService>();
_ = builder.AddValidatedOptions<OutboxDispatcherOptions>(
    OutboxDispatcherOptions.SectionName,
    static options => options.Validate());
builder.Services.Configure<MassTransitHostOptions>(options =>
{
    options.WaitUntilStarted = false;
    options.StartTimeout = TimeSpan.FromSeconds(eventBusOptions.HostStartTimeoutSeconds);
    options.StopTimeout = TimeSpan.FromSeconds(eventBusOptions.HostStopTimeoutSeconds);
});

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<PassStationConsumer>();
    x.AddConsumer<DeviceLogConsumer>();
    x.AddConsumer<HourlyCapacityConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var connectionString = builder.Configuration.GetConnectionString(ConnectionResourceNames.EventBus)
            ?? throw new InvalidOperationException($"Missing {ConnectionResourceNames.EventBus} connection string.");
        cfg.Host(connectionString);

        cfg.ReceiveEndpoint(eventBusOptions.ResolveEndpointName(RabbitMqEndpointNames.PassStationBatches), endpoint =>
        {
            endpoint.ApplyIIoTEndpointDefaults(
                eventBusOptions,
                eventBusOptions.Consumers.PassStationConcurrentMessageLimit);
            endpoint.ConfigureConsumer<PassStationConsumer>(context);
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
    eventBusOptions.ResolveEndpointName(RabbitMqEndpointNames.PassStationBatches),
    eventBusOptions.ResolveEndpointName(RabbitMqEndpointNames.DeviceLogs),
    eventBusOptions.ResolveEndpointName(RabbitMqEndpointNames.HourlyCapacities)
};

startupLogger.LogInformation(
    "DataWorker queues configured queues={queues} error_queue_suffix={error_queue_suffix} skipped_queue_suffix={skipped_queue_suffix}",
    configuredQueues,
    "_error",
    "_skipped");

host.Run();

static bool IsHealthCheckMode(string[] args)
{
    return args.Any(arg => string.Equals(arg, "--healthcheck", StringComparison.OrdinalIgnoreCase));
}

static async Task<int> RunHealthCheckAsync(
    IServiceProvider services,
    IConfiguration configuration,
    CancellationToken cancellationToken)
{
    try
    {
        var requiredConnectionStrings = new[]
        {
            ConnectionResourceNames.IiotDatabase,
            ConnectionResourceNames.EventBus,
            "redis-cache"
        };

        var connectionStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in requiredConnectionStrings)
        {
            var connectionString = configuration.GetConnectionString(name);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.Error.WriteLine($"IIoT.DataWorker health check failed: missing connection string '{name}'.");
                return 1;
            }

            connectionStrings[name] = connectionString;
        }

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IIoTDbContext>();
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            Console.Error.WriteLine("IIoT.DataWorker health check failed: database connection is unavailable.");
            return 1;
        }

        var distributedCache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        await distributedCache.GetStringAsync("__iiot_dataworker_healthcheck__", cancellationToken);

        await EnsureTcpConnectAsync(
            connectionStrings[ConnectionResourceNames.EventBus],
            ConnectionResourceNames.EventBus,
            cancellationToken);

        Console.WriteLine("IIoT.DataWorker health check passed.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"IIoT.DataWorker health check failed: {ex.Message}");
        return 1;
    }
}

static async Task EnsureTcpConnectAsync(
    string connectionString,
    string connectionName,
    CancellationToken cancellationToken)
{
    if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
    {
        throw new InvalidOperationException($"Connection string '{connectionName}' must be an absolute URI.");
    }

    var port = uri.IsDefaultPort ? 5672 : uri.Port;
    using var client = new TcpClient();
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));
    await client.ConnectAsync(uri.Host, port, timeout.Token);
}
